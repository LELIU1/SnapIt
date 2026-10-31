using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapIt.Windows
{
    /// <summary>贴图窗口：置顶显示截图，拖动移动、滚轮缩放、双击关闭。</summary>
    public partial class PinWindow : Window
    {
        private readonly double _baseWidth;
        private readonly double _baseHeight;
        private double _zoom = 1.0;

        public PinWindow(BitmapSource image, double width, double height, double left, double top)
        {
            InitializeComponent();
            _baseWidth = width;
            _baseHeight = height;
            Left = left;
            Top = top;
            Width = width;
            Height = height;
            PinImage.Source = image;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            if (source != null && IsDescendantOf(source, UnpinButton)) return;

            if (e.ClickCount >= 2)
            {
                Close();
                return;
            }
            try { DragMove(); } catch (InvalidOperationException) { }
        }

        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            UnpinButton.Visibility = Visibility.Visible;
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            UnpinButton.Visibility = Visibility.Collapsed;
        }

        private void Unpin_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private static bool IsDescendantOf(DependencyObject node, DependencyObject ancestor)
        {
            while (node != null)
            {
                if (ReferenceEquals(node, ancestor)) return true;
                node = node is Visual || node is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(node)
                    : LogicalTreeHelper.GetParent(node);
            }
            return false;
        }

        private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
            double newZoom = Math.Max(0.15, Math.Min(5.0, _zoom * factor));
            if (Math.Abs(newZoom - _zoom) < 0.001) return;

            double nw = _baseWidth * newZoom;
            double nh = _baseHeight * newZoom;
            Left += (Width - nw) / 2;
            Top += (Height - nh) / 2;
            Width = nw;
            Height = nh;
            _zoom = newZoom;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
        }
    }
}
