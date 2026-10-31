using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SnapIt.Capture;
using SnapIt.Controls;
using SnapIt.Model;
using SnapIt.Services;
using Image = System.Windows.Controls.Image;

namespace SnapIt.Windows
{
    /// <summary>
    /// 截屏主窗口：全屏遮罩框选 + 图元标注编辑 + 输出。
    /// 坐标体系：窗口 DIP 坐标 × DpiScale = 合成位图内的物理像素坐标。
    /// </summary>
    public partial class CaptureWindow : Window
    {
        /// <summary>是否已有截屏窗口实例在运行（避免热键重复触发）。</summary>
        public static bool IsOpen { get; private set; }

        private enum State { HoverPick, DragSelect, Resizing, Editing, DrawShape, MoveShape }
        private enum Tool { None, Rect, Ellipse, Line, Arrow, Pen, Highlighter, Mosaic, Text }

        private readonly ScreenCapturer _cap = new ScreenCapturer();
        private List<NativeWindowInfo> _windows = new List<NativeWindowInfo>();
        private double _scale = 1.0;

        private State _state = State.HoverPick;
        private Tool _tool = Tool.None;
        private Rect _selection = Rect.Empty;

        private Point _pressPoint;
        private Rect _resizeStartRect;
        private int _resizeHandle = -1;

        private Tool _drawTool = Tool.None;
        private FrameworkElement _drawShape;
        private Point _drawStart;
        private Polyline _drawStroke;

        private UIElement _selectedShape;
        private Point _moveStartPoint;
        private double _moveOriginLeft, _moveOriginTop;

        private TextBox _editingText;
        private bool _uiInitialized;
        private bool _suppressIndicatorRestore;
        private bool _sizeEditActive;
        private TextBlock _sizeEditTarget;
        private double _sizeEditStartSize;

        private Color _color = Color.FromRgb(0xFA, 0x51, 0x51);
        private int _thicknessIndex = 1;
        private static readonly double[] Thicknesses = { 2, 4, 7 };
        private static readonly string[] PaletteHex = { "#FA5151", "#FF9760", "#FFC300", "#07C160", "#10AEFF", "#1485EE", "#7A5AF8", "#1F2329" };

        private readonly List<EditCommand> _undo = new List<EditCommand>();
        private readonly List<EditCommand> _redo = new List<EditCommand>();

        private readonly Rectangle[] _handles = new Rectangle[8];
        private readonly List<ToggleButton> _swatches = new List<ToggleButton>();
        private DashedSelectionAdorner _selAdorner;
        private bool _updatingPanel;

        public CaptureWindow()
        {
            InitializeComponent();
            string shortcut = string.IsNullOrWhiteSpace(App.CaptureHotkey) ? "未注册" : App.CaptureHotkey;
            HelpBtn.ToolTip = "帮助与快捷键（截图：" + shortcut + "）";
            NewCaptureBtn.ToolTip = "重新截图（" + shortcut + "）";

            _cap.CaptureAll();
            _scale = _cap.DpiScale;
            BaseImage.Source = _cap.Image;
            IsOpen = true;

            SourceInitialized += OnSourceInitialized;
            Closing += (s, e) =>
            {
                IsOpen = false;
                _cap.Dispose();
                if (!_suppressIndicatorRestore)
                    (Application.Current as App)?.OnCaptureWindowClosed();
            };
        }

        // ---------------------------------------------------------------- 初始化

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            Left = _cap.PhysX / _scale;
            Top = _cap.PhysY / _scale;
            Width = _cap.RawBitmap.Width / _scale;
            Height = _cap.RawBitmap.Height / _scale;

            BaseImage.Width = Width;
            BaseImage.Height = Height;

            // 编辑画布铺满主显示器截图（Canvas 不做布局拉伸，需显式给尺寸）
            Canvas.SetLeft(ShapeLayer, 0);
            Canvas.SetTop(ShapeLayer, 0);
            ShapeLayer.Width = Width;
            ShapeLayer.Height = Height;
            ShapeCanvas.Width = Width;
            ShapeCanvas.Height = Height;

            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            WindowEnumerator.HideFromAltTab(helper.Handle);
            _windows = WindowEnumerator.GetWindows(
                new Rect(_cap.PhysX, _cap.PhysY, _cap.RawBitmap.Width, _cap.RawBitmap.Height), helper.Handle);

            Cursor = Cursors.Cross;
            UpdateDim();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (_uiInitialized) return;
            _uiInitialized = true;
            BuildHandles();
            BuildSwatches();
            BuildFonts();
            Focus();
        }

        private void BuildHandles()
        {
            var cursors = new[]
            {
                Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE,
                Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE
            };
            for (int i = 0; i < 8; i++)
            {
                int idx = i;
                var h = new Rectangle
                {
                    Width = 9,
                    Height = 9,
                    Fill = Brushes.White,
                    Stroke = new SolidColorBrush(Color.FromRgb(0x07, 0xC1, 0x60)),
                    StrokeThickness = 1.2,
                    Cursor = cursors[i]
                };
                h.MouseLeftButtonDown += (s, ev) => HandleDown(idx, ev);
                _handles[i] = h;
                HandlesCanvas.Children.Add(h);
            }
        }

        private void BuildSwatches()
        {
            foreach (var hex in PaletteHex)
            {
                var c = (Color)ColorConverter.ConvertFromString(hex);
                var btn = new ToggleButton
                {
                    Style = (Style)FindResource("SwatchStyle"),
                    Background = new SolidColorBrush(c),
                    Tag = c,
                    ToolTip = hex
                };
                btn.Click += Swatch_Click;
                _swatches.Add(btn);
                SwatchPanel.Children.Add(btn);
            }
            CheckSwatch(_color);
        }

        private void BuildFonts()
        {
            _updatingPanel = true;
            var fonts = System.Windows.Media.Fonts.SystemFontFamilies
                .Select(f => f.Source)
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct()
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
            FontBox.ItemsSource = fonts;
            FontBox.SelectedItem = fonts.Contains("Microsoft YaHei") ? "Microsoft YaHei" : (fonts.Count > 0 ? fonts[0] : null);
            _updatingPanel = false;
        }

        // ---------------------------------------------------------------- 遮罩 / 选区 UI

        private static void Place(FrameworkElement el, double x, double y, double w, double h)
        {
            Canvas.SetLeft(el, x);
            Canvas.SetTop(el, y);
            el.Width = Math.Max(0, w);
            el.Height = Math.Max(0, h);
        }

