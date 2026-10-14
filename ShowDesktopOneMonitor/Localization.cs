using System;
using System.Globalization;

namespace ShowDesktopOneMonitor
{
    /// <summary>
    /// UI localization helper for tray menu and notification items.
    /// Supports English and Simplified Chinese (简体中文).
    /// Defaults to system culture on first run.
    /// </summary>
    internal static class Localization
    {
        public static string CurrentLanguage = ResolveInitialLanguage();

        private static string ResolveInitialLanguage ()
        {
            string saved = SettingsManager.ReadLanguage();
            if (!string.IsNullOrEmpty(saved)) {
                return saved;
            }
            // Auto-detect Chinese if system UI language is Chinese
            return CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                ? "zh-CN"
                : "en";
        }

        public static bool IsChinese => CurrentLanguage == "zh-CN";

        public static string OpenLogFolder => IsChinese ? "打开日志目录" : "Open log folder";
        public static string WriteLogFile => IsChinese ? "写入日志文件" : "Write log file";
        public static string NoAnimationWithoutTaskbarButton => IsChinese ? "无任务栏按钮的窗口不播动画" : "No animation without a taskbar button";
        public static string MiddleClickMove => IsChinese ? "中键点标题栏移至下一屏幕" : "Move window with middle click";
        public static string StartWithWindows => IsChinese ? "开机自启" : "Start with Windows";
        public static string AutoStartDisabled => IsChinese ? "关闭" : "Disabled";
        public static string AutoStartNormal => IsChinese ? "普通权限（注册表）" : "Normal user (Registry)";
        public static string AutoStartAdmin => IsChinese ? "管理员权限（计划任务免 UAC）" : "Administrator (Task Scheduler, no UAC)";
        public static string HotkeyMenu => IsChinese ? "快捷键 (Hotkey)" : "Hotkey (快捷键)";
        public static string HotkeyWinShiftD => IsChinese ? "Win + Shift + D (默认)" : "Win + Shift + D (Default)";
        public static string HotkeyWinD => IsChinese ? "Win + D (覆盖系统)" : "Win + D (Override System)";
        public static string HotkeyBoth => IsChinese ? "两者均支持 (Win+Shift+D 与 Win+D)" : "Both (Win+Shift+D & Win+D)";
        public static string LanguageMenu => IsChinese ? "语言 (Language)" : "Language (语言)";
        public static string About => IsChinese ? "关于" : "About";
        public static string Exit => IsChinese ? "退出" : "Exit";

        public static string AboutTitle => IsChinese ? "关于 ShowDesktopOneMonitor" : "About ShowDesktopOneMonitor";
        public static string CheckForUpdates => IsChinese ? "检查更新" : "Check for Updates";
        public static string CheckingUpdates => IsChinese ? "正在检查更新..." : "Checking for updates...";
        public static string AlreadyLatest => IsChinese ? "当前已是最新版本" : "You are on the latest version";
        public static string NewVersionFound => IsChinese ? "发现新版本 {0}，是否立即下载并更新？" : "New version {0} is available. Download and update now?";
        public static string DownloadingUpdate => IsChinese ? "正在下载更新 ({0}%)..." : "Downloading update ({0}%)...";
        public static string UpdateDownloadComplete => IsChinese ? "下载完成，正在重启应用..." : "Download complete. Restarting...";
        public static string UpdateFailed => IsChinese ? "更新失败" : "Update failed";
        public static string Close => IsChinese ? "关闭" : "Close";
        public static string CurrentVersion => IsChinese ? "当前版本: " : "Current version: ";
        public static string LatestVersion => IsChinese ? "最新版本: " : "Latest version: ";

        public static string FeatureWinDTitle => IsChinese ? "1. 单屏显示桌面 (Win + D)" : "1. Per-Monitor Show Desktop (Win + D)";
        public static string FeatureWinDDesc => IsChinese
            ? "按 Win + D 或 Win + Shift + D，仅最小化/还原光标所在显示器的窗口。多屏动画始终在本屏垂直落下收放，不跨屏乱飞。"
            : "Press Win + D or Win + Shift + D to minimize or restore windows only on the monitor where the cursor is. Animations drop straight into that screen's taskbar.";

        public static string FeatureMoveTitle => IsChinese ? "2. 中键搬窗到下一屏幕" : "2. Middle-Click Title Bar to Move Window";
        public static string FeatureMoveDesc => IsChinese
            ? "用鼠标中键点击任意窗口标题栏，窗口将搬至下一屏幕并保持在前台与获得焦点，自动适配不同屏幕的 DPI 缩放与最大化状态。"
            : "Middle-click a window's title bar to move it to the next monitor with focus and proper DPI/maximized scaling.";

        public static string FeatureAutoStartTitle => IsChinese ? "3. 开机自启（双权限模式）" : "3. Auto-Start (Dual Modes)";
        public static string FeatureAutoStartDesc => IsChinese
            ? "普通权限通过注册表启动（免提权）；管理员权限通过 Windows 任务计划程序自动提权自启，全程跳过 UAC 弹窗，支持移动管理员权限窗口。"
            : "Normal mode runs via registry without elevation; Administrator mode runs via Task Scheduler with automatic elevation and zero UAC prompts at logon.";

        public static string FeatureLogTitle => IsChinese ? "4. 日志与动画选项" : "4. Diagnostics & Animation Options";
        public static string FeatureLogDesc => IsChinese
            ? "托盘右键可一键打开日志目录或关闭日志；针对无任务栏按钮的特殊窗口支持瞬间切换。"
            : "Open log folder, toggle logging, or suppress sliding animations for windows without a taskbar button.";
    }
}
