using System;
using System.Windows.Documents;
using System.Windows.Media;

namespace SnapIt.Controls
{
    /// <summary>图元选中态的绿色虚线框装饰器。</summary>
    public class DashedSelectionAdorner : Adorner
    {
        private static readonly Pen Pen;

        static DashedSelectionAdorner()
        {
            var brush = new SolidColorBrush(Color.FromRgb(0x07, 0xC1, 0x60));
            brush.Freeze();
            Pen = new Pen(brush, 1.0) { DashStyle = DashStyles.Dash };
            Pen.Freeze();
        }

        public DashedSelectionAdorner(System.Windows.UIElement adornedElement)
            : base(adornedElement) { }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (AdornedElement == null) return;
            var rect = new System.Windows.Rect(AdornedElement.RenderSize);
            rect.Inflate(2, 2);
            drawingContext.DrawRectangle(null, Pen, rect);
        }
    }
}
