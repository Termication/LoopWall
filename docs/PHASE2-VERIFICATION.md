# Phase 2 native-engine verification

This phase adds a diagnostic playback path. It decodes the first video stream through Windows Media Foundation, converts it to `RGB32`/BGRA-compatible frames, and displays those frames in a separate WPF window. It is not the live wallpaper path; desktop attachment, Direct3D rendering, monitor orchestration, and automatic power policies remain later phases.

## What the engine does

- Each `NativePlaybackEngine` instance owns one native worker thread and one local video source.
- The worker initializes COM and Media Foundation on that thread, enables Media Foundation video processing and requests hardware transforms as a best-effort preference.
- It activates only the first video stream, asks the source reader for `RGB32` output, and sends a borrowed decoded-frame pointer through a C callback.
- At end of stream, it seeks to zero when looping is enabled. It never retains an unbounded queue of decoded frames.
- The WPF test window copies at most one frame into an `ArrayPool<byte>` buffer. If the UI has not rendered that frame yet, later decoded frames are dropped.
- Stop/destroy signals the worker and joins it before the managed callback handle is released.

The existing WPF `MediaElement` preview is intentionally independent. A successful WPF preview does not prove native playback, and a successful native preview does not change the desktop wallpaper.

## Prerequisites

Install Visual Studio 2022 (or Build Tools) with:

- Desktop development with C++
- MSVC v143 x64/x86 build tools
- Windows 10 or Windows 11 SDK
- .NET desktop development

Also install CMake 3.24 or later and a .NET 10 SDK. Windows N editions need the Media Feature Pack for Media Foundation. In a Developer PowerShell, this command should find the native compiler:

```powershell
where cl
```

## Build the integrated x64 application

From the repository root:

```powershell
dotnet restore .\src\LoopWall.App\LoopWall.App.csproj
dotnet build .\src\LoopWall.App\LoopWall.App.csproj -c Release -p:Platform=x64
```

The project invokes CMake with the Visual Studio 2022 x64 generator and copies the resulting DLL to:

```text
src\LoopWall.App\bin\x64\Release\net10.0-windows\LoopWall.Engine.dll
```

Confirm the file exists before starting the application:

```powershell
Test-Path .\src\LoopWall.App\bin\x64\Release\net10.0-windows\LoopWall.Engine.dll
```

If you change CMake generators or repair the C++ workload, remove only this generated build folder, then rebuild:

```powershell
Remove-Item .\src\LoopWall.App\obj\native\cmake -Recurse
```

Do not remove project source folders or copy an x86 DLL into the x64 output directory.

## Manual functional test

Use a short local MP4 with H.264 video for the first test.

1. Start LoopWall and import the file.
2. Verify the existing WPF preview plays, pauses, stops, and loops as it did before Phase 2.
3. Select the video and choose **Native engine test**.
4. Confirm the diagnostic window changes from “Waiting for first native frame” to a visible image and reports nonzero dimensions.
5. Select **Pause**. The displayed native frame should stop advancing.
6. Select **Play**. Frame updates should resume.
7. Let the entire video finish. The engine should seek to the beginning and continue; a perfectly seamless visual loop is not guaranteed because the source file may have different first and last frames.
8. Select **Stop**, then close the window. Reopen the test window to start a new native decode worker.
9. Close LoopWall and reopen it. Confirm the original video library and WPF preview still work.

## Codec and hardware limitations

For first-time validation, use an MP4/H.264 file. Media Foundation availability is controlled by Windows and installed extensions:

- MP4/H.264 is the most likely stock-Windows success case.
- H.265/HEVC may require a Windows HEVC extension and supported hardware/software decoder.
- WMV/ASF, AVI, MOV, MKV, WebM, VP9, and AV1 depend on the installed source handler and codec. Importing a file does not mean either preview can decode it.
- The native engine activates the video stream only; it does not play audio.

`MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS` is a request, not a guarantee of hardware video decoding. This phase copies decoded frames to CPU memory for the WPF diagnostic display, so even a hardware-assisted decode path still has a CPU copy. A successful test therefore does not benchmark performance or prove use of a specific GPU. If you need to investigate a supported machine, use Windows GPU/Video Decode counters or an ETW trace while testing and treat the result as machine- and codec-specific.

## Troubleshooting

| Symptom | Likely cause and next check |
| --- | --- |
| CMake cannot find “Visual Studio 17 2022” | Install/repair the C++ workload and MSVC v143 tools, then use a Developer PowerShell. |
| `DllNotFoundException` | Run the normal integrated x64 build and confirm `LoopWall.Engine.dll` is beside `LoopWall.dll`. |
| `BadImageFormatException` | Managed app and native DLL use different architectures. Rebuild both as x64. |
| Native window reports a Media Foundation error | Try a local MP4/H.264 file, then check Media Feature Pack/codecs and the file path. |
| Native video is upside down or has corrupt lines | Record the file, resolution, codec, Windows version, and the displayed stride; this is a format/decoder compatibility issue to fix before wallpaper rendering. |
| WPF preview works but native preview fails | This is expected for some codecs: `MediaElement` and the source-reader path do not necessarily select the same components. |

## Verification boundary

The native build and real video playback must be verified on a Windows installation with MSVC and Media Foundation components available. Compilation alone cannot establish codec support, hardware decoding, CPU/GPU use, or a seamless source-video loop.