        private void UpdateDim()
        {
            if (_selection.IsEmpty || _selection.Width <= 0 || _selection.Height <= 0)
            {
                Place(DimTop, 0, 0, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height);
                Place(DimLeft, 0, 0, 0, 0);
                Place(DimRight, 0, 0, 0, 0);
                Place(DimBottom, 0, 0, 0, 0);
                return;
            }
            var s = _selection;
            Place(DimTop, 0, 0, Width, s.Top);
            Place(DimLeft, 0, s.Top, s.X, s.Height);
            Place(DimRight, s.Right, s.Top, Width - s.Right, s.Height);
            Place(DimBottom, 0, s.Bottom, Width, Height - s.Bottom);
        }

        private void UpdateSelectionUI()
        {
            if (_selection.IsEmpty || _selection.Width < 2 || _selection.Height < 2)
            {
                SelectionBorder.Visibility = Visibility.Collapsed;
                HandlesCanvas.Visibility = Visibility.Collapsed;
                SizeBadge.Visibility = Visibility.Collapsed;
                return;
            }

            var s = _selection;
            SelectionBorder.Visibility = Visibility.Visible;
            Canvas.SetLeft(SelectionBorder, s.X);
            Canvas.SetTop(SelectionBorder, s.Y);
            SelectionBorder.Width = s.Width;
            SelectionBorder.Height = s.Height;

            SizeText.Text = string.Format("{0} × {1}",
                (int)Math.Round(s.Width * _scale), (int)Math.Round(s.Height * _scale));
            SizeBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(SizeBadge, s.X);
            double badgeY = s.Y - SizeBadge.DesiredSize.Height - 6;
            if (badgeY < 2) badgeY = s.Bottom + 6;
            Canvas.SetTop(SizeBadge, badgeY);
            SizeBadge.Visibility = Visibility.Visible;

            bool editing = _state == State.Editing || _state == State.Resizing
                || _state == State.DrawShape || _state == State.MoveShape;
            HandlesCanvas.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            if (editing) UpdateHandles();
            if (_state == State.Editing) PositionToolbar();
        }

        private void UpdateHandles()
        {
            var s = _selection;
            double[][] pos =
            {
                new[] { s.X, s.Y }, new[] { s.X + s.Width / 2, s.Y },
                new[] { s.Right, s.Y }, new[] { s.Right, s.Y + s.Height / 2 },
                new[] { s.Right, s.Bottom }, new[] { s.X + s.Width / 2, s.Bottom },
                new[] { s.X, s.Bottom }, new[] { s.X, s.Y + s.Height / 2 }
            };
            for (int i = 0; i < 8; i++)
            {
                Canvas.SetLeft(_handles[i], pos[i][0] - 4.5);
                Canvas.SetTop(_handles[i], pos[i][1] - 4.5);
            }
        }

        private void UpdateShapeClip()
        {
            ShapeLayer.Clip = new RectangleGeometry(_selection);
        }

        private void ConfigureToolbarViewport(double windowWidth)
        {
            double maxWidth = Math.Max(80, windowWidth - 16);
            ToolBar.MaxWidth = maxWidth;
            ToolBarScroll.MaxWidth = Math.Max(60, maxWidth - 18);
            PropertyPanel.MaxWidth = maxWidth;
            PropertyPanelScroll.MaxWidth = Math.Max(60, maxWidth - 22);
        }

        private void PositionToolbar()
        {
            ConfigureToolbarViewport(Width);
            double maxWidth = Math.Max(80, Width - 16);
            ToolBar.Measure(new Size(maxWidth, double.PositiveInfinity));
            double tw = Math.Min(maxWidth, ToolBar.DesiredSize.Width);
            double th = ToolBar.DesiredSize.Height;
            double tx = Math.Max(8, Math.Min(Width - tw - 8, _selection.X + _selection.Width / 2 - tw / 2));
            double ty = _selection.Bottom + 12;
            if (ty + th > Height - 4) ty = Math.Max(4, _selection.Y - th - 12);
            Canvas.SetLeft(ToolBar, tx);
            Canvas.SetTop(ToolBar, ty);

            if (PropertyPanel.Visibility != Visibility.Visible) return;

            PropertyPanel.Measure(new Size(maxWidth, double.PositiveInfinity));
            double pw = Math.Min(maxWidth, PropertyPanel.DesiredSize.Width);
            double ph = PropertyPanel.DesiredSize.Height;
            double px = Math.Max(8, Math.Min(Width - pw - 8, _selection.X + _selection.Width / 2 - pw / 2));
            double py = ty - ph - 8;
            if (py < 4) py = ty + th + 8;
            Canvas.SetLeft(PropertyPanel, px);
            Canvas.SetTop(PropertyPanel, py);
        }

        private void EnterEditing()
        {
            _state = State.Editing;
            HideMagnifier();
            WindowHoverBorder.Visibility = Visibility.Collapsed;
            ShapeLayer.Visibility = Visibility.Visible;
            ToolBar.Visibility = Visibility.Visible;
            UpdateDim();
            UpdateSelectionUI();
            UpdateShapeClip();
            UpdatePanel();
            Focus();
        }

        private void BackToHoverPick()
        {
            _state = State.HoverPick;
            _selection = Rect.Empty;
            ShapeLayer.Visibility = Visibility.Collapsed;
            ToolBar.Visibility = Visibility.Collapsed;
            PropertyPanel.Visibility = Visibility.Collapsed;
            UpdateDim();
            UpdateSelectionUI();
            Cursor = Cursors.Cross;
        }

        // ---------------------------------------------------------------- 放大镜 / 窗口吸附

        private void ShowMagnifier(Point p)
        {
            Magnifier.Visibility = Visibility.Visible;
            MagnifierText.Visibility = Visibility.Visible;
            MagnifierImage.Source = _cap.CropAround(p.X, p.Y, 5);

            double mx = p.X + 18, my = p.Y + 18;
            if (mx + 66 > Width) mx = p.X - 64 - 18;
            if (my + 86 > Height) my = p.Y - 64 - 18;
            Canvas.SetLeft(Magnifier, mx);
            Canvas.SetTop(Magnifier, my);

            var c = _cap.GetPixel(p.X, p.Y);
            RgbText.Text = string.Format("RGB({0}, {1}, {2})", c.R, c.G, c.B);
            MagnifierText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double tx = mx, ty = my + 68;
            if (tx + MagnifierText.DesiredSize.Width > Width) tx = Width - MagnifierText.DesiredSize.Width - 4;
            if (ty + 20 > Height) ty = my - 24;
            Canvas.SetLeft(MagnifierText, tx);
            Canvas.SetTop(MagnifierText, ty);
        }

        private void HideMagnifier()
        {
            Magnifier.Visibility = Visibility.Collapsed;
            MagnifierText.Visibility = Visibility.Collapsed;
        }

