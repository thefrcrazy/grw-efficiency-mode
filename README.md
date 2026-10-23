# GRW Efficiency Mode

[![Direct Download](https://img.shields.io/badge/Direct-Download-2ea44f?style=for-the-badge)](https://github.com/thefrcrazy/grw-efficiency-mode/releases/latest/download/GRW-Efficiency-Setup.exe)

👉 https://github.com/thefrcrazy/grw-efficiency-mode/releases/latest/download/GRW-Efficiency-Setup.exe

A small Windows tool that launches **Tom Clancy's Ghost Recon Wildlands** and applies Windows **Efficiency Mode** (EcoQoS) to the game while it starts up, then gives control back to Windows.

## Features

- **Detects the game automatically**: Steam version (checked first, in every Steam library) or Ubisoft Connect version.
- **Launches the game** through the right platform.
- **Enables Efficiency Mode** on every new `GRW.exe` instance during startup.
- **Restores normal behavior** 120 seconds after the last new instance.
- **Exits on its own** 15 seconds after the game is closed, or after 5 minutes if the game never starts.
- **Never changes process priority.**
- **Taskbar status**: its icon stays in the taskbar while the game watcher is active. Click it to see whether the game is starting, running with Efficiency Mode active, or has closed.
- **No administrator rights** needed, to install or to run.

## Requirements

- Windows 10 (21H2 or later) or Windows 11
- .NET Framework 4.x (built into Windows)
- Ghost Recon Wildlands installed via Steam or Ubisoft Connect

## Installation

1. Click **Download Here** above to download `GRW-Efficiency-Setup.exe`.
2. Run it. If Windows blocks it, see [Windows warnings](#windows-warnings) below.
3. Review the install location (`%LOCALAPPDATA%\Programs\GRW-Efficiency-Mode`), shown as read-only.
4. Choose the options, then click **Install**:
   - add a shortcut to the Desktop;
   - delete the setup file when done.

The installer uses the green-tinted Ghost Recon skull in `src/app.ico`. When the game is installed, the installed app and its shortcuts use the same icon; otherwise they use `src/app.ico`. To pin a shortcut: right-click it → **Pin to taskbar** or **Pin to Start**.

## Usage

Launch the game with the **GRW Efficiency Mode** shortcut instead of Steam or Ubisoft Connect.

Log file: `%LOCALAPPDATA%\GRW-Efficiency-Mode\GRW-Efficiency-Mode.log`

## Windows warnings

The executable is not code-signed, so Windows may warn on the first run:

- **SmartScreen** ("Windows protected your PC"): click **More info** → **Run anyway**.
- **Smart App Control** blocks the file with no "Run anyway" option. Right-click the file → **Properties** → tick **Unblock** → **OK**, then run it again. This may not be enough on every PC.

Once installed, the app no longer carries the "downloaded from the Internet" mark.

## How it works

The file name decides the mode. If it contains `Setup`, the program installs itself: it copies itself as `GRW-Efficiency-Mode.exe` and creates the shortcuts. Any other name launches the game and applies Efficiency Mode.

The app only uses public Windows APIs. It runs `SetProcessInformation` with `ProcessPowerThrottling` to turn EcoQoS on, then resets it so Windows decides again.

## Settings

The timings are constants at the top of the `Launcher` class in `src/GRWEfficiencyMode.cs`:

| Constant | Default | Meaning |
|---|---|---|
| `RestoreDelaySeconds` | 120 | Delay before Efficiency Mode is removed |
| `MaxWatchSeconds` | 600 | Hard limit after the first detection |
| `MaxWaitForGameSeconds` | 300 | Give up if the game never starts |
| `GraceAfterCloseSeconds` | 15 | Game gone for this long = closed |

## Building

Run `build.cmd` on Windows. It uses the C# compiler built into Windows and writes `dist\GRW-Efficiency-Setup.exe`.

The GitHub Actions workflow builds the same file on every push to `main` and `v*` tag. It uploads an artifact for each build and attaches tagged builds to a release.

## Uninstall

Use **Uninstall GRW Efficiency Mode** in the Start menu, or run the setup again and click **Uninstall**. This removes the app, its shortcuts, its log files, and the now-empty install folder.

## Repository layout

| Path | Content |
|---|---|
| `src/GRWEfficiencyMode.cs` | Full source code (setup + launcher) |
| `src/app.ico` | Application icon |
| `src/app.manifest` | Windows manifest (no administrator rights) |
| `build.cmd` | Local build script |
| `.github/workflows/build.yml` | Automatic build and release |

## License

MIT — see `LICENSE`. Ghost Recon Wildlands is a trademark of Ubisoft; this project is not affiliated with Ubisoft or Valve.
