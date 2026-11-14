#pragma once

#include <stdint.h>

#ifdef LOOPWALL_ENGINE_EXPORTS
#define LOOPWALL_API __declspec(dllexport)
#else
#define LOOPWALL_API __declspec(dllimport)
#endif

#ifdef __cplusplus
extern "C" {
#endif

// The engine exposes a deliberately small C ABI so the WPF application can
// load it through P/Invoke without sharing C++ runtime types.
typedef struct LoopWallEngineHandle LoopWallEngineHandle;

typedef void(__cdecl* LoopWallFrameCallback)(
    const uint8_t* pixels,
    uint32_t width,
    uint32_t height,
    uint32_t stride,
    int64_t presentationTimeHns,
    void* userData);

typedef void(__cdecl* LoopWallErrorCallback)(const wchar_t* message, void* userData);

LOOPWALL_API const wchar_t* __cdecl LoopWallEngine_GetVersion();
LOOPWALL_API int32_t __cdecl LoopWallEngine_Create(LoopWallEngineHandle** engine);
LOOPWALL_API void __cdecl LoopWallEngine_Destroy(LoopWallEngineHandle* engine);

// Opens a local file and starts a paused decode worker. Frame memory is valid
// only for the duration of LoopWallFrameCallback.
LOOPWALL_API int32_t __cdecl LoopWallEngine_Open(
    LoopWallEngineHandle* engine,
    const wchar_t* filePath,
    LoopWallFrameCallback frameCallback,
    LoopWallErrorCallback errorCallback,
    void* userData);

LOOPWALL_API int32_t __cdecl LoopWallEngine_Play(LoopWallEngineHandle* engine);
LOOPWALL_API int32_t __cdecl LoopWallEngine_Pause(LoopWallEngineHandle* engine);
LOOPWALL_API int32_t __cdecl LoopWallEngine_Stop(LoopWallEngineHandle* engine);
LOOPWALL_API int32_t __cdecl LoopWallEngine_SetLooping(LoopWallEngineHandle* engine, int32_t enabled);

#ifdef __cplusplus
}
#endif
