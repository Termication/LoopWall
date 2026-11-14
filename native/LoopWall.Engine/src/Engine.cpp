#include "LoopWallEngine.h"

#include <windows.h>
#include <mfapi.h>
#include <mferror.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <wrl/client.h>

#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <mutex>
#include <new>
#include <string>
#include <system_error>
#include <thread>
#include <utility>

using Microsoft::WRL::ComPtr;

namespace
{
    constexpr uint32_t kMaxFrameDimension = 16'384;

    std::wstring FormatFailure(const wchar_t* operation, HRESULT result)
    {
        wchar_t* systemMessage = nullptr;
        const auto messageLength = FormatMessageW(
            FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
            nullptr,
            static_cast<DWORD>(result),
            0,
            reinterpret_cast<wchar_t*>(&systemMessage),
            0,
            nullptr);

        std::wstring message = operation;
        message += L" failed (0x";
        wchar_t code[16]{};
        swprintf_s(code, L"%08X", static_cast<unsigned int>(result));
        message += code;
        message += L")";

        if (messageLength > 0 && systemMessage != nullptr)
        {
            message += L": ";
            message.append(systemMessage, messageLength);
            LocalFree(systemMessage);
        }

        return message;
    }

    HRESULT ReadVideoFormat(IMFSourceReader* reader, uint32_t& width, uint32_t& height, uint32_t& stride)
    {
        ComPtr<IMFMediaType> mediaType;
        HRESULT result = reader->GetCurrentMediaType(MF_SOURCE_READER_FIRST_VIDEO_STREAM, &mediaType);
        if (FAILED(result))
            return result;

        result = MFGetAttributeSize(mediaType.Get(), MF_MT_FRAME_SIZE, &width, &height);
        if (FAILED(result))
            return result;

        if (width == 0 || height == 0 || width > kMaxFrameDimension || height > kMaxFrameDimension)
            return MF_E_INVALIDMEDIATYPE;

        uint32_t rawStride = 0;
        result = mediaType->GetUINT32(MF_MT_DEFAULT_STRIDE, &rawStride);
        if (FAILED(result))
        {
            LONG calculatedStride = 0;
            result = MFGetStrideForBitmapInfoHeader(MFVideoFormat_RGB32.Data1, width, &calculatedStride);
            if (FAILED(result) || calculatedStride <= 0)
                return FAILED(result) ? result : MF_E_INVALIDMEDIATYPE;

            rawStride = static_cast<uint32_t>(calculatedStride);
        }

        if (rawStride == 0)
            return MF_E_INVALIDMEDIATYPE;

        stride = rawStride;
        return S_OK;
    }
}

class PlaybackEngine
{
public:
    ~PlaybackEngine()
    {
        Stop();
    }

    HRESULT Open(
        const wchar_t* filePath,
        LoopWallFrameCallback frameCallback,
        LoopWallErrorCallback errorCallback,
        void* userData)
    {
        if (filePath == nullptr || *filePath == L'\0' || frameCallback == nullptr)
            return E_INVALIDARG;

        const HRESULT stopResult = Stop();
        if (FAILED(stopResult))
            return stopResult;

        {
            std::lock_guard lock(m_mutex);
            m_filePath = filePath;
            m_frameCallback = frameCallback;
            m_errorCallback = errorCallback;
            m_userData = userData;
            m_stopRequested = false;
            m_playRequested = false;
            m_opened = true;
        }

        try
        {
            m_worker = std::thread(&PlaybackEngine::DecodeWorker, this);
        }
        catch (const std::system_error&)
        {
            std::lock_guard lock(m_mutex);
            m_opened = false;
            return E_FAIL;
        }

        return S_OK;
    }

    HRESULT Play()
    {
        {
            std::lock_guard lock(m_mutex);
            if (!m_opened)
                return MF_E_NOT_INITIALIZED;

            m_playRequested = true;
        }

        m_playbackChanged.notify_all();
        return S_OK;
    }

