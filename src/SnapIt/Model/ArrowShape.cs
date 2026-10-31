using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SnapIt.Model
{
    /// <summary>单向实心箭头：线身 + 等腰三角形箭头，头部随粗细缩放。</summary>
    public class ArrowShape : Shape
    {
        public static readonly DependencyProperty X1Property =
            DependencyProperty.Register("X1", typeof(double), typeof(ArrowShape),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));
        public static readonly DependencyProperty Y1Property =
            DependencyProperty.Register("Y1", typeof(double), typeof(ArrowShape),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));
        public static readonly DependencyProperty X2Property =
            DependencyProperty.Register("X2", typeof(double), typeof(ArrowShape),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));
        public static readonly DependencyProperty Y2Property =
            DependencyProperty.Register("Y2", typeof(double), typeof(ArrowShape),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double X1 { get { return (double)GetValue(X1Property); } set { SetValue(X1Property, value); } }
        public double Y1 { get { return (double)GetValue(Y1Property); } set { SetValue(Y1Property, value); } }
        public double X2 { get { return (double)GetValue(X2Property); } set { SetValue(X2Property, value); } }
        public double Y2 { get { return (double)GetValue(Y2Property); } set { SetValue(Y2Property, value); } }

        protected override Geometry DefiningGeometry
        {
            get
            {
                var p1 = new Point(X1, Y1);
                var p2 = new Point(X2, Y2);
                var v = p2 - p1;
                double len = v.Length;
                if (len < 0.5) return new LineGeometry(p1, p2);

                var u = v / len;
                var n = new Vector(-u.Y, u.X);
                double headLen = Math.Max(10, Math.Min(24, StrokeThickness * 3.0));
                double halfWidth = headLen * 0.55;

                var a = p2 - u * headLen + n * halfWidth;
                var b = p2 - u * headLen - n * halfWidth;
                var bodyEnd = p2 - u * headLen * 0.72;

                var group = new GeometryGroup();
                group.Children.Add(new LineGeometry(p1, bodyEnd));

                var head = new StreamGeometry();
                using (var ctx = head.Open())
                {
                    ctx.BeginFigure(a, true, true);
                    ctx.LineTo(p2, true, false);
                    ctx.LineTo(b, true, false);
                }
                head.Freeze();
                group.Children.Add(head);
                group.Freeze();
                return group;
            }
        }
    }
}
