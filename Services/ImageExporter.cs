using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace SnapIt.Services
{
    /// <summary>结果合成与输出：图元层用 VisualBrush 矢量重绘到物理分辨率，保证与画布所见一致。</summary>
    public static class ImageExporter
    {
        /// <param name="fullBase">整屏底图</param>
        /// <param name="shapeCanvas">图元画布（窗口 DIP 坐标系）</param>
        /// <param name="scale">DIP → 物理像素比例</param>
        /// <param name="selectionDip">选区（窗口 DIP 坐标）</param>
        public static BitmapSource Compose(BitmapSource fullBase, Canvas shapeCanvas, double scale, Rect selectionDip)
        {
            if (fullBase == null) throw new ArgumentNullException(nameof(fullBase));
            if (shapeCanvas == null) throw new ArgumentNullException(nameof(shapeCanvas));
            if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
                throw new ArgumentOutOfRangeException(nameof(scale));
            if (selectionDip.IsEmpty || selectionDip.Width <= 0 || selectionDip.Height <= 0)
                throw new ArgumentException("截图选区必须具有有效宽高。", nameof(selectionDip));

            int px = (int)Math.Floor(selectionDip.X * scale);
            int py = (int)Math.Floor(selectionDip.Y * scale);
            int pw = Math.Max(1, (int)Math.Round(selectionDip.Width * scale));
            int ph = Math.Max(1, (int)Math.Round(selectionDip.Height * scale));

            if (px < 0 || py < 0 || px + pw > fullBase.PixelWidth || py + ph > fullBase.PixelHeight)
                throw new ArgumentOutOfRangeException(nameof(selectionDip), "截图选区超出底图范围。");

            // Rasterize the complete annotation canvas once in physical pixels. This preserves
            // WPF's actual z-order, text layout, strokes, images and per-element transforms.
            // The former per-element VisualBrush path could silently omit committed elements
            // when WPF had not materialized their individual visual layout yet.
            shapeCanvas.UpdateLayout();
            int layerWidth = Math.Max(1, (int)Math.Ceiling(shapeCanvas.Width * scale));
            int layerHeight = Math.Max(1, (int)Math.Ceiling(shapeCanvas.Height * scale));
            var layerBitmap = new RenderTargetBitmap(
                layerWidth, layerHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            layerBitmap.Render(shapeCanvas);
            layerBitmap.Freeze();

            if (px + pw > layerBitmap.PixelWidth || py + ph > layerBitmap.PixelHeight)
                throw new InvalidOperationException("标注画布与截图选区尺寸不一致，无法合成编辑图层。");

            var croppedLayer = new CroppedBitmap(layerBitmap, new Int32Rect(px, py, pw, ph));
            croppedLayer.Freeze();

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new TranslateTransform(-px, -py));
                dc.DrawImage(fullBase, new Rect(0, 0, fullBase.PixelWidth, fullBase.PixelHeight));
                dc.Pop();
                dc.DrawImage(croppedLayer, new Rect(0, 0, pw, ph));
            }

            var result = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
            result.Render(visual);
            result.Freeze();
            return result;
        }

        /// <summary>复制到剪贴板：CF 位图 + PNG 双格式，兼容微信/文档等所有粘贴目标。</summary>
        public static void CopyToClipboard(BitmapSource image)
        {
            Clipboard.SetDataObject(CreateClipboardDataObject(image), true);
        }

        /// <summary>构造含位图与 PNG 双格式的数据对象；不写入系统剪贴板，便于安全验证。</summary>
        public static DataObject CreateClipboardDataObject(BitmapSource image)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));

            var data = new DataObject();
            data.SetImage(image);

            var pngStream = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(pngStream);
            pngStream.Position = 0;
            data.SetData("PNG", pngStream);

            // Keep the stream alive through clipboard publication; some paste targets request
            // the PNG payload lazily instead of consuming it while SetDataObject is called.
            return data;
        }

        /// <summary>另存为 PNG/JPG，返回是否保存成功。</summary>
        public static bool SaveToFile(BitmapSource image, Window owner)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "PNG 图片|*.png|JPEG 图片|*.jpg",
                FileName = "SnapIt_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                AddExtension = true,
                DefaultExt = ".png",
                OverwritePrompt = true
            };
            if (dlg.ShowDialog(owner) != true) return false;
            SaveToFile(image, dlg.FileName);
            return true;
        }

        /// <summary>按文件扩展名保存为 PNG 或 JPEG；供自动化验收与非对话框场景复用。</summary>
        public static void SaveToFile(BitmapSource image, string filePath)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("保存路径不能为空。", nameof(filePath));

            bool isJpeg = filePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                          filePath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);
            BitmapEncoder encoder = isJpeg
                ? (BitmapEncoder)new JpegBitmapEncoder { QualityLevel = 95 }
                : new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(filePath))
                encoder.Save(stream);
        }
    }
}