    HRESULT Pause()
    {
        {
            std::lock_guard lock(m_mutex);
            if (!m_opened)
                return MF_E_NOT_INITIALIZED;

            m_playRequested = false;
        }

        m_playbackChanged.notify_all();
        return S_OK;
    }

    HRESULT Stop()
    {
        std::thread worker;

        {
            std::lock_guard lock(m_mutex);
            if (!m_worker.joinable())
            {
                m_opened = false;
                return S_OK;
            }

            // Stop must be initiated by the managed/UI thread, never by one of
            // the native frame callbacks running on the decode thread.
            if (m_worker.get_id() == std::this_thread::get_id())
                return E_FAIL;

            m_stopRequested = true;
            m_playRequested = true;
            worker = std::move(m_worker);
        }

        m_playbackChanged.notify_all();
        worker.join();

        {
            std::lock_guard lock(m_mutex);
            m_opened = false;
            m_playRequested = false;
        }

        return S_OK;
    }

    HRESULT SetLooping(const bool enabled)
    {
        std::lock_guard lock(m_mutex);
        m_looping = enabled;
        return S_OK;
    }

private:
    bool WaitForPlayback()
    {
        std::unique_lock lock(m_mutex);
        m_playbackChanged.wait(lock, [this]
        {
            return m_stopRequested || m_playRequested;
        });

        return !m_stopRequested;
    }

    bool WaitForPresentationTime(
        const int64_t presentationTimeHns,
        bool& clockStarted,
        int64_t& clockBaseHns,
        std::chrono::steady_clock::time_point& clockBaseTime)
    {
        std::unique_lock lock(m_mutex);
        if (m_stopRequested || !m_playRequested)
            return false;

        if (!clockStarted || presentationTimeHns < clockBaseHns)
        {
            clockStarted = true;
            clockBaseHns = presentationTimeHns;
            clockBaseTime = std::chrono::steady_clock::now();
            return true;
        }

        const auto offset = std::chrono::nanoseconds((presentationTimeHns - clockBaseHns) * 100);
        const auto targetTime = clockBaseTime + offset;
        m_playbackChanged.wait_until(lock, targetTime, [this]
        {
            return m_stopRequested || !m_playRequested;
        });

        return !m_stopRequested && m_playRequested;
    }

    bool IsLooping() const
    {
        std::lock_guard lock(m_mutex);
        return m_looping;
    }

    bool IsStopping() const
    {
        std::lock_guard lock(m_mutex);
        return m_stopRequested;
    }

    void ReportError(const std::wstring& message)
    {
        LoopWallErrorCallback callback = nullptr;
        void* userData = nullptr;

        {
            std::lock_guard lock(m_mutex);
            callback = m_errorCallback;
            userData = m_userData;
        }

        if (callback != nullptr)
            callback(message.c_str(), userData);
    }

    void DeliverFrame(
        const uint8_t* pixels,
        const uint32_t width,
        const uint32_t height,
        const uint32_t stride,
        const int64_t presentationTimeHns)
    {
        LoopWallFrameCallback callback = nullptr;
        void* userData = nullptr;

        {
            std::lock_guard lock(m_mutex);
            callback = m_frameCallback;
            userData = m_userData;
        }

        if (callback != nullptr)
            callback(pixels, width, height, stride, presentationTimeHns, userData);
    }

