**English** | [中文](README.zh-CN.md)

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
| On a multi-monitor setup the minimize/restore animation flies to the taskbar button the shell picks - and that button can live on **another** monitor, so the animation slides sideways across the whole desktop | The shell hook `HSHELL_GETMINRECT` is answered during our own toggle (`MinimizeAnimation.cs`), retargeting the animation at the bottom edge of the monitor the toggle runs on, so it always drops vertically inside that screen |
| Some windows (Foxmail's main frame is one: owned by a hidden helper window and carrying no `WS_EX_APPWINDOW`) have **no taskbar button at all**, so Windows never asks where their animation should go and picks a spot of its own - which reads as the animation sliding sideways. All three levers were measured and are dead: no shell question arrives, `ptMinPosition` is ignored on Windows 11, and changing another process's window styles is refused with access denied | Such a window (owner set, no `WS_EX_APPWINDOW`) is recognised, and a tray switch - *No animation without a taskbar button*, **off by default** - makes it pop instead of sliding sideways by setting `DWMWA_TRANSITIONS_FORCEDISABLED` for that one call. See below for why it is off |

The Windows 11 hook rewrite follows the approach worked out in
[Jiaqi1017/ShowDesktopOneMonitor](https://github.com/Jiaqi1017/ShowDesktopOneMonitor)
(commits 2026-09-07/08, GPL-3.0). Thanks also to
[ppdng/ShowDesktopOneMonitor](https://github.com/ppdng/ShowDesktopOneMonitor) for the
manifest/DPI work.

## Download

Take `ShowDesktopOneMonitor.exe` from the rolling release:
<https://github.com/IamNewHands/ShowDesktopOneMonitor/releases/latest>

Every push to `master` rebuilds it and replaces that single release's asset - there is
only ever one release, and it always holds the newest exe. The **Actions** tab keeps a full
zip (exe + `.exe.config` + README) under the `ShowDesktopOneMonitor-win11` artifact, in case
you also want the config file.

Requires .NET Framework 4.8 (built into Windows 10 2004+ and Windows 11).

## Installation
1. Put `ShowDesktopOneMonitor.exe` in a folder you can write to (the diagnostic log lives next to it).
2. Create a task in Task Scheduler:
- Specify path to *ShowDesktopOneMonitor.exe*
- Trigger: *Run only when user is logged on*
- *Run with highest privileges* is only needed if you want to move the windows of elevated
  apps; it is not needed for the normal features (see *Running as administrator*)
- On the *Settings* tab make sure the task will not be stopped after running longer than some days, for example.
Note: program has icon in tray, but unfortunatelly it is invisible, if app is started from Task Scheduler :(

## Usage
Press *Win + D* to minimize/restore windows **on monitor where cursor is currently on**.
*Win + Shift + D* keeps working as well.

The minimize/restore animation always lands on the **bottom edge of the monitor the cursor
is on**: windows drop straight down into the taskbar and grow back up. It never slides
sideways onto another monitor (that direction comes from the taskbar button Windows picks,
which on a multi-monitor setup can sit on a different screen).

Exception: a window with **no taskbar button** (Foxmail's main frame, for example) gets whatever
animation spot Windows picks, which on a multi-monitor setup reads as the animation sliding
sideways. The tray switch *No animation without a taskbar button* makes such a window pop in and
out instead - it is **off by default**, because on a dual-monitor machine turning it on left the
secondary screen not repainting after a toggle, and that cause is not understood yet. Windows
with a taskbar button keep the vertical animation.

## Move a window to the next monitor

Middle-click a window's **title bar** and that window moves to the next monitor
(`Screen.AllScreens` order, wrapping around after the last one). Size and relative position
are kept, and a maximized window stays maximized on the new monitor - the same idea as
`Win + Shift + Arrow`.

Only the caption area reacts, so middle-click keeps its normal meaning everywhere else
(opening a link in a new tab, closing a tab, autoscroll, paste). With a single monitor
nothing is swallowed at all: the click passes through and only a log line is written.

Limitations:
- Apps that draw their own title bar (Chrome, VS Code, ...) only respond in the top strip
  that matches the system caption height, not across their whole visual title bar.
- A window owned by an elevated process is invisible to a normal-privilege process: Windows
  does not even hand it the input destined for a higher-integrity window, so that title bar
  never triggers. Run this app elevated and those windows move too (see below).

### Running as administrator

Only needed for the elevated-window case above. Either right-click the exe and pick
*Run as administrator*, or tick *Run with highest privileges* on the Task Scheduler task -
a scheduled task starts elevated at logon **without** a UAC prompt.

Nothing else in the program needs elevation, and an elevated process can touch more of the
system, so leave it off if you do not need it.

## Troubleshooting

The app is tray-only: there is deliberately **no taskbar button**. On Windows 11 a new
tray icon starts hidden - click the `^` chevron next to the clock to find it, or turn it
on under *Settings > Personalization > Taskbar > Other system tray icons*.

The app writes a diagnostic log to `log.txt` **next to the exe** (it falls back to
`%LOCALAPPDATA%\ShowDesktopOneMonitor\log.txt` if that folder is not writable; the tray
menu's *Open log folder* always opens whichever one is in use). It records startup, the tray
icon, the hot key registrations, the `SetWindowsHookEx` result, every hot key press and how
many key events the hook has seen - enough to tell "the app never started", "the hook is
dead" and "the hook is alive but the combo did not match" apart.

The file is capped at 1 MB: once it would grow past that, its oldest half is dropped, so it
never grows without bound.

**Write log file** in the tray menu switches the log off: while it is unchecked **nothing at
all is written** (not even errors). The state is kept in the settings file and survives a
restart; tick it again when you need the log.

For animation problems, the log lists every window it toggles (`minimizing:` / `restoring:`
with hwnd, window class and title) and every shell question it answers (`MinimizeAnimation:`
with the rectangle the shell wanted to use). A window that shows up in `minimizing:` but has
no matching `MinimizeAnimation:` line is one Windows never asked about - such a window keeps
the animation direction Windows picks for it. Lines ending in
`[no taskbar button: Windows never asks]` are the ones classified as having no taskbar button;
with the tray switch on, the same line ends in `..., animation skipped]`.

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
