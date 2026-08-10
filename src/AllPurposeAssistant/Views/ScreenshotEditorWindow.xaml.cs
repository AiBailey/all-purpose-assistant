using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace AllPurposeAssistant.Views;

public partial class ScreenshotEditorWindow : Window
{
    private enum Tool { Pen, Rect, Arrow, Text, Mosaic }

    private sealed class EditorAction
    {
        public EditorAction(Action undo, Action redo)
        {
            Undo = undo;
            Redo = redo;
        }

        public Action Undo { get; }
        public Action Redo { get; }
    }

    private sealed class TextAnnotation
    {
        public TextAnnotation(Grid container, TextBox editor, TextBlock display)
        {
            Container = container;
            Editor = editor;
            Display = display;
        }

        public Grid Container { get; }
        public TextBox Editor { get; }
        public TextBlock Display { get; }
        public bool IsEditing { get; set; } = true;
        public bool IsNew { get; set; } = true;
    }

    private sealed class ArrowAnnotation
    {
        public ArrowAnnotation(System.Windows.Shapes.Path shape, Point start, Point end)
        {
            Shape = shape;
            Start = start;
            End = end;
        }

        public System.Windows.Shapes.Path Shape { get; }
        public Point Start { get; set; }
        public Point End { get; set; }
    }

    private enum ArrowDragMode { Move, Start, End }
    private enum RectangleDragMode { Move, TopLeft, TopRight, BottomLeft, BottomRight }

    private readonly BitmapSource _shot;
    private readonly string? _defaultSaveDirectory;
    private readonly string _defaultSaveFormat;
    private readonly int _jpegQuality;
    private readonly double _pinnedOpacity;
    private Tool _tool = Tool.Pen;
    private bool _drawing;
    private Point _origin;
    private UIElement? _currentShape;
    private Polyline? _penLine;
    private Point _lastPen;
    private Color _annotationColor = Color.FromRgb(0xE7, 0x4C, 0x3C);
    private Rectangle? _movingRectangle;
    private Point _moveStart;
    private Point _rectangleStart;
    private readonly List<Thumb> _rectangleResizeHandles = new();
    private Rectangle? _handleResizingRectangle;
    private Rect _handleOriginalBounds;
    private readonly Dictionary<Grid, TextAnnotation> _textAnnotations = new();
    private readonly Dictionary<System.Windows.Shapes.Path, ArrowAnnotation> _arrowAnnotations = new();
    private readonly Rectangle _selectionOutline;
    private UIElement? _selectedAnnotation;
    private ArrowAnnotation? _movingArrow;
    private ArrowDragMode _arrowDragMode;
    private Point _arrowDragStart;
    private Point _arrowStart;
    private Point _arrowEnd;
    private readonly List<EditorAction> _editorHistory = new();
    private readonly Dictionary<UIElement, EditorAction> _additionActions = new();
    private int _historyIndex = -1;