    void DecodeWorker()
    {
        std::wstring filePath;
        {
            std::lock_guard lock(m_mutex);
            filePath = m_filePath;
        }

        const HRESULT apartmentResult = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        const bool apartmentInitialized = SUCCEEDED(apartmentResult);
        if (FAILED(apartmentResult))
        {
            ReportError(FormatFailure(L"COM initialization", apartmentResult));
            return;
        }

        const HRESULT startupResult = MFStartup(MF_VERSION, MFSTARTUP_LITE);
        if (FAILED(startupResult))
        {
            ReportError(FormatFailure(L"Media Foundation startup", startupResult));
            CoUninitialize();
            return;
        }

        ComPtr<IMFAttributes> attributes;
        HRESULT result = MFCreateAttributes(&attributes, 3);
        if (SUCCEEDED(result))
            result = attributes->SetUINT32(MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, TRUE);
        if (SUCCEEDED(result))
            result = attributes->SetUINT32(MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, TRUE);
        if (SUCCEEDED(result))
            result = attributes->SetUINT32(MF_LOW_LATENCY, TRUE);

        ComPtr<IMFSourceReader> reader;
        if (SUCCEEDED(result))
            result = MFCreateSourceReaderFromURL(filePath.c_str(), attributes.Get(), &reader);
        if (SUCCEEDED(result))
            result = reader->SetStreamSelection(MF_SOURCE_READER_ALL_STREAMS, FALSE);
        if (SUCCEEDED(result))
            result = reader->SetStreamSelection(MF_SOURCE_READER_FIRST_VIDEO_STREAM, TRUE);

        ComPtr<IMFMediaType> outputType;
        if (SUCCEEDED(result))
            result = MFCreateMediaType(&outputType);
        if (SUCCEEDED(result))
            result = outputType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
        if (SUCCEEDED(result))
            result = outputType->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_RGB32);
        if (SUCCEEDED(result))
            result = reader->SetCurrentMediaType(MF_SOURCE_READER_FIRST_VIDEO_STREAM, nullptr, outputType.Get());

        uint32_t width = 0;
        uint32_t height = 0;
        uint32_t stride = 0;
        if (SUCCEEDED(result))
            result = ReadVideoFormat(reader.Get(), width, height, stride);

        if (FAILED(result))
        {
            if (!IsStopping())
                ReportError(FormatFailure(L"Opening the video in Media Foundation", result));

            reader.Reset();
            outputType.Reset();
            attributes.Reset();
            MFShutdown();
            CoUninitialize();
            return;
        }

        bool clockStarted = false;
        int64_t clockBaseHns = 0;
        std::chrono::steady_clock::time_point clockBaseTime{};

        while (!IsStopping())
        {
            if (!WaitForPlayback())
                break;

            DWORD streamIndex = 0;
            DWORD streamFlags = 0;
            LONGLONG sampleTimeHns = 0;
            ComPtr<IMFSample> sample;

            result = reader->ReadSample(
                MF_SOURCE_READER_FIRST_VIDEO_STREAM,
                0,
                &streamIndex,
                &streamFlags,
                &sampleTimeHns,
                &sample);

            if (FAILED(result) || (streamFlags & MF_SOURCE_READERF_ERROR) != 0)
            {
                if (!IsStopping())
                    ReportError(FormatFailure(L"Decoding the video", FAILED(result) ? result : E_FAIL));
                break;
            }

            if ((streamFlags & MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED) != 0)
            {
                result = ReadVideoFormat(reader.Get(), width, height, stride);
                if (FAILED(result))
                {
                    if (!IsStopping())
                        ReportError(FormatFailure(L"Updating the video format", result));
                    break;
                }
            }

            if ((streamFlags & MF_SOURCE_READERF_ENDOFSTREAM) != 0)
            {
                if (!IsLooping())
                    break;

                PROPVARIANT position{};
                position.vt = VT_I8;
                position.hVal.QuadPart = 0;
                result = reader->SetCurrentPosition(GUID_NULL, position);
                if (FAILED(result))
                {
                    if (!IsStopping())
                        ReportError(FormatFailure(L"Looping the video", result));
                    break;
                }

                clockStarted = false;
                continue;
            }

            if (sample == nullptr)
                continue;

            if (!WaitForPresentationTime(sampleTimeHns, clockStarted, clockBaseHns, clockBaseTime))
            {
                // A pause resets the local presentation clock. This deliberately
                // drops a decoded frame rather than retaining unbounded buffers.
                clockStarted = false;
                continue;
            }

            ComPtr<IMFMediaBuffer> buffer;
            result = sample->ConvertToContiguousBuffer(&buffer);
            if (FAILED(result))
            {
                if (!IsStopping())
                    ReportError(FormatFailure(L"Reading a decoded frame", result));
                break;
            }

            BYTE* pixels = nullptr;
            DWORD maximumLength = 0;
            DWORD currentLength = 0;
            result = buffer->Lock(&pixels, &maximumLength, &currentLength);
            if (FAILED(result))
            {
                if (!IsStopping())
                    ReportError(FormatFailure(L"Locking a decoded frame", result));
                break;
            }

            const uint64_t expectedLength = static_cast<uint64_t>(stride) * height;
            if (pixels == nullptr || currentLength < expectedLength)
            {
                buffer->Unlock();
                if (!IsStopping())
                    ReportError(L"A decoded frame was shorter than the advertised BGRA frame size.");
                break;
            }

            DeliverFrame(pixels, width, height, stride, sampleTimeHns);
            buffer->Unlock();
        }

