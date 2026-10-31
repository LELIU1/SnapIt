using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace SnapIt.Services
{
    /// <summary>区域马赛克：缩小再最近邻放大，块大小按粗细档位。</summary>
    public static class MosaicService
    {
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        /// <param name="source">整屏原始位图</param>
        /// <param name="region">相对位图原点的物理像素区域</param>
        /// <param name="blockSize">马赛克块大小（物理像素）</param>
        public static BitmapSource CreateMosaic(Bitmap source, Int32Rect region, int blockSize)
        {
            if (source == null || region.Width <= 0 || region.Height <= 0) return null;

            int x = Math.Max(0, region.X);
            int y = Math.Max(0, region.Y);
            int w = Math.Min(source.Width - x, region.Width);
            int h = Math.Min(source.Height - y, region.Height);
            if (w <= 0 || h <= 0) return null;

            using (var cropped = new Bitmap(w, h))
            {
                using (var g = Graphics.FromImage(cropped))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(source, new Rectangle(0, 0, w, h), new Rectangle(x, y, w, h), GraphicsUnit.Pixel);
                }

                int bx = Math.Max(1, (int)Math.Ceiling(w / (double)blockSize));
                int by = Math.Max(1, (int)Math.Ceiling(h / (double)blockSize));

                using (var small = new Bitmap(bx, by))
                {
                    using (var g = Graphics.FromImage(small))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(cropped, 0, 0, bx, by);
                    }

                    using (var big = new Bitmap(w, h))
                    {
                        using (var g = Graphics.FromImage(big))
                        {
                            g.InterpolationMode = InterpolationMode.NearestNeighbor;
                            g.PixelOffsetMode = PixelOffsetMode.Half;
                            g.DrawImage(small, 0, 0, w, h);
                        }

                        IntPtr hbmp = big.GetHbitmap();
                        try
                        {
                            var result = Imaging.CreateBitmapSourceFromHBitmap(hbmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                            result.Freeze();
                            return result;
                        }
                        finally
                        {
                            DeleteObject(hbmp);
                        }
                    }
                }
            }
        }
    }
}
