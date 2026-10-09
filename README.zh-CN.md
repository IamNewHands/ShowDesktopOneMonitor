[English](README.md) | **中文**

# ShowDesktopOneMonitor

## 说明
让 Win + D 只对一块显示器显示桌面！
行为与系统 Win + D 完全一致，**另有增强**：
- 显示桌面（最小化所有窗口）
- 把窗口恢复到之前的状态
- 如果用户改动了任意窗口的状态，或有窗口被打开/关闭，则重新最小化所有窗口（与系统 Win + D 行为一致）
### 另外 ###
- 只最小化/恢复指定显示器上的窗口，并记住它们各自的状态

## Windows 11 版本（本 fork）

上游 `ruzrobert/ShowDesktopOneMonitor` 自 2023-10 起不再维护，唯一发布过的版本还是
**2019 年的 v1.0 二进制**，在 Windows 11 上会崩溃。本 fork 修掉了这些 Windows 11 问题，
并用 GitHub Actions 编译 exe。

本 fork 修了什么：

| 问题 | 修复 |
|---|---|
| 用 `RegisterHotKey` 注册带 `MOD_WIN` 的热键后，系统会吞掉 Win 键的 key-up，Win 键从此停留在「按下」状态（之后再单按 `D` 会被当成 Win+D） | 改用低级键盘钩子（`WH_KEYBOARD_LL`）自行检测热键；Win 键的 down/up 原样透传，只吞掉热键主键本身 |
| 修饰键状态缓存在布尔变量里，遇到 Win+L / UAC / Ctrl+Alt+Del 会变成陈旧状态，解锁后单按 `D` 就触发「显示桌面」 | 每次判定都用 `GetAsyncKeyState` 现查修饰键的真实状态 |
| `Win+D` 不借助额外重映射层（比如 AHK）就无法拦截，而两个钩子互相吞键会让 Win 键状态失步 | 原生接管 `Win+D`，不需要任何重映射层 |
| 清单里的 `requireAdministrator` 导致无法开机自启，且每次启动都弹 UAC | 以普通用户运行（`asInvoker`），不需要提权 |
| 进程不感知 DPI，混合 DPI 环境下 `Screen.FromPoint` / `Screen.FromHandle` 会选错显示器 | 启用每显示器 DPI 感知（`PerMonitorV2`），并在清单里声明支持 Windows 10/11 |
| 2019 那个版本只在启动时按当时的显示器数量分配状态数组，显示器数量一变就抛 `IndexOutOfRangeException` | 使用现行代码，每次切换都会重新调整数组大小 |
| `SettingsManager.Save()` 在终结器里执行，可能直接杀死进程 | 移到 `ExitThreadCore` 并加了保护 |
| 多显示器上按热键时，最小化/恢复动画会飞向系统算出的任务栏按钮，而那个按钮可能在**另一块**显示器上，动画于是横向滑过整个桌面 | 接管 shell 钩子 `HSHELL_GETMINRECT`：切换期间把动画目标改写成本显示器底边（`MinimizeAnimation.cs`），动画始终在本屏内垂直落下 |