        reader.Reset();
        outputType.Reset();
        attributes.Reset();
        MFShutdown();
        if (apartmentInitialized)
            CoUninitialize();
    }

    mutable std::mutex m_mutex;
    std::condition_variable m_playbackChanged;
    std::thread m_worker;
    std::wstring m_filePath;
    LoopWallFrameCallback m_frameCallback = nullptr;
    LoopWallErrorCallback m_errorCallback = nullptr;
    void* m_userData = nullptr;
    bool m_stopRequested = false;
    bool m_playRequested = false;
    bool m_looping = true;
    bool m_opened = false;
};

struct LoopWallEngineHandle
{
    PlaybackEngine playback;
};

extern "C"
{
    LOOPWALL_API const wchar_t* __cdecl LoopWallEngine_GetVersion()
    {
        return L"0.2.0";
    }

    LOOPWALL_API int32_t __cdecl LoopWallEngine_Create(LoopWallEngineHandle** engine)
    {
        if (engine == nullptr)
            return static_cast<int32_t>(E_POINTER);

        *engine = nullptr;
        try
        {
            *engine = new LoopWallEngineHandle();
            return static_cast<int32_t>(S_OK);
        }
        catch (const std::bad_alloc&)
        {
            return static_cast<int32_t>(E_OUTOFMEMORY);
        }
        catch (...)
        {
            return static_cast<int32_t>(E_FAIL);
        }
    }

    LOOPWALL_API void __cdecl LoopWallEngine_Destroy(LoopWallEngineHandle* engine)
    {
        delete engine;
    }

    LOOPWALL_API int32_t __cdecl LoopWallEngine_Open(
        LoopWallEngineHandle* engine,
        const wchar_t* filePath,
        LoopWallFrameCallback frameCallback,
        LoopWallErrorCallback errorCallback,
        void* userData)
    {
        if (engine == nullptr)
            return static_cast<int32_t>(E_POINTER);

        try
        {
            return static_cast<int32_t>(engine->playback.Open(filePath, frameCallback, errorCallback, userData));
        }
        catch (...)
        {
            return static_cast<int32_t>(E_FAIL);
        }
    }

    LOOPWALL_API int32_t __cdecl LoopWallEngine_Play(LoopWallEngineHandle* engine)
    {
        return engine == nullptr ? static_cast<int32_t>(E_POINTER) : static_cast<int32_t>(engine->playback.Play());
    }

    LOOPWALL_API int32_t __cdecl LoopWallEngine_Pause(LoopWallEngineHandle* engine)
    {
        return engine == nullptr ? static_cast<int32_t>(E_POINTER) : static_cast<int32_t>(engine->playback.Pause());
    }

    LOOPWALL_API int32_t __cdecl LoopWallEngine_Stop(LoopWallEngineHandle* engine)
    {
        return engine == nullptr ? static_cast<int32_t>(E_POINTER) : static_cast<int32_t>(engine->playback.Stop());
    }

    LOOPWALL_API int32_t __cdecl LoopWallEngine_SetLooping(LoopWallEngineHandle* engine, const int32_t enabled)
    {
        return engine == nullptr
            ? static_cast<int32_t>(E_POINTER)
            : static_cast<int32_t>(engine->playback.SetLooping(enabled != 0));
    }
}
