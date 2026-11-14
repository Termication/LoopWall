# LoopWall

LoopWall is a Windows animated-wallpaper application. The project uses a WPF front end and a native C++ engine.

## Current status — Phase 2 native playback diagnostic

Included:
- WPF desktop application shell
- Import multiple local video files
- Video library persisted between launches
- Local video preview using WPF MediaElement
- Play, pause and stop preview controls
- Preview looping
- JSON settings stored under `%LOCALAPPDATA%\LoopWall\settings.json`
- Native C++ Media Foundation source-reader engine with a small C ABI
- Managed x64 P/Invoke bridge with deterministic native-thread cleanup
- A separate native-engine test window with play, pause, stop and looping controls
- Bounded preview buffering: the WPF test surface retains at most one pending frame and drops later frames while it is busy
- CMake builds and copies `LoopWall.Engine.dll` beside the managed application during a normal x64 build

Not included yet:
- Applying video behind desktop icons
- Direct3D rendering
- Multi-monitor wallpaper placement
- Fullscreen-game detection / automatic pause

The original WPF `MediaElement` preview remains unchanged and separate from the native diagnostic preview. The native window is not a wallpaper renderer and does not use Direct3D yet.

The native engine requests Media Foundation video processing and hardware transforms where Windows can use them, then requests `RGB32` frames for the WPF diagnostic surface. That request is only a preference: this project does not detect or prove NVIDIA, AMD or Intel hardware decoding. Windows selects the actual source handler and decoder. See [Phase 2 verification](docs/PHASE2-VERIFICATION.md) for supported test expectations and manual checks.

## Requirements

- Windows 10 or Windows 11
- .NET 10 SDK
- Visual Studio 2022 with the **.NET desktop development** workload
- Visual Studio 2022 or Build Tools with **Desktop development with C++**, MSVC v143 x64 tools, and a Windows 10/11 SDK
- CMake 3.24+
- Windows 10 or 11 with Media Foundation available. Windows N editions need the Media Feature Pack.

## Build and run LoopWall (x64)

Open `LoopWall.sln`, select `Release | x64`, then build/run `LoopWall.App`.

Or from PowerShell in the repository root:

```powershell
dotnet restore .\src\LoopWall.App\LoopWall.App.csproj
dotnet build .\src\LoopWall.App\LoopWall.App.csproj -c Release -p:Platform=x64
dotnet run --project .\src\LoopWall.App\LoopWall.App.csproj -c Release -p:Platform=x64
```

The managed build configures CMake under `src\LoopWall.App\obj\native\cmake`, builds `LoopWall.Engine` with the Visual Studio 2022 x64 generator, and copies `LoopWall.Engine.dll` into the managed output folder. Do not copy a 32-bit DLL beside the x64 application.

## Build the native C++ engine directly

From the repository root:

```powershell
cmake -S .\native\LoopWall.Engine -B .\native\LoopWall.Engine\build -G "Visual Studio 17 2022" -A x64
cmake --build .\native\LoopWall.Engine\build --config Release --target LoopWall.Engine
```

The DLL is generated at `native\LoopWall.Engine\build\Release\LoopWall.Engine.dll` for the Visual Studio generator. The direct build is useful for troubleshooting; the regular .NET build still performs its own CMake build and copies the correct DLL automatically.

## Project layout

```text
LoopWall/
├── LoopWall.sln
├── README.md
├── src/
│   └── LoopWall.App/
│       ├── App.xaml
│       ├── MainWindow.xaml
│       ├── MainWindow.xaml.cs
│       ├── Interop/
│       │   └── NativePlaybackEngine.cs
│       ├── Models/
│       │   ├── AppSettings.cs
│       │   └── WallpaperVideo.cs
│       ├── Services/
│       │   ├── SettingsService.cs
│       │   └── VideoLibraryService.cs
│       └── Views/
│           ├── NativeEngineTestWindow.xaml
│           └── NativeEngineTestWindow.xaml.cs
├── native/
│   └── LoopWall.Engine/
│       ├── CMakeLists.txt
│       ├── include/
│       │   └── LoopWallEngine.h
│       └── src/
│           └── Engine.cpp
└── docs/
    └── PHASE2-VERIFICATION.md
```

## Settings and library

LoopWall stores settings and imported file paths in:

```text
%LOCALAPPDATA%\LoopWall\settings.json
```

The app stores file paths, not copies of the video files. If a video is moved or deleted, it will be skipped when the library is loaded.

## Development direction

1. Phase 1: WPF application foundation and local preview.
2. Phase 2: Native Media Foundation playback engine and diagnostic preview.
3. Phase 3: Windows desktop attachment and multi-monitor support.
4. Phase 4: Direct3D 11 rendering, hardware decode and resource policies.
5. Phase 5: Startup, power management and fullscreen-aware pause.
