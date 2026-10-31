using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace SnapIt.Capture
{
    /// <summary>
    /// 截图引擎：首版只捕获主显示器，保留 BitmapSource（显示/导出）与原始 Bitmap（取色/马赛克）。
    /// 坐标以主显示器物理像素原点为基准，便于后续扩展到多显示器。
    /// </summary>
    public class ScreenCapturer : IDisposable
    {
        public BitmapSource Image { get; private set; }
        public Bitmap RawBitmap { get; private set; }
        public double DpiScale { get; private set; } = 1.0;
        public double PhysX { get; private set; }
        public double PhysY { get; private set; }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        public void CaptureAll()
        {
            RawBitmap?.Dispose();
            RawBitmap = null;
            Image = null;

            var primary = System.Windows.Forms.Screen.PrimaryScreen;
            if (primary == null)
                throw new InvalidOperationException("未检测到可用的主显示器。");

            var bounds = primary.Bounds;
            PhysX = bounds.X;
            PhysY = bounds.Y;

            using (var g0 = Graphics.FromHwnd(IntPtr.Zero))
                DpiScale = g0.DpiX / 96.0;

            var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(bounds.X, bounds.Y, 0, 0,
                    new System.Drawing.Size(bounds.Width, bounds.Height), CopyPixelOperation.SourceCopy);
            }
            RawBitmap = bmp;

            IntPtr hbmp = bmp.GetHbitmap();
            try
            {
                Image = Imaging.CreateBitmapSourceFromHBitmap(hbmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                Image.Freeze();
            }
            finally
            {
                DeleteObject(hbmp);
            }
        }

        /// <summary>按窗口 DIP 坐标取合成位图像素颜色。</summary>
        public System.Drawing.Color GetPixel(double dipX, double dipY)
        {
            if (RawBitmap == null) return System.Drawing.Color.Black;
            int x = (int)(dipX * DpiScale);
            int y = (int)(dipY * DpiScale);
            if (x < 0 || y < 0 || x >= RawBitmap.Width || y >= RawBitmap.Height)
                return System.Drawing.Color.Black;
            return RawBitmap.GetPixel(x, y);
        }

        /// <summary>按窗口 DIP 坐标裁出 2*half 边长的放大镜内容。</summary>
        public CroppedBitmap CropAround(double dipX, double dipY, int half)
        {
            int cx = (int)(dipX * DpiScale);
            int cy = (int)(dipY * DpiScale);
            int w = RawBitmap.Width, h = RawBitmap.Height;
            int cropWidth = Math.Min(w, half * 2);
            int cropHeight = Math.Min(h, half * 2);
            int x = Math.Max(0, Math.Min(w - cropWidth, cx - half));
            int y = Math.Max(0, Math.Min(h - cropHeight, cy - half));
            return new CroppedBitmap(Image, new Int32Rect(x, y, cropWidth, cropHeight));
        }

        public void Dispose()
        {
            RawBitmap?.Dispose();
            RawBitmap = null;
            Image = null;
        }
    }
}
