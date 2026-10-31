using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;
using SnapIt.Services;
using SnapIt.Windows;

namespace SnapIt
{
    public partial class App : Application
    {
        private WinForms.NotifyIcon _tray;
        private WinForms.ToolStripMenuItem _indicatorMenuItem;
        private HotkeyService _hotkey;
        private DesktopIndicatorWindow _desktopIndicator;
        private bool _captureSessionActive;
        private bool _restoreIndicatorAfterCapture;
        public static string CaptureHotkey { get; private set; }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmFlush();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += (s, ex) =>
            {
                string logPath = WriteExceptionLog("未处理异常", ex.Exception);
                MessageBox.Show(
                    "SnapIt 发生异常：" + ex.Exception.GetBaseException().Message +
                    (logPath == null ? string.Empty : "\n\n详细信息已写入：" + logPath),
                    "SnapIt", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };

            _tray = new WinForms.NotifyIcon
            {
                Text = BuildTrayText(CaptureHotkey),
                Icon = BuildIcon(),
                Visible = true
            };
            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("截图", null, (s, ev) => StartCapture());
            _indicatorMenuItem = new WinForms.ToolStripMenuItem("隐藏桌面提示图标", null,
                (s, ev) => ToggleDesktopIndicator());
            menu.Items.Add(_indicatorMenuItem);
            menu.Items.Add("帮助与快捷键", null, (s, ev) => ShowHelp());
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("退出", null, (s, ev) =>
            {
                _tray.Visible = false;
                Shutdown();
            });
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (s, ev) => StartCapture();

            _hotkey = new HotkeyService();
            CaptureHotkey = _hotkey.RegisterDefault();
            _hotkey.HotkeyPressed += StartCapture;

            _tray.Text = BuildTrayText(CaptureHotkey);
            _desktopIndicator = new DesktopIndicatorWindow(CaptureHotkey);
            _desktopIndicator.Show();
            ShowStartupNotification();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_desktopIndicator != null) _desktopIndicator.Close();
            if (_hotkey != null) _hotkey.Dispose();
            if (_tray != null)
            {
                _tray.Visible = false;
                if (_tray.Icon != null) _tray.Icon.Dispose();
                _tray.Dispose();
            }
            base.OnExit(e);
        }

        public void StartCapture()
        {
            if (CaptureWindow.IsOpen) return;
            PrepareDesktopIndicatorForCapture();
            try
            {
                new CaptureWindow().Show();
            }
            catch (Exception ex)
            {
                RestoreDesktopIndicatorAfterCapture();
                string logPath = WriteExceptionLog("截图启动失败", ex);
                MessageBox.Show(
                    "截图启动失败：" + ex.GetBaseException().Message +
                    (logPath == null ? string.Empty : "\n\n详细信息已写入：" + logPath),
                    "SnapIt", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PrepareDesktopIndicatorForCapture()
        {
            if (!_captureSessionActive)
            {
                _captureSessionActive = true;
                _restoreIndicatorAfterCapture = _desktopIndicator != null && _desktopIndicator.IsVisible;
            }

            if (_desktopIndicator == null || !_desktopIndicator.IsVisible) return;
            // ToolTips are separate popup windows; close them before screen capture so
            // an already-open tooltip cannot leak into the screenshot after its owner hides.
            _desktopIndicator.SuppressToolTips();
            _desktopIndicator.Hide();
            Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));
            try { DwmFlush(); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        public void ShowDesktopIndicator()
        {
            if (_desktopIndicator != null)
            {
                _desktopIndicator.RestoreToolTips();
                if (!_desktopIndicator.IsVisible)
                    _desktopIndicator.Show();
            }
            UpdateIndicatorMenuText();
        }

        public void HideDesktopIndicator()
        {
            if (_desktopIndicator != null && _desktopIndicator.IsVisible)
                _desktopIndicator.Hide();
            UpdateIndicatorMenuText();
        }

        private void ToggleDesktopIndicator()
        {
            if (_desktopIndicator != null && _desktopIndicator.IsVisible)
                HideDesktopIndicator();
            else
                ShowDesktopIndicator();
        }

        private void UpdateIndicatorMenuText()
        {
            if (_indicatorMenuItem != null)
                _indicatorMenuItem.Text = _desktopIndicator != null && _desktopIndicator.IsVisible
                    ? "隐藏桌面提示图标"
                    : "显示桌面提示图标";
        }

        public void OnCaptureWindowClosed()
        {
            RestoreDesktopIndicatorAfterCapture();
        }

        private void RestoreDesktopIndicatorAfterCapture()
        {
            bool restore = _captureSessionActive && _restoreIndicatorAfterCapture;
            _captureSessionActive = false;
            _restoreIndicatorAfterCapture = false;
            if (restore) ShowDesktopIndicator();
        }

        private void ShowHelp()
        {
            new HelpWindow(CaptureHotkey).ShowDialog();
        }

        public static string BuildTrayText(string hotkey)
        {
            return string.IsNullOrWhiteSpace(hotkey)
                ? "SnapIt 快截 | 点击桌面图标截图"
                : "SnapIt 快截 | " + hotkey + " 截图 | 点击桌面图标";
        }

        public static string BuildStartupMessage(string hotkey)
        {
            if (string.IsNullOrWhiteSpace(hotkey))
                return "已启动。点击桌面角落的 SnapIt 小图标截图；也可按右侧握柄移动图标。";
            if (hotkey == "Alt+A")
                return "已启动。按 Alt+A 或点击桌面角落的 SnapIt 小图标截图；可拖动握柄移动图标。";
            return "Alt+A 已被占用。按 " + hotkey + " 或点击桌面角落的 SnapIt 小图标截图；可拖动握柄移动图标。";
        }

        private void ShowStartupNotification()
        {
            var icon = CaptureHotkey == null
                ? WinForms.ToolTipIcon.Error
                : (CaptureHotkey == "Alt+A" ? WinForms.ToolTipIcon.Info : WinForms.ToolTipIcon.Warning);
            _tray.ShowBalloonTip(4000, "SnapIt 已启动", BuildStartupMessage(CaptureHotkey), icon);
        }

        public const string CopyNotificationMessage = "可以在需要的位置粘贴。";

        public void NotifyCaptureCopied()
        {
            if (_tray == null || !_tray.Visible) return;
            _tray.ShowBalloonTip(1800, "截图已复制", CopyNotificationMessage, WinForms.ToolTipIcon.Info);
        }

        private static string WriteExceptionLog(string title, Exception exception)
        {
            try
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SnapIt", "Logs");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "latest-error.txt");
                File.WriteAllText(path,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - " + title + Environment.NewLine +
                    exception.ToString());
                return path;
            }
            catch
            {
                return null;
            }
        }

        private static Icon BuildIcon()
        {
            using (var bmp = new Bitmap(16, 16))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var bg = new SolidBrush(Color.FromArgb(7, 193, 96)))
                {
                    g.FillRectangle(bg, 1, 3, 14, 11);
                    g.FillRectangle(bg, 5, 1, 6, 3);
                }
                using (var lens = new Pen(Color.White, 1.6f))
                    g.DrawEllipse(lens, 5, 5, 6, 6);
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }
}
