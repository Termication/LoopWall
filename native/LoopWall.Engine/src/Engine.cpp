#include "LoopWallEngine.h"

const wchar_t* LoopWallEngine_GetVersion()
{
    return L"0.1.0";
}

int LoopWallEngine_Initialize()
{
    // Phase 1 native engine placeholder.
    // Future phases will initialize the Windows desktop host,
    // monitor enumeration, Media Foundation and Direct3D 11.
    return 0;
}