        private void UpdateWindowHover(Point p)
        {
            var win = WindowAt(p);
            if (win == null)
            {
                WindowHoverBorder.Visibility = Visibility.Collapsed;
                return;
            }
            var r = new Rect(
                (win.Bounds.X - _cap.PhysX) / _scale,
                (win.Bounds.Y - _cap.PhysY) / _scale,
                win.Bounds.Width / _scale,
                win.Bounds.Height / _scale);
            Place(WindowHoverBorder, r.X, r.Y, r.Width, r.Height);
            WindowHoverBorder.Visibility = Visibility.Visible;
        }

        private NativeWindowInfo WindowAt(Point dip)
        {
            double px = dip.X * _scale + _cap.PhysX;
            double py = dip.Y * _scale + _cap.PhysY;
            for (int i = _windows.Count - 1; i >= 0; i--)
            {
                var b = _windows[i].Bounds;
                if (px >= b.X && px <= b.Right && py >= b.Y && py <= b.Bottom)
                    return _windows[i];
            }
            return null;
        }

        private Rect WindowToDip(NativeWindowInfo w)
        {
            var captureBounds = new Rect(
                _cap.PhysX, _cap.PhysY, _cap.RawBitmap.Width, _cap.RawBitmap.Height);
            var visibleBounds = Rect.Intersect(captureBounds, w.Bounds);
            if (visibleBounds.IsEmpty) return Rect.Empty;

            return new Rect(
                (visibleBounds.X - _cap.PhysX) / _scale,
                (visibleBounds.Y - _cap.PhysY) / _scale,
                visibleBounds.Width / _scale,
                visibleBounds.Height / _scale);
        }

        // ---------------------------------------------------------------- 鼠标主流程

        private Point GetPos(MouseEventArgs e) { return e.GetPosition(this); }

        private bool InsideSelection(Point p) { return !_selection.IsEmpty && _selection.Contains(p); }

        private Point ClampToCapture(Point p)
        {
            return new Point(
                Math.Max(0, Math.Min(Width, p.X)),
                Math.Max(0, Math.Min(Height, p.Y)));
        }

        private Point ClampToSelection(Point p)
        {
            if (_selection.IsEmpty) return p;
            return new Point(
                Math.Max(_selection.Left, Math.Min(_selection.Right, p.X)),
                Math.Max(_selection.Top, Math.Min(_selection.Bottom, p.Y)));
        }

        private void MoveShapeWithinSelection(UIElement element, double left, double top)
        {
            if (element == null || _selection.IsEmpty) return;

            double width = 0, height = 0;
            if (element is FrameworkElement frameworkElement)
            {
                frameworkElement.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                width = frameworkElement.RenderSize.Width;
                height = frameworkElement.RenderSize.Height;
                if (width <= 0) width = frameworkElement.DesiredSize.Width;
                if (height <= 0) height = frameworkElement.DesiredSize.Height;
                if (width <= 0 && !double.IsNaN(frameworkElement.Width)) width = frameworkElement.Width;
                if (height <= 0 && !double.IsNaN(frameworkElement.Height)) height = frameworkElement.Height;
            }

            double minLeft = _selection.Left;
            double minTop = _selection.Top;
            double maxLeft = Math.Max(minLeft, _selection.Right - width);
            double maxTop = Math.Max(minTop, _selection.Bottom - height);
            Canvas.SetLeft(element, Math.Max(minLeft, Math.Min(maxLeft, left)));
            Canvas.SetTop(element, Math.Max(minTop, Math.Min(maxTop, top)));
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            var p = GetPos(e);
            switch (_state)
            {
                case State.HoverPick:
                    UpdateWindowHover(p);
                    ShowMagnifier(p);
                    break;
                case State.DragSelect:
                    var capturePoint = ClampToCapture(p);
                    _selection = new Rect(_pressPoint, capturePoint);
                    UpdateDim();
                    UpdateSelectionUI();
                    ShowMagnifier(capturePoint);
                    break;
                case State.Resizing:
                    ApplyResize(p);
                    UpdateDim();
                    UpdateSelectionUI();
                    UpdateShapeClip();
                    break;
                case State.Editing:
                    UpdateCursor(p);
                    break;
                case State.DrawShape:
                    UpdateDrawShape(ClampToSelection(p));
                    break;
                case State.MoveShape:
                    if (_selectedShape != null)
                    {
                        double left = _moveOriginLeft + (p.X - _moveStartPoint.X);
                        double top = _moveOriginTop + (p.Y - _moveStartPoint.Y);
                        MoveShapeWithinSelection(_selectedShape, left, top);
                    }
                    break;
            }
        }

