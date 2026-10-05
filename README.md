# ShowDesktopOneMonitor

## Description
Adds abillity to Show Desktop (Win + D) only for One Monitor!
Works exactly like Win + D does **plus additional features**:
- Shows desktop (minimizes all windows)
- Restores windows to their previous states
- Minimizes all windows if user changed state of any window or one of the windows opened / closed (exactly how Win + D works)
### Plus: ###
- Minimizes/Restores only windows on specific monitor, remembering their states

## Windows 11 build (this fork)

Upstream `ruzrobert/ShowDesktopOneMonitor` has been unmaintained since 2023-10, and its
only published release is the **2019 v1.0 binary**, which crashes on Windows 11. This fork
fixes the Windows 11 problems and builds the exe in GitHub Actions.

What is fixed here:

| Problem | Fix |
|---|---|
| `RegisterHotKey` with `MOD_WIN` makes Windows swallow the Win key-up, so the Win key stays logically pressed (a later plain `D` is delivered as Win+D) | The hot key is detected by a low-level keyboard hook (`WH_KEYBOARD_LL`) instead; Win down/up always pass through untouched, and only the hot key itself is swallowed |
| Modifier state cached in booleans goes stale across Win+L / UAC / Ctrl+Alt+Del, so a plain `D` press fires "show desktop" after unlocking | Modifier state is queried live with `GetAsyncKeyState` on every check |
| `Win+D` could not be intercepted without an extra remapping layer (e.g. AHK), and two hooks fighting each other desync the Win key | `Win+D` is taken over natively; no remapping layer needed |
| `requireAdministrator` in the manifest blocks "run at logon" and pops UAC on every start | Runs as a normal user (`asInvoker`); no elevation required |
| The app was DPI-unaware, so on a mixed-DPI setup `Screen.FromPoint` / `Screen.FromHandle` can pick the wrong monitor | Per-monitor DPI aware (`PerMonitorV2`) + Windows 10/11 declared in the manifest |
| The 2019 release sized its per-monitor state array once at startup and threw `IndexOutOfRangeException` whenever the monitor count changed | Uses the current code, which resizes the array on every toggle |
| `SettingsManager.Save()` ran from a finalizer and could kill the process | Saving moved to `ExitThreadCore` and guarded |

The Windows 11 hook rewrite follows the approach worked out in
[Jiaqi1017/ShowDesktopOneMonitor](https://github.com/Jiaqi1017/ShowDesktopOneMonitor)
(commits 2026-09-07/08, GPL-3.0). Thanks also to
[ppdng/ShowDesktopOneMonitor](https://github.com/ppdng/ShowDesktopOneMonitor) for the
manifest/DPI work.

## Download

Open the **Actions** tab, pick the newest successful `Build Windows 11 exe` run, and
download the `ShowDesktopOneMonitor-win11` artifact. Every push to `master` produces one;
publishing a release also attaches the zip to it.

Requires .NET Framework 4.8 (built into Windows 10 2004+ and Windows 11).

## Installation
1. Download and extract the artifact zip somewhere convenient.
2. Create a task in Task Scheduler:
- Specify path to *ShowDesktopOneMonitor.exe*
- Trigger: *Run only when user is logged on*
- *Run with highest privileges* is **not** needed anymore
- On the *Settings* tab make sure the task will not be stopped after running longer than some days, for example.
Note: program has icon in tray, but unfortunatelly it is invisible, if app is started from Task Scheduler :(

## Usage
Press *Win + D* to minimize/restore windows **on monitor where cursor is currently on**.
*Win + Shift + D* keeps working as well.

## Building locally

```
dotnet build ShowDesktopOneMonitor/ShowDesktopOneMonitor.csproj -c Release
```

Only the .NET SDK is required - no Visual Studio and no .NET Framework targeting pack
(the reference assemblies come from the `Microsoft.NETFramework.ReferenceAssemblies`
package that the SDK adds implicitly for .NET Framework targets).

## Credits
In core of the project is @FrigoCoder's code: https://github.com/FrigoCoder/FrigoTab
His code allows to get list of windows exactly like Alt + Tab does, what was excellent for my task.

## License
Copyright (c) 2019 Robert Ruzin. Licensed under the GPL-3.0 license.
