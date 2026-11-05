# whichswitch

A keyboard indicator, because Windows doesn't show the keyboard layout on all screens.

whichswitch is a small Windows utility that shows the current keyboard layout
(e.g. `EN`, `RU`, `DE`) in the title and icon of its own taskbar button. If you
enable "Show my taskbar on all displays", the indicator is visible on every
monitor.

## How it works

- Event-driven only: it listens for shell hook messages
  (`RegisterShellHookWindow`). No polling and no background timers; a single
  short one-shot timer runs after a layout change, because Windows reports the
  change slightly before it takes effect.
- No global keyboard hooks: the tool never sees what you type.
- No network access and no third-party dependencies.

## Requirements

- Windows 10 or 11 (developed and tested on Windows 11)
- To build from source: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Install

There are no prebuilt releases yet, so build it from source:

```powershell
git clone https://github.com/truedrcb/whichswitch.git
cd whichswitch
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The result is `bin\Release\net8.0-windows\win-x64\publish\whichswitch.exe`, about
150 MB because it includes the .NET runtime. It needs no installer and no installed
.NET runtime. Copy just this file anywhere: the DLLs next to it aren't needed.

## Run

Double-click `whichswitch.exe`, or during development run it from the repository:

```powershell
dotnet run
```

A small always-on-top window appears and its taskbar button shows the current layout code.
Switch layouts as usual (Alt+Shift, Win+Space, ...) and the title and icon
update. Close the window to quit.

### Show it on every monitor

1. Open **Settings → Personalization → Taskbar → Taskbar behaviors**.
2. Enable **Show my taskbar on all displays**.
3. Optionally set **Show my taskbar apps on** to *All taskbars*.

### Start with Windows

Press `Win+R`, run `shell:startup`, and put a shortcut to `whichswitch.exe` in
the folder that opens.

## Configure icon colors

Icon background colors are set at build time in
[whichswitch.csproj](whichswitch.csproj). Add one line per language code, then rebuild:

```xml
<AssemblyMetadata Include="IconColor.Default" Value="#285AC8" />
<AssemblyMetadata Include="IconColor.RU" Value="#C82828" />
<AssemblyMetadata Include="IconColor.DE" Value="#2E8B57" />
```

Languages without their own line use `IconColor.Default`.

## Known limitations

- Tested only on Windows 11 with three layouts. The icon of a minimized window
  and multi-monitor behavior have not been verified yet.
- The indicator updates a fraction of a second after you switch layouts.

## Development

```powershell
dotnet build
```

All the code is in one file, [Program.cs](Program.cs). The Windows API declarations
and constants it uses are listed in [NativeMethods.txt](NativeMethods.txt) and
generated at build time by Microsoft's
[CsWin32](https://github.com/microsoft/CsWin32) from the official Windows metadata.
The package is used only during the build, so the first build needs network access
to download it, while the tool itself never uses the network.
