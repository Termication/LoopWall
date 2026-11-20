#pragma once

#include "LoopWallEngine.h"

namespace LoopWall
{
bool EngineInitialize();
void EngineShutdown();
int EngineAttachWindow(void* hwnd);
int EngineResize(int width, int height);
int EngineOpen(const wchar_t* path);
int EnginePlay();
int EnginePause();
int EngineStop();
int EngineSetLoop(int enabled);
int EngineSetVolume(float volume);
int EngineSetMute(int mute);
int EngineGetState();
long long EngineGetPositionMs();
long long EngineGetDurationMs();
int EngineHasVideo();
int EngineCopyLastError(wchar_t* buffer, int bufferChars);
}
