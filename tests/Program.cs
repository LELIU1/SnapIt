using System;
using System.IO;
using GdiBitmap = System.Drawing.Bitmap;
using GdiColor = System.Drawing.Color;
using IOPath = System.IO.Path;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Reflection;
using System.Windows.Controls.Primitives;
using SnapIt.Model;
using SnapIt.Services;
using SnapIt.Windows;

namespace SnapIt.AcceptanceTests
{
    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            int failures = 0;
            failures += Run("capture-window XAML and construction smoke", TestCaptureWindowConstruction);
            failures += Run("startup acknowledgement and dynamic tray shortcut", TestStartupFeedback);
            failures += Run("hotkey fallback never takes over PrintScreen", TestHotkeyFallbackPolicy);
            failures += Run("editor toolbar and dynamic shortcut help", TestToolbarAndHelp);
            failures += Run("narrow-screen toolbar remains reachable", TestNarrowToolbarViewport);
            failures += Run("Shift constrains straight line horizontally", TestShiftConstrainedLine);
            failures += Run("toolbar starts a fresh screenshot session", TestNewCaptureToolbarAction);
            failures += Run("pin-window XAML and image initialization", TestPinWindowConstruction);
            failures += Run("capture-window edited layers save end-to-end", TestCaptureWindowComposedSavePipeline);
            failures += Run("compose preserves cropped base pixels at 2x", TestBaseCrop);
            failures += Run("compose renders all committed annotation types", TestAnnotationLayers);
            failures += Run("compose scales annotation layers at 125%", TestScaledAnnotationAt125);
            failures += Run("compose scales annotation layers at 150%", TestScaledAnnotation);
            failures += Run("compose clips layers at selection bounds", TestSelectionClip);
            failures += Run("undo and redo change exported image", TestUndoRedoExport);
            failures += Run("PNG/JPEG output dimensions and encoders", TestFileExports);
            failures += Run("mosaic generation and composition", TestMosaic);
            failures += Run("desktop indicator is hidden/restored around capture", TestDesktopIndicatorLifecycle);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ACCEPTANCE RESULT: PASS" : "ACCEPTANCE RESULT: FAIL (" + failures + ")");
            return failures == 0 ? 0 : 1;
        }

        private static int Run(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine("PASS  " + name);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL  " + name + " :: " + ex.Message);
                return 1;
            }
        }

        private static void TestCaptureWindowConstruction()
        {
            var window = new CaptureWindow();
            try
            {
                Assert(CaptureWindow.IsOpen, "Capture window should register itself during construction.");
                window.Opacity = 0;
                window.Show();
                window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

                var handlesField = typeof(CaptureWindow).GetField("_handles", BindingFlags.Instance | BindingFlags.NonPublic);
                var handles = handlesField.GetValue(window) as Rectangle[];
                Assert(handles != null && handles.Length == 8, "Selection handles were not initialized.");
                for (int i = 0; i < handles.Length; i++)
                    Assert(handles[i] != null, "Selection handle " + i + " was not initialized.");

                var swatchesField = typeof(CaptureWindow).GetField("_swatches", BindingFlags.Instance | BindingFlags.NonPublic);
                var swatches = swatchesField.GetValue(window) as System.Collections.Generic.List<ToggleButton>;
                Assert(swatches != null && swatches.Count == 8, "Color palette was not initialized exactly once.");

                var fontBox = window.FindName("FontBox") as ComboBox;
                Assert(fontBox != null && fontBox.Items.Count > 0, "Font list did not initialize.");

                typeof(CaptureWindow).GetField("_selection", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(window, new Rect(10, 10, 180, 100));
                typeof(CaptureWindow).GetMethod("EnterEditing", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(window, null);
                var toolbar = window.FindName("ToolBar") as Border;
                var helpButton = window.FindName("HelpBtn") as Button;
                var newCaptureButton = window.FindName("NewCaptureBtn") as Button;
                Assert(toolbar != null && toolbar.Visibility == Visibility.Visible, "Editor toolbar did not become the primary editing UI.");
                Assert(helpButton != null && ((TextBlock)helpButton.Content).Text == "帮助", "Toolbar Help action is not visible as a labeled control.");
                Assert(newCaptureButton != null && ((TextBlock)newCaptureButton.Content).Text == "重截",
                    "Toolbar does not provide a new-capture action.");
                var tryDoubleClick = GetPrivateMethod(typeof(CaptureWindow), "TryEditTextOnDoubleClick", typeof(UIElement));
                Assert(!(bool)tryDoubleClick.Invoke(window, new object[] { null }),
                    "Double-clicking an empty canvas must not trigger the copy-and-close action.");
                Assert(window.IsVisible && CaptureWindow.IsOpen,
                    "A non-text double-click unexpectedly closed the capture session.");
            }
            finally
            {
                window.Close();
            }
            Assert(!CaptureWindow.IsOpen, "Capture window should clear its open flag when closed.");
        }

        private static void TestStartupFeedback()
        {
            string defaultText = App.BuildStartupMessage("Alt+A");
            string fallbackText = App.BuildStartupMessage("Ctrl+Alt+A");
            string unavailableText = App.BuildStartupMessage(null);
            Assert(defaultText.Contains("已启动") && defaultText.Contains("Alt+A"),
                "Startup notification does not confirm background operation and the default shortcut.");
            Assert(fallbackText.Contains("Ctrl+Alt+A") && fallbackText.Contains("Alt+A 已被占用"),
                "Startup notification does not explain the active fallback shortcut.");
            Assert(unavailableText.Contains("点击桌面角落") && unavailableText.Contains("移动图标"),
                "Startup notification does not explain how to use the desktop indicator when no hotkey is registered.");
            Assert(App.BuildTrayText("Alt+A").Contains("Alt+A"), "Tray hover text does not show the active shortcut.");
            Assert(App.BuildTrayText("Alt+A").Contains("桌面图标"), "Tray hover text does not mention the desktop indicator.");
            Assert(App.BuildTrayText(null).Contains("点击桌面图标截图"), "Tray hover text lacks an alternative when no shortcut is registered.");
            Assert(App.BuildStartupMessage("Alt+A").Contains("点击桌面角落"), "Startup notification does not explain the desktop indicator.");
            Assert(App.CopyNotificationMessage.Contains("粘贴"), "Copy confirmation text is missing.");
        }

        private static void TestDesktopIndicatorLifecycle()
        {
            var app = Application.Current as App;
            if (app == null) app = new App();

            var indicator = new DesktopIndicatorWindow("Alt+A") { Opacity = 0 };
            try
            {
                indicator.Show();
                indicator.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
                Assert(indicator.Topmost && !indicator.ShowInTaskbar,
                    "Desktop indicator must remain topmost without occupying the taskbar.");
                Assert(Math.Abs(indicator.Width - 76) < 0.1 && Math.Abs(indicator.Height - 48) < 0.1,
                    "Desktop indicator is not compact.");
                Assert(indicator.ToolTip.ToString().Contains("Alt+A"), "Desktop indicator does not advertise the active shortcut.");
                Assert(indicator.FindName("CaptureButton") is Button && indicator.FindName("MoveThumb") is Thumb,
                    "Desktop indicator is missing its capture action or draggable move handle.");

                var indicatorField = typeof(App).GetField("_desktopIndicator", BindingFlags.Instance | BindingFlags.NonPublic);
                var oldIndicator = indicatorField.GetValue(app) as DesktopIndicatorWindow;
                indicatorField.SetValue(app, indicator);
                try
                {
                    var prepareCapture = typeof(App).GetMethod("PrepareDesktopIndicatorForCapture", BindingFlags.Instance | BindingFlags.NonPublic);
                    var captureButton = indicator.FindName("CaptureButton") as Button;
                    var pendingTooltip = new ToolTip { Content = "点击开始截图", IsOpen = true };
                    captureButton.ToolTip = pendingTooltip;
                    prepareCapture.Invoke(app, null);
                    Assert(!indicator.IsVisible, "Desktop indicator remained visible while capture started.");
                    Assert(!pendingTooltip.IsOpen, "An already-open indicator tooltip remained visible over the capture.");
                    Assert(indicator.ToolTip == null && captureButton.ToolTip == null &&
                           !ToolTipService.GetIsEnabled(indicator) && !ToolTipService.GetIsEnabled(captureButton),
                        "Indicator tooltips were not suppressed before capture.");
                    var capture = new CaptureWindow { Opacity = 0 };
                    try
                    {
                        Assert(!indicator.IsVisible, "Desktop indicator appeared during screen capture initialization.");
                        capture.Show();
                        capture.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
                        capture.Close();
                    }
                    finally
                    {
                        if (capture.IsVisible) capture.Close();
                    }
                    Assert(indicator.IsVisible, "Desktop indicator did not return after capture was closed.");
                    Assert(indicator.ToolTip.ToString().Contains("Alt+A") && captureButton.ToolTip.ToString().Contains("开始截图") &&
                           ToolTipService.GetIsEnabled(indicator) && ToolTipService.GetIsEnabled(captureButton),
                        "Indicator tooltips were not restored after the capture session.");

                    indicator.Hide();
                    prepareCapture.Invoke(app, null);
                    var hiddenCapture = new CaptureWindow { Opacity = 0 };
                    try
                    {
                        hiddenCapture.Show();
                        hiddenCapture.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
                        hiddenCapture.Close();
                    }
                    finally
                    {
                        if (hiddenCapture.IsVisible) hiddenCapture.Close();
                    }
                    Assert(!indicator.IsVisible, "A manually hidden desktop indicator should remain hidden after capture.");
                }
                finally
                {
                    indicatorField.SetValue(app, oldIndicator);
                }
            }
            finally
            {
                indicator.Close();
            }
        }

        private static void TestHotkeyFallbackPolicy()
        {
            var candidatesField = typeof(HotkeyService).GetField("Candidates", BindingFlags.Static | BindingFlags.NonPublic);
            var candidates = candidatesField.GetValue(null) as Array;
            Assert(candidates != null && candidates.Length == 3, "Expected exactly three modified hotkey candidates.");
            string[] expectedNames = { "Alt+A", "Ctrl+Alt+A", "Ctrl+Shift+A" };
            foreach (int index in new[] { 0, 1, 2 })
            {
                object candidate = candidates.GetValue(index);
                var candidateType = candidate.GetType();
                string name = (string)candidateType.GetField("Name").GetValue(candidate);
                uint modifiers = (uint)candidateType.GetField("Modifiers").GetValue(candidate);
                Assert(name == expectedNames[index], "Hotkey fallback order changed unexpectedly.");
                Assert(modifiers != 0, "Unmodified keys must not be automatically registered.");
            }
        }

        private static void TestNarrowToolbarViewport()
        {
            var window = new CaptureWindow();
            try
            {
                typeof(CaptureWindow).GetMethod("ConfigureToolbarViewport", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(window, new object[] { 320.0 });
                var toolbar = window.FindName("ToolBar") as Border;
                var toolbarScroll = window.FindName("ToolBarScroll") as ScrollViewer;
                var panel = window.FindName("PropertyPanel") as Border;
                var panelScroll = window.FindName("PropertyPanelScroll") as ScrollViewer;
                Assert(toolbar != null && toolbar.MaxWidth <= 304, "Toolbar width is not constrained for a narrow screen.");
                Assert(toolbarScroll != null && toolbarScroll.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto,
                    "Toolbar does not provide horizontal access to overflow controls.");
                Assert(panel != null && panel.MaxWidth <= 304 && panelScroll != null &&
                    panelScroll.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto,
                    "Text properties do not adapt to a narrow screen.");
            }
            finally
            {
                window.Close();
            }
        }

        private static void TestToolbarAndHelp()
        {
            var help = new HelpWindow("Ctrl+Alt+A");
            var hotkeyText = help.FindName("GlobalHotkeyText") as TextBlock;
            var drawingHelp = help.FindName("DrawingHelpText") as TextBlock;
            var editorShortcuts = help.FindName("EditorShortcutsText") as TextBlock;
            Assert(hotkeyText != null && hotkeyText.Text.Contains("Ctrl+Alt+A"), "Help does not show the registered global shortcut.");
            Assert(drawingHelp != null && drawingHelp.Text.Contains("托盘") && drawingHelp.Text.Contains("直线") &&
                drawingHelp.Text.Contains("Shift 水平"),
                "Help does not explain the tray entry point, drawing tools, and Shift horizontal constraint.");
            Assert(editorShortcuts != null && editorShortcuts.Text.Contains("Ctrl+S") && editorShortcuts.Text.Contains("Ctrl+Z"),
                "Help does not document save and undo shortcuts.");
            help.Close();

            var noHotkeyHelp = new HelpWindow(null);
            var noHotkeyText = noHotkeyHelp.FindName("GlobalHotkeyText") as TextBlock;
            Assert(noHotkeyText != null && noHotkeyText.Text.Contains("双击系统托盘") && !noHotkeyText.Text.Contains("主界面"),
                "No-hotkey help points to a nonexistent main window instead of the tray.");
            noHotkeyHelp.Close();
        }

        private static void TestShiftConstrainedLine()
        {
            var constrain = typeof(CaptureWindow).GetMethod("ConstrainLineEndpoint", BindingFlags.Static | BindingFlags.NonPublic);
            var start = new Point(18, 42);
            var pointer = new Point(95, 17);
            var horizontal = (Point)constrain.Invoke(null, new object[] { start, pointer, true });
            var freeAngle = (Point)constrain.Invoke(null, new object[] { start, pointer, false });
            Assert(Math.Abs(horizontal.X - 95) < 0.001 && Math.Abs(horizontal.Y - 42) < 0.001,
                "Shift should preserve the pointer X and lock the line to the start Y.");
            Assert(Math.Abs(freeAngle.X - 95) < 0.001 && Math.Abs(freeAngle.Y - 17) < 0.001,
                "Without Shift the line should retain its free angle.");
        }

        private static void TestNewCaptureToolbarAction()
        {
            var window = new CaptureWindow();
            try
            {
                window.Opacity = 0;
                window.Show();
                window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
                typeof(CaptureWindow).GetField("_selection", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(window, new Rect(10, 10, 160, 100));
                typeof(CaptureWindow).GetMethod("EnterEditing", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(window, null);
                var button = window.FindName("NewCaptureBtn") as Button;
                Assert(button != null, "New-capture toolbar action is missing.");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
                Assert(!window.IsVisible && !CaptureWindow.IsOpen, "New-capture action did not close the current editing overlay.");
            }
            finally
            {
                if (window.IsVisible) window.Close();
            }
        }

        private static void TestPinWindowConstruction()
        {
            var image = CreateBaseImage(40, 30);
            var pin = new PinWindow(image, 40, 30, 12, 16);
            try
            {
                pin.Opacity = 0;
                pin.Show();
                pin.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
                Assert(pin.Topmost, "Pinned image window is not always-on-top.");
                Assert(Math.Abs(pin.Width - 40) < 1 && Math.Abs(pin.Height - 30) < 1,
                    "Pinned image size differs by more than one DIP from its requested size.");
                var pinImage = pin.FindName("PinImage") as Image;
                Assert(pinImage != null && ReferenceEquals(pinImage.Source, image), "Pinned image content was not initialized.");
                var unpinButton = pin.FindName("UnpinButton") as Button;
                Assert(unpinButton != null && unpinButton.Visibility == Visibility.Collapsed,
                    "Pin window should reveal an explicit unpin action on hover.");
                typeof(PinWindow).GetMethod("Window_MouseEnter", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(pin, new object[] { pin, null });
                Assert(unpinButton.Visibility == Visibility.Visible, "Hovering over the pin did not reveal its release control.");
                unpinButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, unpinButton));
                Assert(!pin.IsVisible, "Explicit unpin button did not close the pinned image.");
            }
            finally
            {
                if (pin.IsVisible) pin.Close();
            }
        }

        private static void TestCaptureWindowComposedSavePipeline()
        {
            var window = new CaptureWindow();
            string pngPath = IOPath.Combine(AppContext.BaseDirectory, "capture-window-edited-layer.png");
            try
            {
                window.Opacity = 0;
                window.Show();
                window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

                var selectionField = typeof(CaptureWindow).GetField("_selection", BindingFlags.Instance | BindingFlags.NonPublic);
                selectionField.SetValue(window, new Rect(10, 10, 160, 110));
                typeof(CaptureWindow).GetMethod("EnterEditing", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);

                var canvas = window.FindName("ShapeCanvas") as Canvas;
                Assert(canvas != null, "Annotation canvas was not found in the real capture window.");
                var marker = new Rectangle
                {
                    Width = 30,
                    Height = 30,
                    Fill = new SolidColorBrush(Color.FromRgb(245, 15, 25)),
                    Stroke = Brushes.Transparent
                };
                Canvas.SetLeft(marker, 20);
                Canvas.SetTop(marker, 20);
                canvas.Children.Add(marker);

                var textInput = new TextBox
                {
                    Text = "SAVE",
                    FontFamily = new FontFamily("Arial"),
                    FontSize = 22,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(20, 240, 40)),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0)
                };
                Canvas.SetLeft(textInput, 60);
                Canvas.SetTop(textInput, 55);
                canvas.Children.Add(textInput);
                typeof(CaptureWindow).GetMethod("AttachEditingBox", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(window, new object[] { textInput });
                typeof(CaptureWindow).GetMethod("CommitEditingText", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(window, null);

                var existingText = new TextBlock
                {
                    Text = "EDIT",
                    FontFamily = new FontFamily("Arial"),
                    FontSize = 18,
                    Foreground = new SolidColorBrush(Color.FromRgb(35, 90, 240))
                };
                Canvas.SetLeft(existingText, 105);
                Canvas.SetTop(existingText, 62);
                canvas.Children.Add(existingText);
                typeof(CaptureWindow).GetMethod("BeginEditTextExisting", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(window, new object[] { existingText });
                var editingText = typeof(CaptureWindow).GetField("_editingText", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(window) as TextBox;
                Assert(editingText != null, "Existing text did not enter edit mode.");
                editingText.Text = "EDITED";
                typeof(CaptureWindow).GetMethod("CommitEditingText", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(window, null);

                var toolField = typeof(CaptureWindow).GetField("_tool", BindingFlags.Instance | BindingFlags.NonPublic);
                var stateField = typeof(CaptureWindow).GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic);
                SelectTool(window, "Line");
                Assert(toolField.GetValue(window).ToString() == "Line", "Line toolbar did not activate the straight-line tool.");
                typeof(CaptureWindow).GetMethod("StartDrawShape", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(window, new object[] { new Point(30, 80) });
                GetPrivateMethod(typeof(CaptureWindow), "UpdateDrawShape", typeof(Point), typeof(bool))
                    .Invoke(window, new object[] { new Point(70, 45), true });
                GetPrivateMethod(typeof(CaptureWindow), "CommitDrawShape", typeof(Point), typeof(bool))
                    .Invoke(window, new object[] { new Point(70, 45), true });
                var drawnLine = canvas.Children[canvas.Children.Count - 1] as Polyline;
                Assert(drawnLine != null, "Straight-line tool did not create a polyline.");
                double lineStartY = Canvas.GetTop(drawnLine) + drawnLine.Points[0].Y;
                double lineEndY = Canvas.GetTop(drawnLine) + drawnLine.Points[1].Y;
                Assert(Math.Abs(lineStartY - lineEndY) < 0.001, "Shift-held line was not horizontal after commit.");
                stateField.SetValue(window, Enum.Parse(stateField.FieldType, "Editing"));

                SelectTool(window, "Pen");
                Assert(toolField.GetValue(window).ToString() == "Pen", "Pen toolbar did not activate the freehand tool.");
                typeof(CaptureWindow).GetMethod("StartDrawShape", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(window, new object[] { new Point(30, 65) });
                GetPrivateMethod(typeof(CaptureWindow), "UpdateDrawShape", typeof(Point))
                    .Invoke(window, new object[] { new Point(40, 70) });
                GetPrivateMethod(typeof(CaptureWindow), "UpdateDrawShape", typeof(Point))
                    .Invoke(window, new object[] { new Point(50, 65) });
                GetPrivateMethod(typeof(CaptureWindow), "CommitDrawShape", typeof(Point))
                    .Invoke(window, new object[] { new Point(60, 68) });
                stateField.SetValue(window, Enum.Parse(stateField.FieldType, "Editing"));

                var composed = typeof(CaptureWindow).GetMethod("Compose", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(window, null) as BitmapSource;
                Assert(composed != null, "Capture window returned no composed image.");
                double scale = (double)typeof(CaptureWindow).GetField("_scale", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(window);
                Assert(composed.PixelWidth == (int)Math.Round(160 * scale), "Capture-window export width mismatch.");
                Assert(composed.PixelHeight == (int)Math.Round(110 * scale), "Capture-window export height mismatch.");

                int markerX = (int)Math.Round(25 * scale);
                int markerY = (int)Math.Round(25 * scale);
                AssertPixel(composed, markerX, markerY, 245, 15, 25, "actual capture-window annotation");
                int textX = (int)Math.Round(50 * scale);
                int textY = (int)Math.Round(43 * scale);
                Assert(CountColor(composed, textX, textY, (int)Math.Round(60 * scale), (int)Math.Round(32 * scale),
                    20, 240, 40) > 10, "Committed text was not included in the capture-window composition.");
                int editedTextX = (int)Math.Round(95 * scale);
                int editedTextY = (int)Math.Round(50 * scale);
                Assert(CountColor(composed, editedTextX, editedTextY, (int)Math.Round(65 * scale), (int)Math.Round(35 * scale),
                    35, 90, 240) > 10, "Re-edited text was not included in the capture-window composition.");
                int linePixelCount = CountColor(composed, (int)Math.Round(10 * scale), (int)Math.Round(60 * scale),
                    (int)Math.Round(75 * scale), (int)Math.Round(25 * scale), 35, 90, 240);
                Assert(linePixelCount > 5,
                    "Straight-line tool did not produce a visible saved layer (pixels=" + linePixelCount + ", scale=" + scale + ").");
                Assert(CountColor(composed, (int)Math.Round(18 * scale), (int)Math.Round(52 * scale),
                    (int)Math.Round(35 * scale), (int)Math.Round(22 * scale), 35, 90, 240) > 5,
                    "Freehand pen did not produce a visible saved layer.");
                ImageExporter.SaveToFile(composed, pngPath);
                var savedFrame = BitmapDecoder.Create(new Uri(pngPath), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                Assert(savedFrame.PixelWidth == composed.PixelWidth && savedFrame.PixelHeight == composed.PixelHeight,
                    "Saved capture-window image dimensions changed.");
                AssertPixel(savedFrame, markerX, markerY, 245, 15, 25, "saved actual capture-window annotation");
                Assert(CountColor(savedFrame, textX, textY, (int)Math.Round(60 * scale), (int)Math.Round(32 * scale),
                    20, 240, 40) > 10, "Committed text was missing from the saved capture-window image.");
                Assert(CountColor(savedFrame, editedTextX, editedTextY, (int)Math.Round(65 * scale), (int)Math.Round(35 * scale),
                    35, 90, 240) > 10, "Re-edited text was missing from the saved capture-window image.");
                Assert(CountColor(savedFrame, (int)Math.Round(20 * scale), (int)Math.Round(68 * scale),
                    (int)Math.Round(45 * scale), (int)Math.Round(6 * scale), 35, 90, 240) > 5,
                    "Straight line was missing from the saved image.");
                Assert(CountColor(savedFrame, (int)Math.Round(18 * scale), (int)Math.Round(52 * scale),
                    (int)Math.Round(35 * scale), (int)Math.Round(22 * scale), 35, 90, 240) > 5,
                    "Freehand pen was missing from the saved image.");
            }
            finally
            {
                window.Close();
            }
        }

        private static System.Reflection.MethodInfo GetPrivateMethod(Type type, string name, params Type[] parameterTypes)
        {
            return type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic, null, parameterTypes, null);
        }

        private static void SelectTool(CaptureWindow window, string tag)
        {
            var toolbar = window.FindName("ToolBar") as Border;
            var scroll = toolbar?.Child as ScrollViewer;
            var stack = scroll?.Content as StackPanel;
            Assert(stack != null, "Editor toolbar was not initialized.");

            RadioButton button = null;
            foreach (UIElement child in stack.Children)
            {
                var candidate = child as RadioButton;
                if (candidate != null && string.Equals(candidate.Tag as string, tag, StringComparison.Ordinal))
                {
                    button = candidate;
                    break;
                }
            }
            Assert(button != null, "Toolbar does not contain a tool button tagged " + tag + ".");
            typeof(CaptureWindow).GetMethod("ToolBtn_Click", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(window, new object[] { button, new RoutedEventArgs() });
        }

        private static void TestBaseCrop()
        {
            const int scale = 2;
            var baseImage = CreateBaseImage(320 * scale, 200 * scale);
            var canvas = CreateCanvas(320, 200);
            var composed = ImageExporter.Compose(baseImage, canvas, scale, new Rect(40, 30, 180, 120));

            Assert(composed.PixelWidth == 360, "Expected crop width of 360 physical pixels.");
            Assert(composed.PixelHeight == 240, "Expected crop height of 240 physical pixels.");
            AssertPixel(composed, 10, 10, 20, 40, 60, "cropped base pixel");
        }

        private static void TestAnnotationLayers()
        {
            const int scale = 1;
            var baseImage = CreateBaseImage(320, 200);
            var canvas = CreateCanvas(320, 200);

            var rectangle = new Rectangle
            {
                Width = 50,
                Height = 30,
                Fill = new SolidColorBrush(Color.FromRgb(240, 20, 30)),
                Stroke = Brushes.Transparent
            };
            Canvas.SetLeft(rectangle, 55);
            Canvas.SetTop(rectangle, 45);
            canvas.Children.Add(rectangle);

            var text = new TextBlock
            {
                Text = "TEST",
                FontFamily = new FontFamily("Arial"),
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(20, 240, 40))
            };
            Canvas.SetLeft(text, 120);
            Canvas.SetTop(text, 70);
            canvas.Children.Add(text);

            var ellipse = new Ellipse
            {
                Width = 24,
                Height = 18,
                Fill = new SolidColorBrush(Color.FromRgb(30, 70, 240)),
                Stroke = Brushes.Transparent
            };
            Canvas.SetLeft(ellipse, 200);
            Canvas.SetTop(ellipse, 45);
            canvas.Children.Add(ellipse);

            var arrow = new ArrowShape
            {
                X1 = 0,
                Y1 = 0,
                X2 = 50,
                Y2 = 25,
                Width = 56,
                Height = 31,
                Stroke = new SolidColorBrush(Color.FromRgb(250, 200, 20)),
                StrokeThickness = 4
            };
            Canvas.SetLeft(arrow, 60);
            Canvas.SetTop(arrow, 120);
            canvas.Children.Add(arrow);

            var pen = new Polyline
            {
                Points = new PointCollection { new Point(0, 0), new Point(18, 10), new Point(30, 0) },
                Stroke = new SolidColorBrush(Color.FromRgb(230, 30, 220)),
                StrokeThickness = 5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            Canvas.SetLeft(pen, 150);
            Canvas.SetTop(pen, 125);
            canvas.Children.Add(pen);

            var highlighter = new Polyline
            {
                Points = new PointCollection { new Point(0, 0), new Point(20, 0), new Point(40, 5) },
                Stroke = new SolidColorBrush(Color.FromRgb(255, 230, 0)),
                StrokeThickness = 10,
                Opacity = 0.45,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            Canvas.SetLeft(highlighter, 165);
            Canvas.SetTop(highlighter, 160);
            canvas.Children.Add(highlighter);

            Layout(canvas);
            var composed = ImageExporter.Compose(baseImage, canvas, scale, new Rect(40, 30, 220, 150));

            AssertPixel(composed, 25, 25, 240, 20, 30, "rectangle fill");
            AssertPixel(composed, 172, 24, 30, 70, 240, "ellipse fill");
            Assert(CountColor(composed, 18, 88, 62, 38, 250, 200, 20) > 10, "Arrow pixels missing.");
            Assert(CountColor(composed, 108, 92, 42, 28, 230, 30, 220) > 5, "Pen pixels missing.");
            Assert(CountColor(composed, 78, 38, 90, 48, 20, 240, 40) > 15, "Text pixels missing.");
            Assert(CountChangedPixels(composed, 122, 125, 48, 18) > 15, "Highlighter pixels missing.");
        }

        private static void TestScaledAnnotationAt125()
        {
            const double scale = 1.25;
            var baseImage = CreateBaseImage(400, 250);
            var canvas = CreateCanvas(320, 200);
            var marker = new Rectangle
            {
                Width = 50,
                Height = 30,
                Fill = new SolidColorBrush(Color.FromRgb(240, 20, 30)),
                Stroke = Brushes.Transparent
            };
            Canvas.SetLeft(marker, 55);
            Canvas.SetTop(marker, 45);
            canvas.Children.Add(marker);
            Layout(canvas);

            var composed = ImageExporter.Compose(baseImage, canvas, scale, new Rect(40, 30, 180, 120));
            Assert(composed.PixelWidth == 225 && composed.PixelHeight == 150, "125% crop dimensions incorrect.");
            AssertPixel(composed, 45, 35, 240, 20, 30, "125% annotation pixels");
        }

        private static void TestScaledAnnotation()
        {
            const double scale = 1.5;
            var baseImage = CreateBaseImage(480, 300);
            var canvas = CreateCanvas(320, 200);
            var marker = new Rectangle
            {
                Width = 50,
                Height = 30,
                Fill = new SolidColorBrush(Color.FromRgb(240, 20, 30)),
                Stroke = Brushes.Transparent
            };
            Canvas.SetLeft(marker, 55);
            Canvas.SetTop(marker, 45);
            canvas.Children.Add(marker);
            Layout(canvas);

            var composed = ImageExporter.Compose(baseImage, canvas, scale, new Rect(40, 30, 180, 120));
            Assert(composed.PixelWidth == 270 && composed.PixelHeight == 180, "150% crop dimensions incorrect.");
            AssertPixel(composed, 45, 35, 240, 20, 30, "scaled annotation pixels");
        }

        private static void TestSelectionClip()
        {
            var baseImage = CreateBaseImage(320, 200);
            var canvas = CreateCanvas(320, 200);
            var rectangle = new Rectangle
            {
                Width = 60,
                Height = 40,
                Fill = new SolidColorBrush(Color.FromRgb(250, 10, 10)),
                Stroke = Brushes.Transparent
            };
            Canvas.SetLeft(rectangle, 30);
            Canvas.SetTop(rectangle, 25);
            canvas.Children.Add(rectangle);
            Layout(canvas);

            var composed = ImageExporter.Compose(baseImage, canvas, 1, new Rect(50, 40, 100, 80));
            AssertPixel(composed, 5, 5, 250, 10, 10, "annotation inside crop");
            AssertPixel(composed, 45, 30, 20, 40, 60, "base image outside annotation in crop");
            Assert(composed.PixelWidth == 100 && composed.PixelHeight == 80, "Crop dimensions incorrect.");
        }

        private static void TestUndoRedoExport()
        {
            var baseImage = CreateBaseImage(80, 60);
            var canvas = CreateCanvas(80, 60);
            var marker = new Rectangle
            {
                Width = 20,
                Height = 20,
                Fill = new SolidColorBrush(Color.FromRgb(220, 40, 30)),
                Stroke = Brushes.Transparent
            };
            Canvas.SetLeft(marker, 10);
            Canvas.SetTop(marker, 10);
            canvas.Children.Add(marker);
            var add = new ShapeAddCommand(canvas, marker);
            Layout(canvas);

            var withLayer = ImageExporter.Compose(baseImage, canvas, 1, new Rect(0, 0, 80, 60));
            AssertPixel(withLayer, 15, 15, 220, 40, 30, "annotation before undo");

            add.Undo();
            Layout(canvas);
            var undone = ImageExporter.Compose(baseImage, canvas, 1, new Rect(0, 0, 80, 60));
            AssertPixel(undone, 15, 15, 20, 40, 60, "undo removes annotation from export");

            add.Redo();
            Layout(canvas);
            var redone = ImageExporter.Compose(baseImage, canvas, 1, new Rect(0, 0, 80, 60));
            AssertPixel(redone, 15, 15, 220, 40, 30, "redo restores annotation in export");
        }

        private static void TestFileExports()
        {
            var baseImage = CreateBaseImage(80, 60);
            var canvas = CreateCanvas(80, 60);
            var marker = new Rectangle
            {
                Width = 20,
                Height = 20,
                Fill = new SolidColorBrush(Color.FromRgb(220, 40, 30)),
                Stroke = Brushes.Transparent
            };
            Canvas.SetLeft(marker, 10);
            Canvas.SetTop(marker, 10);
            canvas.Children.Add(marker);
            Layout(canvas);
            var composed = ImageExporter.Compose(baseImage, canvas, 1, new Rect(0, 0, 80, 60));

            string pngPath = IOPath.Combine(AppContext.BaseDirectory, "acceptance-edited.png");
            string jpgPath = IOPath.Combine(AppContext.BaseDirectory, "acceptance-edited.jpg");
            ImageExporter.SaveToFile(composed, pngPath);
            ImageExporter.SaveToFile(composed, jpgPath);

            Assert(new FileInfo(pngPath).Length > 100, "PNG file is empty or too small.");
            Assert(new FileInfo(jpgPath).Length > 100, "JPEG file is empty or too small.");
            AssertEncodedImage(pngPath, 80, 60, true);
            AssertEncodedImage(jpgPath, 80, 60, false);

            var clipboardData = ImageExporter.CreateClipboardDataObject(composed);
            Assert(clipboardData.GetDataPresent(DataFormats.Bitmap), "Clipboard bitmap format is missing.");
            Assert(clipboardData.GetDataPresent("PNG"), "Clipboard PNG format is missing.");
            var pngStream = clipboardData.GetData("PNG") as Stream;
            Assert(pngStream != null && pngStream.Length > 0, "Clipboard PNG payload is empty.");
            pngStream.Position = 0;
            var clipboardFrame = BitmapDecoder.Create(pngStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            Assert(clipboardFrame.PixelWidth == 80 && clipboardFrame.PixelHeight == 60, "Clipboard image dimensions mismatch.");
            AssertPixel(clipboardFrame, 15, 15, 220, 40, 30, "clipboard PNG contains edited layer");
        }

        private static void TestMosaic()
        {
            using (var source = new GdiBitmap(32, 32))
            {
                for (int y = 0; y < source.Height; y++)
                for (int x = 0; x < source.Width; x++)
                    source.SetPixel(x, y, ((x + y) % 2 == 0) ? GdiColor.White : GdiColor.Black);

                var mosaic = MosaicService.CreateMosaic(source, new Int32Rect(4, 4, 24, 24), 6);
                Assert(mosaic != null, "Mosaic renderer returned null.");
                Assert(mosaic.PixelWidth == 24 && mosaic.PixelHeight == 24, "Mosaic dimensions incorrect.");

                var pixels = new byte[mosaic.PixelWidth * mosaic.PixelHeight * 4];
                mosaic.CopyPixels(pixels, mosaic.PixelWidth * 4, 0);
                int min = 255, max = 0;
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    min = Math.Min(min, pixels[i]);
                    max = Math.Max(max, pixels[i]);
                }
                Assert(max - min < 255, "Mosaic retained full checkerboard contrast.");

                var layer = new Image { Source = mosaic, Width = 24, Height = 24 };
                Canvas.SetLeft(layer, 20);
                Canvas.SetTop(layer, 15);
                var canvas = CreateCanvas(80, 60);
                canvas.Children.Add(layer);
                Layout(canvas);
                var output = ImageExporter.Compose(CreateBaseImage(80, 60), canvas, 1, new Rect(0, 0, 80, 60));
                var rendered = GetRgb(output, 32, 27);
                Assert(Math.Abs(rendered.R - 20) > 20 || Math.Abs(rendered.G - 40) > 20 || Math.Abs(rendered.B - 60) > 20,
                    "Mosaic image layer is missing from the composed screenshot.");
            }
        }

        private static Canvas CreateCanvas(double width, double height)
        {
            return new Canvas { Width = width, Height = height, Background = Brushes.Transparent };
        }

        private static void Layout(Canvas canvas)
        {
            canvas.Measure(new Size(canvas.Width, canvas.Height));
            canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height));
            canvas.UpdateLayout();
        }

        private static BitmapSource CreateBaseImage(int width, int height)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(20, 40, 60)), null, new Rect(0, 0, width, height));

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        private static void AssertEncodedImage(string path, int width, int height, bool lossless)
        {
            BitmapDecoder decoder = BitmapDecoder.Create(
                new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            Assert(frame.PixelWidth == width && frame.PixelHeight == height, "Encoded dimensions mismatch: " + path);
            if (lossless)
                AssertPixel(frame, 15, 15, 220, 40, 30, "PNG preserves annotation color");
            else
            {
                var c = GetRgb(frame, 15, 15);
                Assert(Math.Abs(c.R - 220) < 20 && Math.Abs(c.G - 40) < 20 && Math.Abs(c.B - 30) < 20,
                    "JPEG annotation color is unexpectedly absent.");
            }
        }

        private static int CountChangedPixels(BitmapSource bitmap, int x, int y, int width, int height)
        {
            int count = 0;
            int right = Math.Min(bitmap.PixelWidth, x + width);
            int bottom = Math.Min(bitmap.PixelHeight, y + height);
            for (int py = Math.Max(0, y); py < bottom; py++)
            for (int px = Math.Max(0, x); px < right; px++)
            {
                var c = GetRgb(bitmap, px, py);
                if (Math.Abs(c.R - 20) > 3 || Math.Abs(c.G - 40) > 3 || Math.Abs(c.B - 60) > 3) count++;
            }
            return count;
        }

        private static int CountColor(BitmapSource bitmap, int x, int y, int width, int height, byte r, byte g, byte b)
        {
            int count = 0;
            int right = Math.Min(bitmap.PixelWidth, x + width);
            int bottom = Math.Min(bitmap.PixelHeight, y + height);
            for (int py = Math.Max(0, y); py < bottom; py++)
            for (int px = Math.Max(0, x); px < right; px++)
            {
                var c = GetRgb(bitmap, px, py);
                if (Math.Abs(c.R - r) < 3 && Math.Abs(c.G - g) < 3 && Math.Abs(c.B - b) < 3) count++;
            }
            return count;
        }

        private static void AssertPixel(BitmapSource bitmap, int x, int y, byte r, byte g, byte b, string label)
        {
            var c = GetRgb(bitmap, x, y);
            Assert(Math.Abs(c.R - r) < 2 && Math.Abs(c.G - g) < 2 && Math.Abs(c.B - b) < 2,
                label + " expected RGB(" + r + "," + g + "," + b + ") but got RGB(" + c.R + "," + c.G + "," + c.B + ").");
        }

        private static (byte R, byte G, byte B) GetRgb(BitmapSource bitmap, int x, int y)
        {
            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var pixel = new byte[4];
            converted.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
            return (pixel[2], pixel[1], pixel[0]);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
