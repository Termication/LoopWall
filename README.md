# LoopWall

LoopWall is a Windows animated-wallpaper application. The project uses a WPF front end and a native C++ engine.

## Phase 1 status

Included:
- WPF desktop application shell
- Import multiple local video files
- Video library persisted between launches
- Local video preview using WPF MediaElement
- Play, pause and stop preview controls
- Preview looping
- JSON settings stored under `%LOCALAPPDATA%\LoopWall\settings.json`
- C++ native engine DLL scaffold built with CMake

Not included yet:
- Applying video behind desktop icons
- Direct3D rendering
- Hardware decoding control
- Multi-monitor wallpaper placement
- Fullscreen-game detection / automatic pause

The Phase 1 preview player is not the final wallpaper playback engine. WPF MediaElement support depends on Windows codecs installed on the machine.

## Requirements

- Windows 10 or Windows 11
- .NET 10 SDK
- Visual Studio 2022 with the .NET desktop development workload, or an editor with the .NET SDK
- CMake 3.24+
- Visual Studio C++ build tools (MSVC) for the native DLL

## Build and run the WPF application

Open `LoopWall.sln` in Visual Studio and build/run `LoopWall.App`.

Or from PowerShell in the repository root:

```powershell
dotnet restore .\src\LoopWall.App\LoopWall.App.csproj
dotnet build .\src\LoopWall.App\LoopWall.App.csproj -c Release
dotnet run --project .\src\LoopWall.App\LoopWall.App.csproj
```

## Build the native C++ engine scaffold

From the repository root:

```powershell
cmake -S .\native\LoopWall.Engine -B .\native\LoopWall.Engine\build -A x64
cmake --build .\native\LoopWall.Engine\build --config Release
```

The DLL is generated in the CMake build output directory.

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
│       ├── Models/
│       │   ├── AppSettings.cs
│       │   └── WallpaperVideo.cs
│       └── Services/
│           ├── SettingsService.cs
│           └── VideoLibraryService.cs
└── native/
    └── LoopWall.Engine/
        ├── CMakeLists.txt
        ├── include/
        │   └── LoopWallEngine.h
        └── src/
            └── Engine.cpp
```

## Settings and library

LoopWall stores settings and imported file paths in:

```text
%LOCALAPPDATA%\LoopWall\settings.json
```

The app stores file paths, not copies of the video files. If a video is moved or deleted, it will be skipped when the library is loaded.

## Development direction

1. Phase 1: WPF application foundation and local preview.
2. Phase 2: Native playback engine and robust looping.
3. Phase 3: Windows desktop attachment and multi-monitor support.
4. Phase 4: Direct3D 11 rendering, hardware decode and resource policies.
5. Phase 5: Startup, power management and fullscreen-aware pause.
