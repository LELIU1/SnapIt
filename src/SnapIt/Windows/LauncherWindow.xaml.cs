using System.ComponentModel;
using System.Windows;

namespace SnapIt.Windows
{
    public partial class LauncherWindow : Window
    {
        private bool _allowClose;

        public LauncherWindow()
        {
            InitializeComponent();
            ShortcutText.Text = string.IsNullOrWhiteSpace(App.CaptureHotkey)
                ? "（托盘截图）"
                : "（" + App.CaptureHotkey + "）";
        }

        private void StartCapture_Click(object sender, RoutedEventArgs e)
        {
            var app = Application.Current as App;
            app?.StartCapture();
        }

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            new HelpWindow(App.CaptureHotkey) { Owner = this }.ShowDialog();
        }

        private void LauncherWindow_Closing(object sender, CancelEventArgs e)
        {
            if (_allowClose) return;
            e.Cancel = true;
            Hide();
        }

        public void CloseForShutdown()
        {
            _allowClose = true;
            Close();
        }
    }
}
