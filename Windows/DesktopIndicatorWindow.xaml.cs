using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SnapIt.Windows
{
    public partial class DesktopIndicatorWindow : Window
    {
        private bool _toolTipsSuppressed;
        private string _windowToolTip;
        private const string CaptureToolTipText = "点击开始截图";
        private const string MoveToolTipText = "拖动移动提示图标";

        public DesktopIndicatorWindow(string captureHotkey)
        {
            InitializeComponent();
            _windowToolTip = string.IsNullOrWhiteSpace(captureHotkey)
                ? "SnapIt 已运行 · 点击截图 · 拖动右侧握柄移动"
                : "SnapIt 已运行 · 点击截图 · " + captureHotkey + " · 拖动右侧握柄移动";
            ToolTip = _windowToolTip;
            PlaceNearScreenCorner();
        }

        public void SuppressToolTips()
        {
            if (_toolTipsSuppressed) return;
            CloseOpenToolTip(ToolTip);
            CloseOpenToolTip(CaptureButton.ToolTip);
            CloseOpenToolTip(MoveThumb.ToolTip);
            ToolTipService.SetIsEnabled(this, false);
            ToolTipService.SetIsEnabled(CaptureButton, false);
            ToolTipService.SetIsEnabled(MoveThumb, false);
            ToolTip = null;
            CaptureButton.ToolTip = null;
            MoveThumb.ToolTip = null;
            _toolTipsSuppressed = true;
        }

        private static void CloseOpenToolTip(object value)
        {
            if (value is ToolTip toolTip)
                toolTip.IsOpen = false;
        }

        public void RestoreToolTips()
        {
            if (!_toolTipsSuppressed) return;
            ToolTip = _windowToolTip;
            CaptureButton.ToolTip = CaptureToolTipText;
            MoveThumb.ToolTip = MoveToolTipText;
            ToolTipService.SetIsEnabled(this, true);
            ToolTipService.SetIsEnabled(CaptureButton, true);
            ToolTipService.SetIsEnabled(MoveThumb, true);
            _toolTipsSuppressed = false;
        }

        private void PlaceNearScreenCorner()
        {
            Rect area = SystemParameters.WorkArea;
            Left = Math.Max(area.Left + 8, area.Right - Width - 24);
            Top = Math.Max(area.Top + 8, area.Bottom - Height - 72);
        }

        private void MoveThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            Rect area = SystemParameters.WorkArea;
            Left = Math.Max(area.Left, Math.Min(area.Right - Width, Left + e.HorizontalChange));
            Top = Math.Max(area.Top, Math.Min(area.Bottom - Height, Top + e.VerticalChange));
        }

        private void Capture_Click(object sender, RoutedEventArgs e)
        {
            (Application.Current as App)?.StartCapture();
        }

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            new HelpWindow(App.CaptureHotkey) { Owner = this }.ShowDialog();
        }

        private void Hide_Click(object sender, RoutedEventArgs e)
        {
            var app = Application.Current as App;
            if (app != null) app.HideDesktopIndicator();
            else Hide();
        }
    }
}