    public ScreenshotEditorWindow(BitmapSource shot, string? defaultSaveDirectory = null,
        string defaultSaveFormat = "Png", int jpegQuality = 92, double pinnedOpacity = 1)
    {
        _shot = shot;
        _defaultSaveDirectory = defaultSaveDirectory;
        _defaultSaveFormat = defaultSaveFormat is "Jpeg" ? "Jpeg" : "Png";
        _jpegQuality = Math.Clamp(jpegQuality, 1, 100);
        _pinnedOpacity = Math.Clamp(pinnedOpacity, 0.2, 1);
        InitializeComponent();
        ShotImage.Source = shot;
        _selectionOutline = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xD9)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 2 },
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        Canvas.SetZIndex(_selectionOutline, int.MaxValue - 1);
        AnnotateCanvas.Children.Add(_selectionOutline);
        CreateRectangleResizeHandle(RectangleDragMode.TopLeft, Cursors.SizeNWSE);
        CreateRectangleResizeHandle(RectangleDragMode.TopRight, Cursors.SizeNESW);
        CreateRectangleResizeHandle(RectangleDragMode.BottomLeft, Cursors.SizeNESW);
        CreateRectangleResizeHandle(RectangleDragMode.BottomRight, Cursors.SizeNWSE);
        Loaded += (_, _) => FitImage();
    }

    // 等比缩放截图到可显示区域，保证所见即所得且不变形
    private void FitImage()
    {
        double availW = ActualWidth - 32;
        double availH = ActualHeight - 84 - 40;
        if (availW <= 50 || availH <= 50) return;
        double sc = System.Math.Min(availW / _shot.PixelWidth, availH / _shot.PixelHeight);
        if (sc <= 0.01 || sc > 20) sc = 1.0;
        CanvasHost.Width = _shot.PixelWidth;
        CanvasHost.Height = _shot.PixelHeight;
        CanvasHost.LayoutTransform = new ScaleTransform(sc, sc);
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (IsLoaded)
            FitImage();
    }

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton btn) return;
        // 单选互斥
        foreach (var t in new[] { BtnPen, BtnRect, BtnArrow, BtnText, BtnMosaic })
            if (t != btn) t.IsChecked = false;
        btn.IsChecked = true;

        _tool = (btn.Tag as string) switch
        {
            "rect" => Tool.Rect,
            "arrow" => Tool.Arrow,
            "text" => Tool.Text,
            "mosaic" => Tool.Mosaic,
            _ => Tool.Pen
        };
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not string colorText)
            return;

        foreach (var colorButton in new[] { ColorRed, ColorBlue, ColorOrange, ColorGreen, ColorBlack })
            if (colorButton != button) colorButton.IsChecked = false;
        button.IsChecked = true;
        _annotationColor = (Color)ColorConverter.ConvertFromString(colorText);
    }

    private void AnnotateCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var clickedAnnotation = FindAnnotation(e.OriginalSource as DependencyObject);
        if (clickedAnnotation != null)
        {
            SelectAnnotation(clickedAnnotation);
            if (clickedAnnotation is Rectangle rectangle)
            {
                if (_tool != Tool.Rect)
                    return;
                _movingRectangle = rectangle;
                _moveStart = e.GetPosition(AnnotateCanvas);
                _rectangleStart = new Point(GetCanvasLeft(rectangle), GetCanvasTop(rectangle));
                AnnotateCanvas.CaptureMouse();
                e.Handled = true;
                return;
            }

            if (_tool != Tool.Arrow || clickedAnnotation is not System.Windows.Shapes.Path)
                return;
        }

        if ((clickedAnnotation == null || clickedAnnotation is System.Windows.Shapes.Path)
            && TryFindArrow(e.GetPosition(AnnotateCanvas), out var arrowAnnotation, out var arrowDragMode))
        {
            SelectAnnotation(arrowAnnotation.Shape);
            if (_tool != Tool.Arrow)
                return;

            var position = e.GetPosition(AnnotateCanvas);
            _movingArrow = arrowAnnotation;
            _arrowDragStart = position;
            _arrowStart = arrowAnnotation.Start;
            _arrowEnd = arrowAnnotation.End;
            _arrowDragMode = arrowDragMode;
            AnnotateCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        // 只有点击画布空白处才创建标注，已有标注及其控件保留自己的交互。
        if (e.OriginalSource != AnnotateCanvas)
            return;

        var pos = e.GetPosition(AnnotateCanvas);
        _drawing = true;
        _origin = pos;
        _currentShape = null;
        if (_tool != Tool.Text)
            AnnotateCanvas.CaptureMouse();

        switch (_tool)
        {
            case Tool.Pen:
                _penLine = new Polyline
                {
                    Stroke = CreateAnnotationBrush(),
                    StrokeThickness = 3,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
                _penLine.Points.Add(pos);
                AddAnnotation(_penLine);
                _lastPen = pos;
                break;
            case Tool.Rect:
                var r = new Rectangle
                {
                    Stroke = CreateAnnotationBrush(),
                    StrokeThickness = 2,
                    Fill = new SolidColorBrush(Color.FromArgb(30, _annotationColor.R, _annotationColor.G, _annotationColor.B)),
                    Cursor = Cursors.SizeAll
                };
                _currentShape = r;
                AddAnnotation(r);
                break;
            case Tool.Arrow:
                var arrow = new System.Windows.Shapes.Path
                {
                    Stroke = CreateAnnotationBrush(),
                    StrokeThickness = 3,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round
                };
                _currentShape = arrow;
                AddAnnotation(arrow);
                _arrowAnnotations.Add(arrow, new ArrowAnnotation(arrow, pos, pos));
                break;
            case Tool.Text:
                AddTextAt(pos);
                _drawing = false;
                break;
            case Tool.Mosaic:
                var mosaic = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(100, 44, 62, 80)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(74, 144, 217)),
                    BorderThickness = new Thickness(1),
                    IsHitTestVisible = false
                };
                _currentShape = mosaic;
                AddAnnotation(mosaic);
                break;
        }
    }

    private void AnnotateCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_movingArrow != null)
        {
            var current = e.GetPosition(AnnotateCanvas);
            var delta = current - _arrowDragStart;
            switch (_arrowDragMode)
            {
                case ArrowDragMode.Start:
                    _movingArrow.Start = current;
                    break;
                case ArrowDragMode.End:
                    _movingArrow.End = current;
                    break;
                default:
                    _movingArrow.Start = _arrowStart + delta;
                    _movingArrow.End = _arrowEnd + delta;
                    break;
            }
            _movingArrow.Shape.Data = CreateArrowGeometry(_movingArrow.Start, _movingArrow.End);
            UpdateSelectionOutline();
            return;
        }

        if (_movingRectangle != null)
        {
            var current = e.GetPosition(AnnotateCanvas);
            Canvas.SetLeft(_movingRectangle, _rectangleStart.X + current.X - _moveStart.X);
            Canvas.SetTop(_movingRectangle, _rectangleStart.Y + current.Y - _moveStart.Y);
            UpdateSelectionOutline();
            return;
        }

        if (!_drawing)
        {
            var position = e.GetPosition(AnnotateCanvas);
            if (_tool == Tool.Arrow && TryFindArrow(position, out _, out var dragMode))
                AnnotateCanvas.Cursor = dragMode == ArrowDragMode.Move ? Cursors.SizeAll : Cursors.Cross;
            else
                AnnotateCanvas.Cursor = _tool == Tool.Arrow ? Cursors.Cross : null;
            return;
        }
        var pos = e.GetPosition(AnnotateCanvas);

        switch (_tool)
        {
            case Tool.Pen:
                _penLine?.Points.Add(pos);
                break;
            case Tool.Rect:
                if (_currentShape is Rectangle rr)
                {
                    double x = System.Math.Min(_origin.X, pos.X);
                    double y = System.Math.Min(_origin.Y, pos.Y);
                    rr.Width = System.Math.Abs(pos.X - _origin.X);
                    rr.Height = System.Math.Abs(pos.Y - _origin.Y);
                    Canvas.SetLeft(rr, x);
                    Canvas.SetTop(rr, y);
                }
                break;
            case Tool.Arrow:
                if (_currentShape is System.Windows.Shapes.Path arrow)
                {
                    arrow.Data = CreateArrowGeometry(_origin, pos);
                    if (_arrowAnnotations.TryGetValue(arrow, out var arrowAnnotation))
                        arrowAnnotation.End = pos;
                }
                break;
            case Tool.Mosaic:
                if (_currentShape is Border mosaic)
                    SetCanvasBounds(mosaic, CreateBounds(_origin, pos));
                break;
        }
    }

    private void AnnotateCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_movingArrow != null)
        {
            var arrow = _movingArrow;
            var originalStart = _arrowStart;
            var originalEnd = _arrowEnd;
            var updatedStart = arrow.Start;
            var updatedEnd = arrow.End;
            if (updatedStart != originalStart || updatedEnd != originalEnd)
            {
                ExecuteHistoryAction(new EditorAction(
                    () => SetArrowPoints(arrow, originalStart, originalEnd),
                    () => SetArrowPoints(arrow, updatedStart, updatedEnd)));
            }
            _movingArrow = null;
            AnnotateCanvas.ReleaseMouseCapture();
            UpdateSelectionOutline();
            return;
        }

        if (_movingRectangle != null)
        {
            var rectangle = _movingRectangle;
            var originalPosition = _rectangleStart;
            var updatedPosition = new Point(GetCanvasLeft(rectangle), GetCanvasTop(rectangle));
            if (updatedPosition != originalPosition)
            {
                ExecuteHistoryAction(new EditorAction(
                    () => SetCanvasPosition(rectangle, originalPosition),
                    () => SetCanvasPosition(rectangle, updatedPosition)));
            }
            _movingRectangle = null;
            AnnotateCanvas.ReleaseMouseCapture();
            UpdateSelectionOutline();
            return;
        }

        if (!_drawing)
        {
            if (Mouse.Captured == AnnotateCanvas)
                AnnotateCanvas.ReleaseMouseCapture();
            return;
        }
        _drawing = false;
        AnnotateCanvas.ReleaseMouseCapture();

        if (_currentShape is Border mosaic)
            FinalizeMosaic(mosaic);

        _currentShape = null;
        _penLine = null;
    }

    private static Rect CreateBounds(Point first, Point second) => new(
        Math.Min(first.X, second.X), Math.Min(first.Y, second.Y),
        Math.Abs(first.X - second.X), Math.Abs(first.Y - second.Y));

    private static Rect GetRectangleBounds(Rectangle rectangle) => new(
        GetCanvasLeft(rectangle), GetCanvasTop(rectangle), rectangle.Width, rectangle.Height);

    private static void SetCanvasBounds(UIElement element, Rect bounds)
    {
        Canvas.SetLeft(element, bounds.Left);
        Canvas.SetTop(element, bounds.Top);
        if (element is FrameworkElement frameworkElement)
        {
            frameworkElement.Width = bounds.Width;
            frameworkElement.Height = bounds.Height;
        }
    }

    private void CreateRectangleResizeHandle(RectangleDragMode dragMode, Cursor cursor)
    {
        var handle = new Thumb
        {
            Width = 16,
            Height = 16,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(74, 144, 217)),
            BorderThickness = new Thickness(2),
            Cursor = cursor,
            Tag = dragMode,
            Visibility = Visibility.Collapsed
        };
        Canvas.SetZIndex(handle, int.MaxValue);
        handle.DragStarted += (_, _) =>
        {
            if (_selectedAnnotation is not Rectangle rectangle || handle.Tag is not RectangleDragMode mode)
                return;
            _handleResizingRectangle = rectangle;
            _handleOriginalBounds = GetRectangleBounds(rectangle);
            handle.Tag = mode;
        };
        handle.DragDelta += (_, _) =>
        {
            if (_handleResizingRectangle == null || handle.Tag is not RectangleDragMode mode)
                return;
            ResizeRectangle(_handleResizingRectangle, _handleOriginalBounds, mode,
                Mouse.GetPosition(AnnotateCanvas));
            UpdateSelectionOutline();
        };
        handle.DragCompleted += (_, _) =>
        {
            if (_handleResizingRectangle == null) return;
            var rectangle = _handleResizingRectangle;
            var originalBounds = _handleOriginalBounds;
            var updatedBounds = GetRectangleBounds(rectangle);
            if (updatedBounds != originalBounds)
            {
                ExecuteHistoryAction(new EditorAction(
                    () => SetCanvasBounds(rectangle, originalBounds),
                    () => SetCanvasBounds(rectangle, updatedBounds)));
            }
            _handleResizingRectangle = null;
            UpdateSelectionOutline();
        };
        _rectangleResizeHandles.Add(handle);
        AnnotateCanvas.Children.Add(handle);
    }

    private void HideRectangleResizeHandles()
    {
        foreach (var handle in _rectangleResizeHandles)
            handle.Visibility = Visibility.Collapsed;
    }

    private void ShowRectangleResizeHandles(Rect bounds)
    {
        var corners = new[] { bounds.TopLeft, bounds.TopRight, bounds.BottomLeft, bounds.BottomRight };
        for (var index = 0; index < _rectangleResizeHandles.Count; index++)
        {
            var handle = _rectangleResizeHandles[index];
            Canvas.SetLeft(handle, corners[index].X - handle.Width / 2);
            Canvas.SetTop(handle, corners[index].Y - handle.Height / 2);
            handle.Visibility = Visibility.Visible;
        }
    }

    private static void ResizeRectangle(Rectangle rectangle, Rect originalBounds,
        RectangleDragMode dragMode, Point position)
    {
        const double minimumSize = 8;
        var left = originalBounds.Left;
        var top = originalBounds.Top;
        var right = originalBounds.Right;
        var bottom = originalBounds.Bottom;

        switch (dragMode)
        {
            case RectangleDragMode.TopLeft:
                left = Math.Min(position.X, right - minimumSize);
                top = Math.Min(position.Y, bottom - minimumSize);
                break;
            case RectangleDragMode.TopRight:
                right = Math.Max(position.X, left + minimumSize);
                top = Math.Min(position.Y, bottom - minimumSize);
                break;
            case RectangleDragMode.BottomLeft:
                left = Math.Min(position.X, right - minimumSize);
                bottom = Math.Max(position.Y, top + minimumSize);
                break;
            case RectangleDragMode.BottomRight:
                right = Math.Max(position.X, left + minimumSize);
                bottom = Math.Max(position.Y, top + minimumSize);
                break;
        }

        SetCanvasBounds(rectangle, new Rect(left, top, right - left, bottom - top));
    }

    private void FinalizeMosaic(Border mosaic)
    {
        var bounds = new Rect(GetCanvasLeft(mosaic), GetCanvasTop(mosaic), mosaic.Width, mosaic.Height);
        if (bounds.Width < 4 || bounds.Height < 4)
        {
            DiscardAnnotation(mosaic);
            SelectAnnotation(null);
            return;
        }

        var image = new Image
        {
            Source = CreateMosaicBitmap(bounds),
            Stretch = Stretch.Fill
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        mosaic.Background = null;
        mosaic.BorderThickness = new Thickness(0);
        mosaic.Child = image;
    }

    private BitmapSource CreateMosaicBitmap(Rect bounds)
    {
        var left = Math.Clamp((int)Math.Floor(bounds.Left), 0, _shot.PixelWidth - 1);
        var top = Math.Clamp((int)Math.Floor(bounds.Top), 0, _shot.PixelHeight - 1);
        var right = Math.Clamp((int)Math.Ceiling(bounds.Right), left + 1, _shot.PixelWidth);
        var bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom), top + 1, _shot.PixelHeight);
        var width = right - left;
        var height = bottom - top;
        var crop = new CroppedBitmap(_shot, new Int32Rect(left, top, width, height));
        var converted = new FormatConvertedBitmap(crop, PixelFormats.Bgra32, null, 0);
        var stride = width * 4;
        var pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);

        const int blockSize = 12;
        for (var blockY = 0; blockY < height; blockY += blockSize)
        {
            for (var blockX = 0; blockX < width; blockX += blockSize)
            {
                var sourceIndex = blockY * stride + blockX * 4;
                var blue = pixels[sourceIndex];
                var green = pixels[sourceIndex + 1];
                var red = pixels[sourceIndex + 2];
                var alpha = pixels[sourceIndex + 3];
                var maxY = Math.Min(blockY + blockSize, height);
                var maxX = Math.Min(blockX + blockSize, width);
                for (var y = blockY; y < maxY; y++)
                {
                    for (var x = blockX; x < maxX; x++)
                    {
                        var index = y * stride + x * 4;
                        pixels[index] = blue;
                        pixels[index + 1] = green;
                        pixels[index + 2] = red;
                        pixels[index + 3] = alpha;
                    }
                }
            }
        }

        var result = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        result.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        result.Freeze();
        return result;
    }

    private static Geometry CreateArrowGeometry(Point start, Point end)
    {
        var direction = end - start;
        if (direction.Length < 1)
            return Geometry.Empty;

        direction.Normalize();
        const double headLength = 12;
        const double headAngle = 28 * Math.PI / 180;
        var backward = -direction;
        var left = Rotate(backward, headAngle) * headLength + end;
        var right = Rotate(backward, -headAngle) * headLength + end;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, false, false);
            context.LineTo(end, true, false);
            context.BeginFigure(left, false, false);
            context.LineTo(end, true, false);
            context.LineTo(right, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Vector Rotate(Vector vector, double radians) => new(
        vector.X * Math.Cos(radians) - vector.Y * Math.Sin(radians),
        vector.X * Math.Sin(radians) + vector.Y * Math.Cos(radians));

    private SolidColorBrush CreateAnnotationBrush() => new(_annotationColor);

    private static double GetCanvasLeft(UIElement element)
    {
        var left = Canvas.GetLeft(element);
        return double.IsNaN(left) ? 0 : left;
    }

    private static double GetCanvasTop(UIElement element)
    {
        var top = Canvas.GetTop(element);
        return double.IsNaN(top) ? 0 : top;
    }

    private static void SetCanvasPosition(UIElement element, Point position)
    {
        Canvas.SetLeft(element, position.X);
        Canvas.SetTop(element, position.Y);
    }

    private static void SetArrowPoints(ArrowAnnotation arrow, Point start, Point end)
    {
        arrow.Start = start;
        arrow.End = end;
        arrow.Shape.Data = CreateArrowGeometry(start, end);
    }

    private void AddAnnotation(UIElement annotation)
    {
        var action = new EditorAction(
            () => AnnotateCanvas.Children.Remove(annotation),
            () =>
            {
                if (!AnnotateCanvas.Children.Contains(annotation))
                    AnnotateCanvas.Children.Add(annotation);
            });
        ExecuteHistoryAction(action);
        _additionActions[annotation] = action;
        SelectAnnotation(annotation);
    }

    private void DiscardAnnotation(UIElement annotation)
    {
        AnnotateCanvas.Children.Remove(annotation);
        if (!_additionActions.Remove(annotation, out var action)) return;

        var historyIndex = _editorHistory.IndexOf(action);
        if (historyIndex < 0) return;
        _editorHistory.RemoveAt(historyIndex);
        if (historyIndex <= _historyIndex) _historyIndex--;
    }

    private void ExecuteHistoryAction(EditorAction action)
    {
        if (_historyIndex < _editorHistory.Count - 1)
            _editorHistory.RemoveRange(_historyIndex + 1, _editorHistory.Count - _historyIndex - 1);

        _editorHistory.Add(action);
        _historyIndex = _editorHistory.Count - 1;
        action.Redo();
    }

    private UIElement? FindAnnotation(DependencyObject? source)
    {
        for (var current = source; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is Rectangle rectangle && rectangle != _selectionOutline
                && AnnotateCanvas.Children.Contains(rectangle))
                return rectangle;
            if (current is Polyline line && AnnotateCanvas.Children.Contains(line))
                return line;
            if (current is System.Windows.Shapes.Path path && _arrowAnnotations.ContainsKey(path))
                return path;
            if (current is Grid grid && _textAnnotations.ContainsKey(grid))
                return grid;
        }
        return null;
    }

    private void SelectAnnotation(UIElement? annotation)
    {
        _selectedAnnotation = annotation;
        if (annotation is not Grid grid || !_textAnnotations.TryGetValue(grid, out var textAnnotation)
            || !textAnnotation.IsEditing)
        {
            AnnotateCanvas.Focus();
        }
        UpdateSelectionOutline();
    }

    private void UpdateSelectionOutline()
    {
        if (_selectedAnnotation == null || !AnnotateCanvas.Children.Contains(_selectedAnnotation))
        {
            _selectionOutline.Visibility = Visibility.Collapsed;
            HideRectangleResizeHandles();
            return;
        }

        var bounds = GetAnnotationBounds(_selectedAnnotation);
        if (bounds.IsEmpty)
        {
            _selectionOutline.Visibility = Visibility.Collapsed;
            HideRectangleResizeHandles();
            return;
        }

        Canvas.SetLeft(_selectionOutline, bounds.X - 4);
        Canvas.SetTop(_selectionOutline, bounds.Y - 4);
        _selectionOutline.Width = bounds.Width + 8;
        _selectionOutline.Height = bounds.Height + 8;
        _selectionOutline.Visibility = Visibility.Visible;
        if (_selectedAnnotation is Rectangle)
            ShowRectangleResizeHandles(bounds);
        else
            HideRectangleResizeHandles();
    }

    private Rect GetAnnotationBounds(UIElement annotation)
    {
        if (annotation is Rectangle rectangle)
            return new Rect(GetCanvasLeft(rectangle), GetCanvasTop(rectangle), rectangle.Width, rectangle.Height);

        if (annotation is System.Windows.Shapes.Path path && _arrowAnnotations.TryGetValue(path, out var arrow))
        {
            var x = Math.Min(arrow.Start.X, arrow.End.X);
            var y = Math.Min(arrow.Start.Y, arrow.End.Y);
            return new Rect(x, y, Math.Abs(arrow.End.X - arrow.Start.X), Math.Abs(arrow.End.Y - arrow.Start.Y));
        }

        if (annotation is Polyline line && line.Points.Count > 0)
        {
            var minX = line.Points.Min(point => point.X);
            var minY = line.Points.Min(point => point.Y);
            var maxX = line.Points.Max(point => point.X);
            var maxY = line.Points.Max(point => point.Y);
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        if (annotation is Grid grid)
            return new Rect(GetCanvasLeft(grid), GetCanvasTop(grid), grid.Width, grid.Height);

        return Rect.Empty;
    }

    private void AddTextAt(Point pos)
    {
        var container = new Grid
        {
            Width = 120,
            Height = 32,
            MinWidth = 80,
            MinHeight = 28
        };
        var tb = new TextBox
        {
            Text = "",
            FontSize = 16,
            FontFamily = new FontFamily("Microsoft YaHei"),
            Foreground = CreateAnnotationBrush(),
            Background = Brushes.Transparent,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalContentAlignment = VerticalAlignment.Top,
            Padding = new Thickness(3, 1, 3, 1),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xD9))
        };
        var display = new TextBlock
        {
            FontSize = tb.FontSize,
            FontFamily = tb.FontFamily,
            Foreground = tb.Foreground,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(3, 1, 3, 1),
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed
        };
        var resizeThumb = new Thumb
        {
            Width = 14,
            Height = 14,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = Brushes.Transparent,
            Opacity = 0,
            Cursor = Cursors.SizeNWSE,
            ToolTip = "拖动调整文字框大小"
        };
        var originalSize = new Size();
        resizeThumb.DragStarted += (_, _) => originalSize = new Size(container.Width, container.Height);
        resizeThumb.DragDelta += (_, e) =>
        {
            container.Width = Math.Max(container.MinWidth, container.Width + e.HorizontalChange);
            container.Height = Math.Max(container.MinHeight, container.Height + e.VerticalChange);
            UpdateSelectionOutline();
        };
        resizeThumb.DragCompleted += (_, _) =>
        {
            var updatedSize = new Size(container.Width, container.Height);
            if (updatedSize == originalSize) return;
            ExecuteHistoryAction(new EditorAction(
                () => SetTextAnnotationSize(container, originalSize),
                () => SetTextAnnotationSize(container, updatedSize)));
        };
        var moveThumb = new Thumb
        {
            Height = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Background = Brushes.Transparent,
            Opacity = 0,
            Cursor = Cursors.SizeAll,
            ToolTip = "拖动移动文字框"
        };
        var originalPosition = new Point();
        moveThumb.DragStarted += (_, _) =>
            originalPosition = new Point(GetCanvasLeft(container), GetCanvasTop(container));
        moveThumb.DragDelta += (_, e) =>
        {
            Canvas.SetLeft(container, GetCanvasLeft(container) + e.HorizontalChange);
            Canvas.SetTop(container, GetCanvasTop(container) + e.VerticalChange);
            UpdateSelectionOutline();
        };
        moveThumb.DragCompleted += (_, _) =>
        {
            var updatedPosition = new Point(GetCanvasLeft(container), GetCanvasTop(container));
            if (updatedPosition == originalPosition) return;
            ExecuteHistoryAction(new EditorAction(
                () => SetCanvasPosition(container, originalPosition),
                () => SetCanvasPosition(container, updatedPosition)));
        };

        var annotation = new TextAnnotation(container, tb, display);
        tb.LostKeyboardFocus += (_, _) => CommitTextAnnotation(annotation);
        display.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2) return;
            BeginTextEditing(annotation);
            e.Handled = true;
        };

        container.Children.Add(display);
        container.Children.Add(tb);
        container.Children.Add(moveThumb);
        container.Children.Add(resizeThumb);
        Canvas.SetLeft(container, pos.X);
        Canvas.SetTop(container, pos.Y);
        AddAnnotation(container);
        _textAnnotations.Add(container, annotation);
        tb.Focus();
    }

    private void BeginTextEditing(TextAnnotation annotation)
    {
        if (annotation.IsEditing) return;

        annotation.IsEditing = true;
        annotation.Display.Visibility = Visibility.Collapsed;
        annotation.Editor.Visibility = Visibility.Visible;
        annotation.Editor.BorderThickness = new Thickness(1);
        annotation.Editor.Focus();
        annotation.Editor.CaretIndex = annotation.Editor.Text.Length;
    }

    private void CommitTextAnnotation(TextAnnotation annotation)
    {
        if (!annotation.IsEditing) return;

        annotation.IsEditing = false;
        if (string.IsNullOrWhiteSpace(annotation.Editor.Text))
        {
            DiscardAnnotation(annotation.Container);
            _textAnnotations.Remove(annotation.Container);
            return;
        }

        var originalText = annotation.Display.Text;
        annotation.Display.Text = annotation.Editor.Text;
        annotation.Editor.Visibility = Visibility.Collapsed;
        annotation.Display.Visibility = Visibility.Visible;
        if (!annotation.IsNew && originalText != annotation.Editor.Text)
        {
            var updatedText = annotation.Editor.Text;
            ExecuteHistoryAction(new EditorAction(
                () => SetTextAnnotationContent(annotation, originalText),
                () => SetTextAnnotationContent(annotation, updatedText)));
        }
        annotation.IsNew = false;
    }

    private static void SetTextAnnotationSize(Grid container, Size size)
    {
        container.Width = size.Width;
        container.Height = size.Height;
    }

    private static void SetTextAnnotationContent(TextAnnotation annotation, string text)
    {
        annotation.Editor.Text = text;
        annotation.Display.Text = text;
        annotation.Editor.Visibility = Visibility.Collapsed;
        annotation.Display.Visibility = Visibility.Visible;
        annotation.IsEditing = false;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.FocusedElement is not TextBox)
        {
            if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                RedoLastAnnotation();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Z)
            {
                UndoLastAnnotation();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Y)
            {
                RedoLastAnnotation();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Escape)
        {
            Close();
        }

        if (e.Key == Key.Delete && Keyboard.FocusedElement is not TextBox)
        {
            DeleteSelectedAnnotation();
            e.Handled = true;
        }
    }

    private bool TryFindArrow(Point position, out ArrowAnnotation annotation, out ArrowDragMode dragMode)
    {
        const double endpointRadius = 20;
        const double bodyRadius = 14;

        foreach (var candidate in _arrowAnnotations.Values.Reverse())
        {
            if ((position - candidate.Start).Length <= endpointRadius)
            {
                annotation = candidate;
                dragMode = ArrowDragMode.Start;
                return true;
            }
            if ((position - candidate.End).Length <= endpointRadius)
            {
                annotation = candidate;
                dragMode = ArrowDragMode.End;
                return true;
            }
        }

        foreach (var candidate in _arrowAnnotations.Values.Reverse())
        {
            if (DistanceToSegment(position, candidate.Start, candidate.End) <= bodyRadius)
            {
                annotation = candidate;
                dragMode = ArrowDragMode.Move;
                return true;
            }
        }

        annotation = null!;
        dragMode = ArrowDragMode.Move;
        return false;
    }

    private static double DistanceToSegment(Point point, Point start, Point end)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared;
        if (lengthSquared < 0.001)
            return (point - start).Length;

        var projection = Vector.Multiply(point - start, segment) / lengthSquared;
        projection = Math.Clamp(projection, 0, 1);
        var closest = start + segment * projection;
        return (point - closest).Length;
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button)
            button.IsChecked = false;
        UndoLastAnnotation();
    }

    private void UndoLastAnnotation()
    {
        if (_historyIndex < 0)
        {
            StatusBarText.Text = "没有可撤销的标注";
            return;
        }

        _editorHistory[_historyIndex--].Undo();
        if (_selectedAnnotation != null && !AnnotateCanvas.Children.Contains(_selectedAnnotation))
            SelectAnnotation(null);
        else
            UpdateSelectionOutline();
        StatusBarText.Text = "已撤销最后一个标注";
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button)
            button.IsChecked = false;
        RedoLastAnnotation();
    }

    private void RedoLastAnnotation()
    {
        if (_historyIndex >= _editorHistory.Count - 1)
        {
            StatusBarText.Text = "没有可重做的标注";
            return;
        }

        _editorHistory[++_historyIndex].Redo();
        UpdateSelectionOutline();
        StatusBarText.Text = "已重做标注";
    }

    private void DeleteSelectedAnnotation()
    {
        if (_selectedAnnotation == null) return;

        var annotation = _selectedAnnotation;
        var index = AnnotateCanvas.Children.IndexOf(annotation);
        if (index < 0) return;

        ExecuteHistoryAction(new EditorAction(
            () => AnnotateCanvas.Children.Insert(Math.Min(index, AnnotateCanvas.Children.Count), annotation),
            () => AnnotateCanvas.Children.Remove(annotation)));
        SelectAnnotation(null);
        StatusBarText.Text = "已删除标注";
    }

    // 合成最终图（保留原始像素尺寸，按比例放大显示中的标注）
    private BitmapSource Compose()
    {
        foreach (var annotation in _textAnnotations.Values.ToArray())
            CommitTextAnnotation(annotation);

        // 画布逻辑坐标与原图像素一致，因此标注可直接按原图尺寸渲染。
        var previousSelectionVisibility = _selectionOutline.Visibility;
        var previousHandleVisibilities = _rectangleResizeHandles
            .Select(handle => handle.Visibility)
            .ToArray();
        _selectionOutline.Visibility = Visibility.Collapsed;
        foreach (var handle in _rectangleResizeHandles)
            handle.Visibility = Visibility.Collapsed;
        var annotationVisual = new DrawingVisual();
        using (var dc = annotationVisual.RenderOpen())
        {
            dc.DrawRectangle(new VisualBrush(AnnotateCanvas), null,
                new Rect(0, 0, _shot.PixelWidth, _shot.PixelHeight));
        }
        var annRt = new RenderTargetBitmap(_shot.PixelWidth, _shot.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        annRt.Render(annotationVisual);
        _selectionOutline.Visibility = previousSelectionVisibility;
        for (var index = 0; index < _rectangleResizeHandles.Count; index++)
            _rectangleResizeHandles[index].Visibility = previousHandleVisibilities[index];

        // 原图与按比例缩放后的标注合成，避免导出时降低截图清晰度。
        var vis = new DrawingVisual();
        using (var dc = vis.RenderOpen())
        {
            var outputRect = new Rect(0, 0, _shot.PixelWidth, _shot.PixelHeight);
            dc.DrawImage(_shot, outputRect);
            dc.DrawImage(annRt, outputRect);
        }
        var rtb = new RenderTargetBitmap(_shot.PixelWidth, _shot.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(vis);
        rtb.Freeze();
        return rtb;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = Compose();
            Clipboard.SetImage(result);
            Close();
        }
        catch (Exception ex)
        {
            StatusBarText.Text = "复制失败: " + ex.Message;
        }
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var pinnedWindow = new PinnedScreenshotWindow(Compose(), _pinnedOpacity);
            pinnedWindow.Show();
            Close();
        }
        catch (Exception ex)
        {
            StatusBarText.Text = "钉图失败: " + ex.Message;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "PNG 图片|*.png|JPEG 图片|*.jpg",
            FilterIndex = _defaultSaveFormat == "Jpeg" ? 2 : 1,
            FileName = $"截图_{DateTime.Now:yyyyMMdd_HHmmss}.{(_defaultSaveFormat == "Jpeg" ? "jpg" : "png")}",
            Title = "保存截图"
        };
        if (!string.IsNullOrWhiteSpace(_defaultSaveDirectory) && Directory.Exists(_defaultSaveDirectory))
            dlg.InitialDirectory = _defaultSaveDirectory;
        if (dlg.ShowDialog(this) == true)
        {
            try
            {
                var result = Compose();
                BitmapEncoder encoder = System.IO.Path.GetExtension(dlg.FileName).Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                    ? new JpegBitmapEncoder { QualityLevel = _jpegQuality }
                    : new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(result));
                using var fs = new FileStream(dlg.FileName, FileMode.Create);
                encoder.Save(fs);
                StatusBarText.Text = "已保存: " + dlg.FileName;
            }
            catch (Exception ex)
            {
                StatusBarText.Text = "保存失败: " + ex.Message;
            }
        }
    }
}
