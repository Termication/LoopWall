#pragma once

#ifdef LOOPWALL_ENGINE_EXPORTS
#define LOOPWALL_API __declspec(dllexport)
#else
#define LOOPWALL_API __declspec(dllimport)
#endif

#ifdef __cplusplus
extern "C" {
#endif

enum LoopWallState
{
    LoopWallState_Idle = 0,
    LoopWallState_Opening = 1,
    LoopWallState_Playing = 2,
    LoopWallState_Paused = 3,
    LoopWallState_Stopped = 4,
    LoopWallState_Ended = 5,
    LoopWallState_Error = 6
};

enum LoopWallResult
{
    LoopWall_Ok = 0,
    LoopWall_NotInitialized = 1,
    LoopWall_InvalidArgument = 2,
    LoopWall_NotReady = 3,
    LoopWall_MediaFailed = 4,
    LoopWall_Timeout = 5
};

LOOPWALL_API const wchar_t* LoopWallEngine_GetVersion();
LOOPWALL_API int LoopWallEngine_Initialize();
LOOPWALL_API void LoopWallEngine_Shutdown();
LOOPWALL_API int LoopWallEngine_AttachWindow(void* hwnd);
LOOPWALL_API int LoopWallEngine_Resize(int width, int height);
LOOPWALL_API int LoopWallEngine_Open(const wchar_t* path);
LOOPWALL_API int LoopWallEngine_Play();
LOOPWALL_API int LoopWallEngine_Pause();
LOOPWALL_API int LoopWallEngine_Stop();
LOOPWALL_API int LoopWallEngine_SetLoop(int enabled);
LOOPWALL_API int LoopWallEngine_SetVolume(float volume);
LOOPWALL_API int LoopWallEngine_SetMute(int mute);
LOOPWALL_API int LoopWallEngine_GetState();
LOOPWALL_API long long LoopWallEngine_GetPositionMs();
LOOPWALL_API long long LoopWallEngine_GetDurationMs();
LOOPWALL_API int LoopWallEngine_HasVideo();
LOOPWALL_API int LoopWallEngine_CopyLastError(wchar_t* buffer, int bufferChars);

#ifdef __cplusplus
}
#endif
