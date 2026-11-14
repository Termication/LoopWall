using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace LoopWall.App.Interop;

/// <summary>
/// Managed ownership and P/Invoke boundary for the x64 Media Foundation engine.
/// A frame pointer is borrowed: subscribers must copy it before their handler returns.
/// </summary>
public sealed class NativePlaybackEngine : IDisposable
{
    private const string NativeLibrary = "LoopWall.Engine.dll";

    private readonly NativeFrameCallback _frameCallback;
    private readonly NativeErrorCallback _errorCallback;
    private GCHandle _selfHandle;
    private IntPtr _handle;
    private bool _disposed;

    public NativePlaybackEngine()
    {
        _frameCallback = OnNativeFrame;
        _errorCallback = OnNativeError;
        _selfHandle = GCHandle.Alloc(this);

        try
        {
            ThrowIfFailed(LoopWallEngine_Create(out _handle), "Creating the native playback engine");
        }
        catch
        {
            _selfHandle.Free();
            throw;
        }
    }

    public event EventHandler<NativeFrameEventArgs>? FrameReady;
    public event EventHandler<NativeEngineErrorEventArgs>? Error;

    public static string Version
    {
        get
        {
            var version = Marshal.PtrToStringUni(LoopWallEngine_GetVersion());
            return string.IsNullOrWhiteSpace(version) ? "Unknown" : version;
        }
    }

    /// <summary>Starts a paused decode worker for a local video file.</summary>
    public void Open(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ThrowIfDisposed();

        if (!File.Exists(filePath))
            throw new FileNotFoundException("The selected video file no longer exists.", filePath);

        ThrowIfFailed(
            LoopWallEngine_Open(
                _handle,
                filePath,
                _frameCallback,
                _errorCallback,
                GCHandle.ToIntPtr(_selfHandle)),
            "Opening the video in the native playback engine");
    }

    public void Play()
    {
        ThrowIfDisposed();
        ThrowIfFailed(LoopWallEngine_Play(_handle), "Starting native playback");
    }

    public void Pause()
    {
        ThrowIfDisposed();
        ThrowIfFailed(LoopWallEngine_Pause(_handle), "Pausing native playback");
    }

    public void Stop()
    {
        ThrowIfDisposed();
        ThrowIfFailed(LoopWallEngine_Stop(_handle), "Stopping native playback");
    }

    public void SetLooping(bool enabled)
    {
        ThrowIfDisposed();
        ThrowIfFailed(LoopWallEngine_SetLooping(_handle, enabled ? 1 : 0), "Configuring native playback looping");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_handle != IntPtr.Zero)
        {
            // Destroy synchronously joins the native decode thread, so no callback
            // can reference this GCHandle after the call returns.
            LoopWallEngine_Destroy(_handle);
            _handle = IntPtr.Zero;
        }

        if (_selfHandle.IsAllocated)
            _selfHandle.Free();

        GC.SuppressFinalize(this);
    }

    private static void OnNativeFrame(
        IntPtr pixels,
        uint width,
        uint height,
        uint stride,
        long presentationTimeHns,
        IntPtr userData)
    {
        try
        {
            if (GCHandle.FromIntPtr(userData).Target is NativePlaybackEngine engine && !engine._disposed)
            {
                engine.FrameReady?.Invoke(engine,
                    new NativeFrameEventArgs(pixels, width, height, stride, presentationTimeHns));
            }
        }
        catch
        {
            // Exceptions must never escape an unmanaged callback boundary.
        }
    }

    private static void OnNativeError([MarshalAs(UnmanagedType.LPWStr)] string message, IntPtr userData)
    {
        try
        {
            if (GCHandle.FromIntPtr(userData).Target is NativePlaybackEngine engine && !engine._disposed)
                engine.Error?.Invoke(engine, new NativeEngineErrorEventArgs(message));
        }
        catch
        {
            // See OnNativeFrame: native code must remain insulated from handlers.
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static void ThrowIfFailed(int hresult, string operation)
    {
        if (hresult < 0)
            throw new NativePlaybackException(operation, hresult);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NativeFrameCallback(
        IntPtr pixels,
        uint width,
        uint height,
        uint stride,
        long presentationTimeHns,
        IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private delegate void NativeErrorCallback([MarshalAs(UnmanagedType.LPWStr)] string message, IntPtr userData);

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern IntPtr LoopWallEngine_GetVersion();

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int LoopWallEngine_Create(out IntPtr engine);

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int LoopWallEngine_Open(
        IntPtr engine,
        [MarshalAs(UnmanagedType.LPWStr)] string filePath,
        NativeFrameCallback frameCallback,
        NativeErrorCallback errorCallback,
        IntPtr userData);

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int LoopWallEngine_Play(IntPtr engine);

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int LoopWallEngine_Pause(IntPtr engine);

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int LoopWallEngine_Stop(IntPtr engine);

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int LoopWallEngine_SetLooping(IntPtr engine, int enabled);

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern void LoopWallEngine_Destroy(IntPtr engine);
}

public sealed class NativeFrameEventArgs(
    IntPtr pixels,
    uint width,
    uint height,
    uint stride,
    long presentationTimeHns) : EventArgs
{
    public IntPtr Pixels { get; } = pixels;
    public uint Width { get; } = width;
    public uint Height { get; } = height;
    public uint Stride { get; } = stride;
    public long PresentationTimeHns { get; } = presentationTimeHns;
}

public sealed class NativeEngineErrorEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}

public sealed class NativePlaybackException(string operation, int hresult)
    : Exception($"{operation} failed (0x{hresult:X8}).", new Win32Exception(hresult))
{
    public int HResultCode { get; } = hresult;
}