Windows 11 的钩子改法参考了
[Jiaqi1017/ShowDesktopOneMonitor](https://github.com/Jiaqi1017/ShowDesktopOneMonitor)
（2026-09-07/08 两个提交，GPL-3.0）；清单与 DPI 部分感谢
[ppdng/ShowDesktopOneMonitor](https://github.com/ppdng/ShowDesktopOneMonitor)。

## 下载

从滚动 release 里取 `ShowDesktopOneMonitor.exe`：
<https://github.com/IamNewHands/ShowDesktopOneMonitor/releases/latest>

每次向 `master` 推送都会重新编译，并覆盖这唯一一个 release 里的资产 —— 永远只有一个
release，里面永远只有最新那个 exe。**Actions** 页签下另有一份完整 zip（exe +
`.exe.config` + README），制品名为 `ShowDesktopOneMonitor-win11`，需要配置文件时取它。

需要 .NET Framework 4.8（Windows 10 2004+ 与 Windows 11 自带）。

## 安装
1. 把 `ShowDesktopOneMonitor.exe` 放到一个**可写**目录（诊断日志就写在它旁边）。
2. 在任务计划程序里创建一个任务：
- 程序或脚本指向 *ShowDesktopOneMonitor.exe*
- 触发器：*只在用户登录时运行*
- *使用最高权限运行* 只在你想移动提权进程的窗口时才需要，普通功能不需要（见下面的「以管理员身份运行」）
- 在*设置*页签确认任务不会因为运行时间过长而被停止。
注意：程序有托盘图标，但如果由任务计划程序启动，这个图标可能是看不见的 :(

## 用法
按 *Win + D* 最小化/恢复**光标当前所在显示器**上的窗口。
*Win + Shift + D* 同样可用。

窗口的展开/收起动画始终落在**光标所在显示器的底边**：窗口垂直往下收进任务栏、
再垂直长回来。不会出现「动画横向滑到另一块显示器去」的情况（那个方向是 Windows
自己算的任务栏按钮位置决定的，多屏时可能落在别的屏幕上）。

## 把窗口移到下一个显示器

用鼠标**中键**点某个窗口的**标题栏**，该窗口就会移到下一个显示器（按
`Screen.AllScreens` 顺序，最后一块之后绕回第一块）。窗口尺寸与相对位置保持不变，
最大化的窗口在新显示器上仍然最大化 —— 和 `Win + Shift + 方向键` 一个思路。

只有标题栏区域会响应，所以中键在其它位置保持原有含义（新标签页打开链接、关闭标签页、
自动滚动、粘贴）。只有一块显示器时一个点击都不会被吞：点击照常透传，只在日志里留一行。

已知限制：
- 自绘标题栏的应用（Chrome、VS Code 等）只在顶部那条与系统标题栏等高的区域内响应，
  而不是整个视觉上的标题栏。
- 提权进程的窗口对普通权限进程是「不可见」的：Windows 根本不会把发往更高完整性窗口的
  输入交给它，所以那个标题栏永远不会触发。以管理员身份运行本程序，这些窗口也能移动
  （见下）。

### 以管理员身份运行

只有上面那种「提权窗口」的情况才需要。可以右键 exe 选*以管理员身份运行*，或者在任务
计划程序的任务上勾选*使用最高权限运行* —— 计划任务在登录时启动是提权的，但**不会**弹 UAC。

程序其它功能都不需要提权，而提权进程能碰到的东西多得多，不需要就别开。

## 排查

本程序是纯托盘程序：**故意没有任务栏按钮**。Windows 11 默认会把新出现的托盘图标收起来，
点时钟旁边的 `^` 展开就能找到，或者在*设置 > 个性化 > 任务栏 > 其它系统托盘图标*里打开。

程序会把诊断日志写在 **exe 同目录的 `log.txt`**（该目录不可写时回退到
`%LOCALAPPDATA%\ShowDesktopOneMonitor\log.txt`；托盘菜单里的 *Open log folder* 总是打开
实际在用的那一个）。日志记录启动信息、托盘图标、热键注册、`SetWindowsHookEx` 的结果、
每次热键触发以及钩子累计收到的按键数 —— 足以区分「程序根本没起来」「钩子死了」和
「钩子活着但组合键没匹配上」。

日志上限 1 MB：超过就把最旧的一半丢掉，不会无限增长。

托盘菜单里的 **Write log file** 是日志开关：取消勾选后**一行都不再写**（连错误也不写），
状态存在设置里，重启后保持关着；需要排查时再勾回来。

排查动画方向时，日志里每个被切换的窗口都有一行 `minimizing:` / `restoring:`（hwnd、窗口类、
标题），接管动画的那次 shell 询问是一行 `MinimizeAnimation:`（含 shell 原本想用的矩形）。
某个窗口在 `minimizing:` 里出现、却没有对应的 `MinimizeAnimation:` 行，就说明系统没有为它
询问动画位置 —— 那种窗口保留系统自己的动画方向。

## 本地编译

```
dotnet build ShowDesktopOneMonitor/ShowDesktopOneMonitor.csproj -c Release
```

只需要 .NET SDK —— 不需要 Visual Studio，也不需要装 .NET Framework 目标包
（.NET Framework 目标的引用程序集由 SDK 自动引入的
`Microsoft.NETFramework.ReferenceAssemblies` 包提供）。

## 致谢
项目核心是 @FrigoCoder 的代码：https://github.com/FrigoCoder/FrigoTab
他的代码能像 Alt + Tab 那样取得窗口列表，正好满足本项目的需要。

## 许可证
Copyright (c) 2019 Robert Ruzin. Licensed under the GPL-3.0 license.