        private void UpdateCursor(Point p)
        {
            if (InsideSelection(p))
                Cursor = _tool == Tool.None ? Cursors.Arrow : (_tool == Tool.Text ? Cursors.IBeam : Cursors.Cross);
            else
                Cursor = Cursors.Cross;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var p = GetPos(e);
            var source = e.OriginalSource as DependencyObject;

            if (source != null &&
                (IsDescendantOf(source, ToolBar) || IsDescendantOf(source, PropertyPanel)))
                return;

            if (_editingText != null)
            {
                if (source != null && IsDescendantOf(source, _editingText)) return;
                CommitEditingText();
            }

            switch (_state)
            {
                case State.HoverPick:
                    _pressPoint = p;
                    _state = State.DragSelect;
                    WindowHoverBorder.Visibility = Visibility.Collapsed;
                    Cursor = Cursors.Cross;
                    break;

                case State.Editing:
                    if (e.OriginalSource is DependencyObject src &&
                        (IsDescendantOf(src, ToolBar) || IsDescendantOf(src, PropertyPanel)))
                        return; // 点击工具栏/属性面板空白区不触发画布逻辑
                    if (InsideSelection(p))
                    {
                        var hit = HitShape(p);
                        if (e.ClickCount >= 2)
                        {
                            TryEditTextOnDoubleClick(hit);
                            return;
                        }
                        if (hit != null)
                        {
                            SelectShape(hit);
                            BeginMoveShape(p);
                        }
                        else if (_tool == Tool.Text)
                        {
                            StartText(p);
                        }
                        else if (_tool != Tool.None)
                        {
                            StartDrawShape(p);
                        }
                        else
                        {
                            DeselectShape();
                        }
                    }
                    else
                    {
                        DeselectShape();
                        BackToHoverPick();
                        _pressPoint = p;
                        _state = State.DragSelect;
                        Cursor = Cursors.Cross;
                    }
                    break;
            }
        }

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var p = GetPos(e);
            switch (_state)
            {
                case State.DragSelect:
                    if (_selection.Width < 2 || _selection.Height < 2)
                    {
                        var win = WindowAt(_pressPoint);
                        if (win != null)
                        {
                            _selection = WindowToDip(win);
                            EnterEditing();
                        }
                        else
                        {
                            _selection = Rect.Empty;
                            _state = State.HoverPick;
                            UpdateDim();
                            UpdateSelectionUI();
                            Cursor = Cursors.Cross;
                        }
                    }
                    else
                    {
                        EnterEditing();
                    }
                    break;

                case State.Resizing:
                    _state = State.Editing;
                    _resizeHandle = -1;
                    UpdateSelectionUI();
                    break;

                case State.DrawShape:
                    CommitDrawShape(ClampToSelection(p));
                    _state = State.Editing;
                    break;

                case State.MoveShape:
                    EndMoveShape(p);
                    _state = State.Editing;
                    break;
            }
        }

        private void HandleDown(int index, MouseButtonEventArgs e)
        {
            if (_state != State.Editing) return;
            _state = State.Resizing;
            _resizeHandle = index;
            _resizeStartRect = _selection;
            _pressPoint = GetPos(e);
            e.Handled = true;
        }

        private void ApplyResize(Point p)
        {
            var d = p - _pressPoint;
            var s = _resizeStartRect;
            double l = s.X, t = s.Y, r = s.Right, b = s.Bottom;

            switch (_resizeHandle)
            {
                case 0: l += d.X; t += d.Y; break;
                case 1: t += d.Y; break;
                case 2: r += d.X; t += d.Y; break;
                case 3: r += d.X; break;
                case 4: r += d.X; b += d.Y; break;
                case 5: b += d.Y; break;
                case 6: l += d.X; b += d.Y; break;
                case 7: l += d.X; break;
            }

            if (r - l < 8) r = l + 8;
            if (b - t < 8) b = t + 8;
            l = Math.Max(0, l);
            t = Math.Max(0, t);
            r = Math.Min(Width, r);
            b = Math.Min(Height, b);
            if (r - l < 8 || b - t < 8) return;

            _selection = new Rect(l, t, r - l, b - t);
        }

        // ---------------------------------------------------------------- 图元绘制

        private void StartDrawShape(Point p)
        {
            _drawStart = p;
            _drawTool = _tool;
            _state = State.DrawShape;

            var stroke = new SolidColorBrush(_color);
            stroke.Freeze();
            double w = CurrentThickness();

            switch (_tool)
            {
                case Tool.Rect:
                    _drawShape = new Rectangle
                    {
                        Stroke = stroke, StrokeThickness = w,
                        Fill = Brushes.Transparent, StrokeLineJoin = PenLineJoin.Round
                    };
                    break;
                case Tool.Ellipse:
                    _drawShape = new Ellipse
                    {
                        Stroke = stroke, StrokeThickness = w, Fill = Brushes.Transparent
                    };
                    break;
                case Tool.Arrow:
                    _drawShape = new ArrowShape
                    {
                        X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y,
                        Stroke = stroke, StrokeThickness = w,
                        StrokeStartLineCap = PenLineCap.Round
                    };
                    Canvas.SetLeft(_drawShape, 0);
                    Canvas.SetTop(_drawShape, 0);
                    break;
                case Tool.Line:
                    _drawStroke = new Polyline
                    {
                        Stroke = stroke,
                        StrokeThickness = w,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        Fill = null
                    };
                    _drawStroke.Points.Add(p);
                    _drawStroke.Points.Add(p);
                    _drawShape = _drawStroke;
                    break;
                case Tool.Pen:
                case Tool.Highlighter:
                    _drawStroke = new Polyline
                    {
                        Stroke = stroke,
                        StrokeThickness = _tool == Tool.Highlighter ? w * 1.8 : w,
                        StrokeLineJoin = PenLineJoin.Round,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        Opacity = _tool == Tool.Highlighter ? 0.45 : 1.0,
                        Fill = null
                    };
                    _drawStroke.Points.Add(p);
                    _drawShape = _drawStroke;
                    break;
                case Tool.Mosaic:
                    _drawShape = new Rectangle
                    {
                        Stroke = new SolidColorBrush(Color.FromRgb(0x86, 0x90, 0x9C)),
                        StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 },
                        Fill = Brushes.Transparent
                    };
                    break;
                default:
                    _state = State.Editing;
                    return;
            }

            if (_drawShape is Polyline)
            {
                // Polyline points are held in screen-canvas coordinates while drawing, then
                // shifted into element-local coordinates when committed. Keep its visual origin
                // at the canvas origin during preview; never force its Width/Height to zero.
                Canvas.SetLeft(_drawShape, 0);
                Canvas.SetTop(_drawShape, 0);
            }
            else if (_drawShape != null && !(_drawShape is ArrowShape))
            {
                Canvas.SetLeft(_drawShape, p.X);
                Canvas.SetTop(_drawShape, p.Y);
                _drawShape.Width = 0;
                _drawShape.Height = 0;
            }
            ShapeCanvas.Children.Add(_drawShape);
        }

        private void UpdateDrawShape(Point p)
        {
            UpdateDrawShape(p, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        }

        private void UpdateDrawShape(Point p, bool shiftPressed)
        {
            if (_drawShape == null) return;
            switch (_drawTool)
            {
                case Tool.Rect:
                case Tool.Ellipse:
                case Tool.Mosaic:
                    PlaceFromPoints(_drawShape, _drawStart, p);
                    break;
                case Tool.Arrow:
                    var a = (ArrowShape)_drawShape;
                    a.X2 = p.X;
                    a.Y2 = p.Y;
                    break;
                case Tool.Line:
                    _drawStroke.Points[1] = ConstrainLineEndpoint(_drawStart, p, shiftPressed);
                    break;
                case Tool.Pen:
                case Tool.Highlighter:
                    _drawStroke.Points.Add(p);
                    break;
            }
        }

        private static Point ConstrainLineEndpoint(Point start, Point end, bool shiftPressed)
        {
            return shiftPressed ? new Point(end.X, start.Y) : end;
        }

        private static void PlaceFromPoints(FrameworkElement el, Point a, Point b)
        {
            double x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
            double w = Math.Abs(a.X - b.X), h = Math.Abs(a.Y - b.Y);
            Canvas.SetLeft(el, x);
            Canvas.SetTop(el, y);
            el.Width = w;
            el.Height = h;
        }

        private void CommitDrawShape(Point p)
        {
            CommitDrawShape(p, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        }

        private void CommitDrawShape(Point p, bool shiftPressed)
        {
            var shape = _drawShape;
            var tool = _drawTool;
            _drawShape = null;
            _drawStroke = null;
            _drawTool = Tool.None;

            if (shape == null) return;
            double w = Math.Abs(p.X - _drawStart.X);
            double h = Math.Abs(p.Y - _drawStart.Y);

            switch (tool)
            {
                case Tool.Rect:
                case Tool.Ellipse:
                    if (w < 3 && h < 3) { ShapeCanvas.Children.Remove(shape); return; }
                    PushUndo(new ShapeAddCommand(ShapeCanvas, shape));
                    break;

                case Tool.Arrow:
                    var a = (ArrowShape)shape;
                    if (w < 3 && h < 3) { ShapeCanvas.Children.Remove(a); return; }
                    double minX = Math.Min(a.X1, a.X2), minY = Math.Min(a.Y1, a.Y2);
                    a.X1 -= minX; a.Y1 -= minY; a.X2 -= minX; a.Y2 -= minY;
                    Canvas.SetLeft(a, minX);
                    Canvas.SetTop(a, minY);
                    PushUndo(new ShapeAddCommand(ShapeCanvas, a));
                    break;

                case Tool.Line:
                case Tool.Pen:
                case Tool.Highlighter:
                    var line = shape as Polyline;
                    if (line == null || line.Points.Count < 2) { ShapeCanvas.Children.Remove(shape); return; }
                    if (tool == Tool.Line)
                    {
                        var endpoint = ConstrainLineEndpoint(_drawStart, p, shiftPressed);
                        line.Points[1] = endpoint;
                        if ((endpoint - _drawStart).Length < 3) { ShapeCanvas.Children.Remove(shape); return; }
                    }
                    double lx = double.MaxValue, ly = double.MaxValue;
                    foreach (var pt in line.Points)
                    {
                        lx = Math.Min(lx, pt.X); ly = Math.Min(ly, pt.Y);
                    }
                    var shifted = new PointCollection();
                    foreach (var pt in line.Points) shifted.Add(new Point(pt.X - lx, pt.Y - ly));
                    line.Points = shifted;
                    Canvas.SetLeft(line, lx);
                    Canvas.SetTop(line, ly);
                    PushUndo(new ShapeAddCommand(ShapeCanvas, line));
                    break;

                case Tool.Mosaic:
                    ShapeCanvas.Children.Remove(shape);
                    if (w < 6 || h < 6) return;
                    var region = new Int32Rect(
                        (int)(Math.Min(_drawStart.X, p.X) * _scale),
                        (int)(Math.Min(_drawStart.Y, p.Y) * _scale),
                        (int)Math.Round(w * _scale), (int)Math.Round(h * _scale));
                    var mosaic = MosaicService.CreateMosaic(_cap.RawBitmap, region, BlockPhys());
                    if (mosaic == null) return;
                    var img = new Image
                    {
                        Source = mosaic,
                        Width = Math.Abs(_drawStart.X - p.X),
                        Height = Math.Abs(_drawStart.Y - p.Y),
                        Tag = region
                    };
                    Canvas.SetLeft(img, Math.Min(_drawStart.X, p.X));
                    Canvas.SetTop(img, Math.Min(_drawStart.Y, p.Y));
                    ShapeCanvas.Children.Add(img);
                    PushUndo(new ShapeAddCommand(ShapeCanvas, img));
                    break;
            }
        }

        private double CurrentThickness() { return Thicknesses[_thicknessIndex]; }

        private int BlockPhys() { return Math.Max(4, (int)(CurrentThickness() * _scale * 2)); }

        // ---------------------------------------------------------------- 文字工具

        private void StartText(Point p)
        {
            var tb = new TextBox
            {
                FontFamily = new FontFamily(SelectedFont()),
                FontSize = ParsedSize(),
                FontWeight = BoldBtn.IsChecked == true ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = ItalicBtn.IsChecked == true ? FontStyles.Italic : FontStyles.Normal,
                Foreground = new SolidColorBrush(_color),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                MinWidth = 12,
                Padding = new Thickness(1)
            };
            if (UnderlineBtn.IsChecked == true) tb.TextDecorations = TextDecorations.Underline;
            Canvas.SetLeft(tb, p.X);
            Canvas.SetTop(tb, p.Y - tb.FontSize / 2);
            ShapeCanvas.Children.Add(tb);
            AttachEditingBox(tb);
            tb.Focus();
        }

        private void AttachEditingBox(TextBox tb)
        {
            _editingText = tb;
            tb.TextChanged += EditingText_TextChanged;
            tb.LostKeyboardFocus += EditingText_LostFocus;
            SizeEditingBox();
        }

        private void SizeEditingBox()
        {
            var tb = _editingText;
            if (tb == null) return;
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            tb.Width = Math.Max(12, tb.DesiredSize.Width + 2);
        }

        private void EditingText_TextChanged(object sender, TextChangedEventArgs e)
        {
            SizeEditingBox();
        }

        private void EditingText_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (_editingText == null) return;
            var focused = Keyboard.FocusedElement as DependencyObject;
            if (focused != null && (IsDescendantOf(focused, PropertyPanel) || IsDescendantOf(focused, ToolBar)))
                return; // 焦点移到面板/工具栏：保持编辑态，实时应用属性
            CommitEditingText();
        }

        private void CommitEditingText()
        {
            var tb = _editingText;
            if (tb == null) return;
            _editingText = null;
            tb.TextChanged -= EditingText_TextChanged;
            tb.LostKeyboardFocus -= EditingText_LostFocus;
            ShapeCanvas.Children.Remove(tb);

            bool empty = string.IsNullOrWhiteSpace(tb.Text);
            var block = BuildTextBlockFrom(tb, tb.Text);

            if (tb.Tag is TextBlock original)
            {
                // 二次编辑：用 DelegateCommand 保留新旧两个元素引用
                if (empty)
                {
                    ShapeCanvas.Children.Add(original);
                }
                else
                {
                    ShapeCanvas.Children.Add(block);
                    PushUndo(new DelegateCommand(
                        () => { ShapeCanvas.Children.Remove(block); ShapeCanvas.Children.Add(original); },
                        () => { ShapeCanvas.Children.Remove(original); ShapeCanvas.Children.Add(block); }));
                }
                Focus();
                return;
            }

            if (!empty)
            {
                ShapeCanvas.Children.Add(block);
                PushUndo(new ShapeAddCommand(ShapeCanvas, block));
            }
            Focus();
        }

        private void CancelEditingText()
        {
            var tb = _editingText;
            if (tb == null) return;
            _editingText = null;
            tb.TextChanged -= EditingText_TextChanged;
            tb.LostKeyboardFocus -= EditingText_LostFocus;
            if (tb.Tag is TextBlock original)
                ShapeCanvas.Children.Add(original); // 还原
            else
                ShapeCanvas.Children.Remove(tb);
            Focus();
        }

        private bool TryEditTextOnDoubleClick(UIElement hit)
        {
            if (!(hit is TextBlock text)) return false;
            BeginEditTextExisting(text);
            return true;
        }

        private void BeginEditTextExisting(TextBlock block)
        {
            CommitPendingSizeEdit();
            DeselectShape();
            _tool = Tool.Text;
            TextToolBtn.IsChecked = true;
            ReflectPanelFrom(block);
            var tb = new TextBox
            {
                Text = block.Text,
                FontFamily = block.FontFamily,
                FontSize = block.FontSize,
                FontWeight = block.FontWeight,
                FontStyle = block.FontStyle,
                Foreground = block.Foreground,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                MinWidth = 12,
                Padding = new Thickness(1),
                Tag = block
            };
            if (block.TextDecorations != null) tb.TextDecorations = block.TextDecorations;
            Canvas.SetLeft(tb, Canvas.GetLeft(block));
            Canvas.SetTop(tb, Canvas.GetTop(block));
            ShapeCanvas.Children.Remove(block);
            ShapeCanvas.Children.Add(tb);
            AttachEditingBox(tb);
            UpdatePanel();
            tb.Focus();
            tb.Select(tb.Text.Length, 0);
        }

        private static TextBlock BuildTextBlockFrom(TextBox tb, string text)
        {
            var block = new TextBlock
            {
                Text = text,
                FontFamily = tb.FontFamily,
                FontSize = tb.FontSize,
                FontWeight = tb.FontWeight,
                FontStyle = tb.FontStyle,
                Foreground = tb.Foreground,
                TextDecorations = tb.TextDecorations
            };
            Canvas.SetLeft(block, Canvas.GetLeft(tb));
            Canvas.SetTop(block, Canvas.GetTop(tb));
            return block;
        }

        private string SelectedFont()
        {
            return FontBox.SelectedItem as string ?? "Microsoft YaHei";
        }

        private double ParsedSize()
        {
            double value;
            if (double.TryParse(SizeBox.Text, out value)) return ClampFontSize(value);
            if (_editingText != null) return _editingText.FontSize;
            if (_selectedShape is TextBlock selectedText) return selectedText.FontSize;
            return 16;
        }

        private static double ClampFontSize(double value)
        {
            return Math.Max(8, Math.Min(72, value));
        }

        private void SetSizeBox(double value)
        {
            CommitPendingSizeEdit();
            double updated = ClampFontSize(value);
            _updatingPanel = true;
            SizeBox.Text = updated.ToString("0.##");
            _updatingPanel = false;

            if (_editingText != null)
            {
                _editingText.FontSize = updated;
                SizeEditingBox();
            }
            else if (_selectedShape is TextBlock selectedText)
            {
                double old = selectedText.FontSize;
                if (Math.Abs(old - updated) > 0.01)
                {
                    selectedText.FontSize = updated;
                    PushUndo(new DelegateCommand(
                        () => selectedText.FontSize = old,
                        () => selectedText.FontSize = updated));
                }
            }
        }

        private void CommitPendingSizeEdit()
        {
            if (!_sizeEditActive) return;

            var target = _sizeEditTarget;
            double oldSize = _sizeEditStartSize;
            double newSize = target != null ? target.FontSize : oldSize;
            _sizeEditActive = false;
            _sizeEditTarget = null;

            if (target != null && Math.Abs(oldSize - newSize) > 0.01)
            {
                PushUndo(new DelegateCommand(
                    () => target.FontSize = oldSize,
                    () => target.FontSize = newSize));
            }

            if (target != null)
            {
                _updatingPanel = true;
                SizeBox.Text = newSize.ToString("0.##");
                _updatingPanel = false;
            }
        }

        private void SizeBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            CommitPendingSizeEdit();
            if (_editingText != null)
                SetSizeBoxText(_editingText.FontSize);
            else if (_selectedShape is TextBlock selectedText)
                SetSizeBoxText(selectedText.FontSize);
            else if (!double.TryParse(SizeBox.Text, out _))
                SetSizeBoxText(16);
        }

        private void SizeBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            CommitPendingSizeEdit();
            e.Handled = true;
        }

        private void SetSizeBoxText(double value)
        {
            _updatingPanel = true;
            SizeBox.Text = value.ToString("0.##");
            _updatingPanel = false;
        }

        // ---------------------------------------------------------------- 图元选中 / 移动 / 命中

        private UIElement HitShape(Point p)
        {
            var hit = ShapeCanvas.InputHitTest(p);
            if (ReferenceEquals(hit, ShapeCanvas) || ReferenceEquals(hit, ShapeLayer)) return null;
            return hit as UIElement;
        }

        private void SelectShape(UIElement el)
        {
            if (_selectedShape == el) return;
            DeselectShape();
            _selectedShape = el;
            var layer = AdornerLayer.GetAdornerLayer(el);
            if (layer != null)
            {
                _selAdorner = new DashedSelectionAdorner(el);
                layer.Add(_selAdorner);
            }
            ReflectPanelFrom(el);
            UpdatePanel();
        }

        private void DeselectShape()
        {
            CommitPendingSizeEdit();
            if (_selectedShape != null && _selAdorner != null)
            {
                var layer = AdornerLayer.GetAdornerLayer(_selectedShape);
                if (layer != null) layer.Remove(_selAdorner);
            }
            _selAdorner = null;
            _selectedShape = null;
            UpdatePanel();
        }

        private void BeginMoveShape(Point p)
        {
            _state = State.MoveShape;
            _moveStartPoint = p;
            _moveOriginLeft = Canvas.GetLeft(_selectedShape);
            _moveOriginTop = Canvas.GetTop(_selectedShape);
            if (double.IsNaN(_moveOriginLeft)) _moveOriginLeft = 0;
            if (double.IsNaN(_moveOriginTop)) _moveOriginTop = 0;
        }

        private void EndMoveShape(Point p)
        {
            var el = _selectedShape;
            if (el == null) return;
            double nl = Canvas.GetLeft(el), nt = Canvas.GetTop(el);
            if (Math.Abs(nl - _moveOriginLeft) < 0.5 && Math.Abs(nt - _moveOriginTop) < 0.5) return;
            PushUndo(new DelegateCommand(
                () => { Canvas.SetLeft(el, _moveOriginLeft); Canvas.SetTop(el, _moveOriginTop); },
                () => { Canvas.SetLeft(el, nl); Canvas.SetTop(el, nt); }));
        }

        private void DeleteSelected()
        {
            var el = _selectedShape;
            if (el == null) return;
            DeselectShape();
            ShapeCanvas.Children.Remove(el);
            PushUndo(new DelegateCommand(
                () => ShapeCanvas.Children.Remove(el),
                () => { if (!ShapeCanvas.Children.Contains(el)) ShapeCanvas.Children.Add(el); }));
        }

        // ---------------------------------------------------------------- 撤销 / 重做

        private void PushUndo(EditCommand cmd)
        {
            _undo.Add(cmd);
            if (_undo.Count > 100) _undo.RemoveAt(0);
            _redo.Clear();
            UpdateUndoRedoButtons();
        }

        private void DoUndo()
        {
            CommitEditingText();
            DeselectShape();
            if (_undo.Count == 0) return;
            var cmd = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            cmd.Undo();
            _redo.Add(cmd);
            UpdateUndoRedoButtons();
        }

        private void DoRedo()
        {
            CommitEditingText();
            DeselectShape();
            if (_redo.Count == 0) return;
            var cmd = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            cmd.Redo();
            _undo.Add(cmd);
            UpdateUndoRedoButtons();
        }

        private void UpdateUndoRedoButtons()
        {
            UndoBtn.IsEnabled = _undo.Count > 0;
            RedoBtn.IsEnabled = _redo.Count > 0;
        }

        // ---------------------------------------------------------------- 属性面板

        private void UpdatePanel()
        {
            bool show = _tool != Tool.None || _selectedShape != null || _editingText != null;
            PropertyPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

            bool textMode = _tool == Tool.Text || _selectedShape is TextBlock || _editingText != null;
            TextPanel.Visibility = textMode ? Visibility.Visible : Visibility.Collapsed;
            ThicknessPanel.Visibility = textMode ? Visibility.Collapsed : Visibility.Visible;
            ThicknessDivider.Visibility = textMode ? Visibility.Collapsed : Visibility.Visible;

            if (show && !_selection.IsEmpty) PositionToolbar();
        }

        private void ReflectPanelFrom(UIElement el)
        {
            _updatingPanel = true;
            try
            {
                Color? c = null;
                double? thick = null;

                if (el is System.Windows.Shapes.Shape s)
                {
                    if (s.Stroke is SolidColorBrush b) c = b.Color;
                    thick = s.StrokeThickness;
                }
                else if (el is TextBlock tb)
                {
                    if (tb.Foreground is SolidColorBrush fb) c = fb.Color;
                    string family = tb.FontFamily.Source;
                    FontBox.SelectedItem = (FontBox.ItemsSource as List<string>)?.Contains(family) == true ? family : FontBox.SelectedItem;
                    SizeBox.Text = tb.FontSize.ToString("0.##");
                    BoldBtn.IsChecked = tb.FontWeight == FontWeights.Bold;
                    ItalicBtn.IsChecked = tb.FontStyle == FontStyles.Italic;
                    UnderlineBtn.IsChecked = tb.TextDecorations != null;
                }

                if (c.HasValue)
                {
                    _color = c.Value;
                    CheckSwatch(c.Value);
                }
                if (thick.HasValue)
                {
                    int idx = 0;
                    for (int i = 0; i < Thicknesses.Length; i++)
                        if (Math.Abs(Thicknesses[i] - thick.Value) < Thicknesses[i] * 0.6) idx = i;
                    _thicknessIndex = idx;
                    (idx == 0 ? ThinBtn : idx == 1 ? MidBtn : ThickBtn).IsChecked = true;
                }
            }
            finally
            {
                _updatingPanel = false;
            }
        }

        private void CheckSwatch(Color c)
        {
            foreach (var s in _swatches)
                s.IsChecked = ((Color)s.Tag).Equals(c);
        }

        private void Swatch_Click(object sender, RoutedEventArgs e)
        {
            if (_updatingPanel) return;
            var clicked = (ToggleButton)sender;
            foreach (var s in _swatches)
                s.IsChecked = ReferenceEquals(s, clicked);   // 手动互斥（ToggleButton 无 GroupName）
            _color = (Color)clicked.Tag;
            if (_editingText != null)
            {
                _editingText.Foreground = new SolidColorBrush(_color);
                return;
            }
            if (_selectedShape != null) ApplyColorToSelected(_color);
        }

        private void ApplyColorToSelected(Color c)
        {
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            var el = _selectedShape;
            if (el is System.Windows.Shapes.Shape s)
            {
                var old = s.Stroke;
                s.Stroke = brush;
                PushUndo(new DelegateCommand(() => s.Stroke = old, () => s.Stroke = brush));
            }
            else if (el is TextBlock tb)
            {
                var old = tb.Foreground;
                tb.Foreground = brush;
                PushUndo(new DelegateCommand(() => tb.Foreground = old, () => tb.Foreground = brush));
            }
        }

        private void Thickness_Click(object sender, RoutedEventArgs e)
        {
            if (_updatingPanel) return;
            _thicknessIndex = int.Parse((string)((FrameworkElement)sender).Tag);
            ApplyThicknessToSelected();
        }

        private void ApplyThicknessToSelected()
        {
            var el = _selectedShape;
            if (el is System.Windows.Shapes.Shape s)
            {
                double w = CurrentThickness();
                var old = s.StrokeThickness;
                s.StrokeThickness = w;
                PushUndo(new DelegateCommand(() => s.StrokeThickness = old, () => s.StrokeThickness = w));
            }
            else if (el is Image img && img.Tag is Int32Rect region)
            {
                var oldSrc = img.Source;
                var newSrc = MosaicService.CreateMosaic(_cap.RawBitmap, region, BlockPhys());
                if (newSrc == null) return;
                img.Source = newSrc;
                PushUndo(new DelegateCommand(() => img.Source = oldSrc, () => img.Source = newSrc));
            }
        }

        private void FontBox_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingPanel) return;
            var fam = new FontFamily(SelectedFont());
            if (_editingText != null)
            {
                _editingText.FontFamily = fam;
                SizeEditingBox();
                return;
            }
            if (_selectedShape is TextBlock tb)
            {
                CommitPendingSizeEdit();
                var old = tb.FontFamily;
                if (old.Source == fam.Source) return;
                tb.FontFamily = fam;
                PushUndo(new DelegateCommand(() => tb.FontFamily = old, () => tb.FontFamily = fam));
            }
        }

        private void SizeBox_Changed(object sender, TextChangedEventArgs e)
        {
            if (_updatingPanel) return;
            double input;
            if (!double.TryParse(SizeBox.Text, out input)) return;
            double fontSize = ClampFontSize(input);

            if (_editingText != null)
            {
                _editingText.FontSize = fontSize;
                SizeEditingBox();
                return;
            }
            if (_selectedShape is TextBlock textBlock)
            {
                if (!_sizeEditActive || _sizeEditTarget != textBlock)
                {
                    CommitPendingSizeEdit();
                    _sizeEditTarget = textBlock;
                    _sizeEditStartSize = textBlock.FontSize;
                    _sizeEditActive = true;
                }
                textBlock.FontSize = fontSize;
            }
        }

        private void FontMinus_Click(object sender, RoutedEventArgs e) { SetSizeBox(ParsedSize() - 2); }
        private void FontPlus_Click(object sender, RoutedEventArgs e) { SetSizeBox(ParsedSize() + 2); }

        private void TextFormat_Click(object sender, RoutedEventArgs e)
        {
            if (_updatingPanel) return;
            CommitPendingSizeEdit();
            if (_editingText != null)
            {
                ApplyFormat(_editingText);
                return;
            }
            if (_selectedShape is TextBlock tb)
            {
                var w = tb.FontWeight; var st = tb.FontStyle; var dec = tb.TextDecorations;
                ApplyFormat(tb);
                var nw = tb.FontWeight; var nst = tb.FontStyle; var ndec = tb.TextDecorations;
                PushUndo(new DelegateCommand(
                    () => { tb.FontWeight = w; tb.FontStyle = st; tb.TextDecorations = dec; },
                    () => { tb.FontWeight = nw; tb.FontStyle = nst; tb.TextDecorations = ndec; }));
            }
        }

        private void ApplyFormat(TextBox tb)
        {
            tb.FontWeight = BoldBtn.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
            tb.FontStyle = ItalicBtn.IsChecked == true ? FontStyles.Italic : FontStyles.Normal;
            tb.TextDecorations = UnderlineBtn.IsChecked == true ? TextDecorations.Underline : null;
            SizeEditingBox();
        }

        private void ApplyFormat(TextBlock tb)
        {
            tb.FontWeight = BoldBtn.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
            tb.FontStyle = ItalicBtn.IsChecked == true ? FontStyles.Italic : FontStyles.Normal;
            tb.TextDecorations = UnderlineBtn.IsChecked == true ? TextDecorations.Underline : null;
        }

        // ---------------------------------------------------------------- 工具栏事件

        private void ToolBtn_Click(object sender, RoutedEventArgs e)
        {
            CommitPendingSizeEdit();
            CommitEditingText();
            DeselectShape();
            var tag = (string)((FrameworkElement)sender).Tag;
            _tool = tag switch
            {
                "Rect" => Tool.Rect,
                "Ellipse" => Tool.Ellipse,
                "Line" => Tool.Line,
                "Arrow" => Tool.Arrow,
                "Pen" => Tool.Pen,
                "Highlighter" => Tool.Highlighter,
                "Mosaic" => Tool.Mosaic,
                "Text" => Tool.Text,
                _ => Tool.None
            };
            UpdatePanel();
        }

        private void UndoBtn_Click(object sender, RoutedEventArgs e) { DoUndo(); }
        private void RedoBtn_Click(object sender, RoutedEventArgs e) { DoRedo(); }

        private void PinBtn_Click(object sender, RoutedEventArgs e)
        {
            var img = Compose();
            if (img == null) { Close(); return; }
            var pin = new PinWindow(img, _selection.Width, _selection.Height,
                Left + _selection.X, Top + _selection.Y);
            pin.Show();
            Close();
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e) { SaveAndClose(); }

        private void CopyBtn_Click(object sender, RoutedEventArgs e) { CopyAndClose(); }

        private void NewCaptureBtn_Click(object sender, RoutedEventArgs e)
        {
            var app = Application.Current as App;
            _suppressIndicatorRestore = true;
            Close();
            if (app != null)
                app.Dispatcher.BeginInvoke(new Action(app.StartCapture), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) { Close(); }

        private void HelpBtn_Click(object sender, RoutedEventArgs e)
        {
            new HelpWindow(App.CaptureHotkey) { Owner = this }.ShowDialog();
        }

        // ---------------------------------------------------------------- 输出与快捷键

        private BitmapSource Compose()
        {
            if (_selection.IsEmpty || _selection.Width < 1) return null;
            CommitPendingSizeEdit();
            CommitEditingText();
            ShapeCanvas.UpdateLayout();
            return ImageExporter.Compose(_cap.Image, ShapeCanvas, _scale, _selection);
        }

        private void CopyAndClose()
        {
            var img = Compose();
            if (img != null)
            {
                ImageExporter.CopyToClipboard(img);
                (Application.Current as App)?.NotifyCaptureCopied();
            }
            Close();
        }

        private void SaveAndClose()
        {
            var img = Compose();
            if (img == null) { Close(); return; }
            if (ImageExporter.SaveToFile(img, this)) Close();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var keySource = e.OriginalSource as DependencyObject;
            if (e.Key == Key.Enter && keySource != null && IsDescendantOf(keySource, SizeBox))
            {
                CommitPendingSizeEdit();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                if (_editingText != null) { CancelEditingText(); e.Handled = true; return; }
                if (_selectedShape != null) { DeselectShape(); e.Handled = true; return; }
                Close();
                e.Handled = true;
                return;
            }

            if (_editingText != null)
            {
                if (e.Key == Key.Enter)
                {
                    CommitEditingText();
                    e.Handled = true;
                }
                return; // 其余按键交给输入框
            }

            var modifiers = Keyboard.Modifiers;
            bool ctrl = modifiers.HasFlag(ModifierKeys.Control);
            if (ctrl && e.Key == Key.Z)
            {
                if (modifiers.HasFlag(ModifierKeys.Shift)) DoRedo(); else DoUndo();
                e.Handled = true;
            }
            else if (ctrl && e.Key == Key.Y) { DoRedo(); e.Handled = true; }
            else if (ctrl && e.Key == Key.C) { CopyAndClose(); e.Handled = true; }
            else if (ctrl && e.Key == Key.S) { SaveAndClose(); e.Handled = true; }
            else if (e.Key == Key.Enter) { CopyAndClose(); e.Handled = true; }
            else if (e.Key == Key.Delete)
            {
                if (_selectedShape != null) { DeleteSelected(); e.Handled = true; }
            }
        }

        private static bool IsDescendantOf(DependencyObject node, DependencyObject ancestor)
        {
            while (node != null)
            {
                if (ReferenceEquals(node, ancestor)) return true;
                node = System.Windows.Media.VisualTreeHelper.GetParent(node);
            }
            return false;
        }
    }
}
