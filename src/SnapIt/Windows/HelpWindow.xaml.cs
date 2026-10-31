using System.Windows;

namespace SnapIt.Windows
{
    public partial class HelpWindow : Window
    {
        public HelpWindow(string captureHotkey)
        {
            InitializeComponent();
            GlobalHotkeyText.Text = string.IsNullOrWhiteSpace(captureHotkey)
                ? "截图快捷键暂不可用，请双击系统托盘图标或右键菜单启动截图。"
                : "当前截图快捷键：" + captureHotkey;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
