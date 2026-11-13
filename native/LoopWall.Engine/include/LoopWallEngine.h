#pragma once

#ifdef LOOPWALL_ENGINE_EXPORTS
#define LOOPWALL_API __declspec(dllexport)
#else
#define LOOPWALL_API __declspec(dllimport)
#endif

#ifdef __cplusplus
extern "C" {
#endif

LOOPWALL_API const wchar_t* LoopWallEngine_GetVersion();
LOOPWALL_API int LoopWallEngine_Initialize();

#ifdef __cplusplus
}
#endif
