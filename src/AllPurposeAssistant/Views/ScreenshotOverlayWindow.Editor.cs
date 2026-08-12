using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using AllPurposeAssistant.Helpers;
using Microsoft.Win32;

namespace AllPurposeAssistant.Views;

public partial class ScreenshotOverlayWindow
{
    private enum OverlayState { Selecting, Editing }
    private enum Tool { Pen, Rect, Arrow, Text, Mosaic }
    private enum ArrowDragMode { Move, Start, End }
    private enum RectangleDragMode { Move, TopLeft, TopRight, BottomLeft, BottomRight }

    private sealed class EditorAction(Action undo, Action redo)
    {
        public Action Undo { get; } = undo;
        public Action Redo { get; } = redo;
    }

    private sealed class TextAnnotation(Grid container, TextBox editor, TextBlock display)
    {
        public Grid Container { get; } = container;
        public TextBox Editor { get; } = editor;
        public TextBlock Display { get; } = display;
        public bool IsEditing { get; set; } = true;
        public bool IsNew { get; set; } = true;
    }

    private sealed class ArrowAnnotation(System.Windows.Shapes.Path shape, Point start, Point end)
    {
        public System.Windows.Shapes.Path Shape { get; } = shape;
        public Point Start { get; set; } = start;
        public Point End { get; set; } = end;
    }

    private OverlayState _state;
    private BitmapSource? _shot;
    private string? _defaultSaveDirectory;
    private string _defaultSaveFormat = "Png";
    private int _jpegQuality = 92;
    private double _pinnedOpacity = 1;
    private Tool _tool = Tool.Pen;
    private Color _annotationColor = Color.FromRgb(0xE7, 0x4C, 0x3C);
    private bool _drawing;
    private Point _origin;
    private UIElement? _currentShape;
    private Polyline? _penLine;
    private Rectangle? _movingRectangle;
    private Point _moveStart;
    private Point _rectangleStart;
    private UIElement? _movingAnnotation;
    private Point _annotationDragStart;
    private Point _annotationStartPosition;
    private Point[]? _penStartPoints;
    private Rectangle? _handleResizingRectangle;
    private Rect _handleOriginalBounds;
    private readonly List<Thumb> _rectangleResizeHandles = [];
    private readonly Dictionary<Grid, TextAnnotation> _textAnnotations = [];
    private readonly Dictionary<System.Windows.Shapes.Path, ArrowAnnotation> _arrowAnnotations = [];
    private readonly List<EditorAction> _editorHistory = [];
    private readonly Dictionary<UIElement, EditorAction> _additionActions = [];
    private int _historyIndex = -1;
    private Rectangle? _selectionOutline;
    private UIElement? _selectedAnnotation;
    private ArrowAnnotation? _movingArrow;
    private ArrowDragMode _arrowDragMode;
    private Point _arrowDragStart;
    private Point _arrowStart;
    private Point _arrowEnd;

    public void ConfigureInlineEditor(string? defaultSaveDirectory, string defaultSaveFormat,
        int jpegQuality, double pinnedOpacity)
    {
        _defaultSaveDirectory = defaultSaveDirectory;
        _defaultSaveFormat = defaultSaveFormat is "Jpeg" ? "Jpeg" : "Png";
        _jpegQuality = Math.Clamp(jpegQuality, 1, 100);
        _pinnedOpacity = Math.Clamp(pinnedOpacity, 0.2, 1);
    }

    public void BeginInlineEditing(int x, int y, int width, int height)
    {
        _shot = ScreenCapture.Crop(_screenImage, x, y, width, height);
        if (_shot.PixelWidth <= 0 || _shot.PixelHeight <= 0)
        {
            Close();
            return;
        }

        _state = OverlayState.Editing;
        var position = ToOverlayDip(x + _captureBounds.Left, y + _captureBounds.Top);
        Canvas.SetLeft(SelectionHost, position.X);
        Canvas.SetTop(SelectionHost, position.Y);
        SelectionHost.Width = _shot.PixelWidth / _dpiScale;
        SelectionHost.Height = _shot.PixelHeight / _dpiScale;
        SelectedImage.Source = _shot;
        SelectedImage.Width = SelectionHost.Width;
        SelectedImage.Height = SelectionHost.Height;
        AnnotateCanvas.Width = _shot.PixelWidth;
        AnnotateCanvas.Height = _shot.PixelHeight;
        AnnotateCanvas.LayoutTransform = new ScaleTransform(1 / _dpiScale, 1 / _dpiScale);
        SelectionHost.Visibility = Visibility.Visible;
        SelectionRect.Visibility = Visibility.Collapsed;
        HintText.Visibility = Visibility.Collapsed;
        InlineToolbar.Visibility = Visibility.Visible;

        _selectionOutline = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xD9)),
            StrokeThickness = 1,
            StrokeDashArray = [3, 2],
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        Canvas.SetZIndex(_selectionOutline, int.MaxValue - 1);
        AnnotateCanvas.Children.Add(_selectionOutline);
        CreateRectangleResizeHandle(RectangleDragMode.TopLeft, Cursors.SizeNWSE);
        CreateRectangleResizeHandle(RectangleDragMode.TopRight, Cursors.SizeNESW);
        CreateRectangleResizeHandle(RectangleDragMode.BottomLeft, Cursors.SizeNESW);
        CreateRectangleResizeHandle(RectangleDragMode.BottomRight, Cursors.SizeNWSE);
        AnnotateCanvas.PreviewMouseLeftButtonDown += AnnotateCanvas_PreviewMouseLeftButtonDown;
        SelectionHost.MouseLeftButtonDown += (_, e) => e.Handled = true;
        InlineToolbar.MouseLeftButtonDown += (_, e) => e.Handled = true;
        PreviewMouseLeftButtonDown += InlinePreviewMouseLeftButtonDown;
        Dispatcher.BeginInvoke(PositionToolbar);
        AnnotateCanvas.Focus();
    }

    private void PositionToolbar()
    {
        var selectionLeft = Canvas.GetLeft(SelectionHost);
        var selectionTop = Canvas.GetTop(SelectionHost);
        var left = Math.Clamp(selectionLeft + (SelectionHost.Width - InlineToolbar.ActualWidth) / 2,
            8, Math.Max(8, Width - InlineToolbar.ActualWidth - 8));
        var top = selectionTop + SelectionHost.Height + 8;
        if (top + InlineToolbar.ActualHeight > Height - 8)
            top = selectionTop - InlineToolbar.ActualHeight - 8;
        Canvas.SetLeft(InlineToolbar, left);
        Canvas.SetTop(InlineToolbar, Math.Clamp(top, 8, Math.Max(8, Height - InlineToolbar.ActualHeight - 8)));
    }

    private void InlinePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsDescendantOf(e.OriginalSource as DependencyObject, SelectionHost)
            && !IsDescendantOf(e.OriginalSource as DependencyObject, InlineToolbar))
        {
            e.Handled = true;
            FinishInlineSession();
        }
    }

    private static bool IsDescendantOf(DependencyObject? child, DependencyObject parent)
    {
        for (var current = child; current != null; current = VisualTreeHelper.GetParent(current))
            if (ReferenceEquals(current, parent)) return true;
        return false;
    }

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button) return;
        foreach (var toolButton in new[] { BtnPen, BtnRect, BtnArrow, BtnText, BtnMosaic })
            if (toolButton != button) toolButton.IsChecked = false;
        button.IsChecked = true;
        _tool = (button.Tag as string) switch
        {
            "rect" => Tool.Rect,
            "arrow" => Tool.Arrow,
            "text" => Tool.Text,
            "mosaic" => Tool.Mosaic,
            _ => Tool.Pen
        };
        AnnotateCanvas.Focus();
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not string colorText) return;
        foreach (var colorButton in new[] { ColorRed, ColorBlue, ColorOrange, ColorGreen, ColorBlack })
            if (colorButton != button) colorButton.IsChecked = false;
        button.IsChecked = true;
        _annotationColor = (Color)ColorConverter.ConvertFromString(colorText);
        AnnotateCanvas.Focus();
    }

    private void AnnotateCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _shot == null) return;
        var clicked = FindAnnotation(e.OriginalSource as DependencyObject);
        if (clicked != null)
        {
            SelectAnnotation(clicked);
            if (clicked is Rectangle rectangle)
            {
                _movingRectangle = rectangle;
                _moveStart = e.GetPosition(AnnotateCanvas);
                _rectangleStart = new Point(GetCanvasLeft(rectangle), GetCanvasTop(rectangle));
                AnnotateCanvas.CaptureMouse();
                e.Handled = true;
                return;
            }

            if (clicked is not System.Windows.Shapes.Path)
            {
                BeginAnnotationDrag(clicked, e.GetPosition(AnnotateCanvas));
                e.Handled = true;
                return;
            }
        }
        if ((clicked == null || clicked is System.Windows.Shapes.Path)
            && TryFindArrow(e.GetPosition(AnnotateCanvas), out var arrow, out var arrowDragMode))
        {
            SelectAnnotation(arrow.Shape);
            BeginArrowDrag(arrow, arrowDragMode, e.GetPosition(AnnotateCanvas));
            e.Handled = true;
            return;
        }
        if (e.OriginalSource != AnnotateCanvas) return;

        var pos = e.GetPosition(AnnotateCanvas);
        _drawing = true;
        _origin = pos;
        _currentShape = null;
        if (_tool != Tool.Text) AnnotateCanvas.CaptureMouse();
        switch (_tool)
        {
            case Tool.Pen:
                _penLine = new Polyline
                {
                    Stroke = CreateAnnotationBrush(), StrokeThickness = 3,
                    StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
                _penLine.Points.Add(pos);
                AddAnnotation(_penLine);
                break;
            case Tool.Rect:
                var rect = new Rectangle
                {
                    Stroke = CreateAnnotationBrush(), StrokeThickness = 2,
                    Fill = new SolidColorBrush(Color.FromArgb(30, _annotationColor.R, _annotationColor.G, _annotationColor.B)),
                    Cursor = Cursors.SizeAll
                };
                _currentShape = rect;
                AddAnnotation(rect);
                break;
            case Tool.Arrow:
                var path = new System.Windows.Shapes.Path
                {
                    Stroke = CreateAnnotationBrush(), StrokeThickness = 3,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round
                };
                _currentShape = path;
                AddAnnotation(path);
                _arrowAnnotations.Add(path, new ArrowAnnotation(path, pos, pos));
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
                    BorderThickness = new Thickness(1), IsHitTestVisible = false
                };
                _currentShape = mosaic;
                AddAnnotation(mosaic);
                break;
        }
        e.Handled = true;
    }

    private void AnnotateCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_shot == null)
            return;

        var clicked = FindAnnotation(e.OriginalSource as DependencyObject);
        if (clicked != null && clicked is not System.Windows.Shapes.Path)
            return;

        var position = e.GetPosition(AnnotateCanvas);
        if (!TryFindArrow(position, out var arrow, out var dragMode))
            return;

        SelectAnnotation(arrow.Shape);
        BeginArrowDrag(arrow, dragMode, position);
        e.Handled = true;
    }

    private void BeginArrowDrag(ArrowAnnotation arrow, ArrowDragMode dragMode, Point position)
    {
        _movingArrow = arrow;
        _arrowDragStart = position;
        _arrowStart = arrow.Start;
        _arrowEnd = arrow.End;
        _arrowDragMode = dragMode;
        AnnotateCanvas.CaptureMouse();
    }

    private void BeginAnnotationDrag(UIElement annotation, Point position)
    {
        if (annotation is Grid grid && _textAnnotations.TryGetValue(grid, out var text) && text.IsEditing)
            return;

        _movingAnnotation = annotation;
        _annotationDragStart = position;
        _annotationStartPosition = new Point(GetCanvasLeft(annotation), GetCanvasTop(annotation));
        _penStartPoints = annotation is Polyline line ? line.Points.ToArray() : null;
        AnnotateCanvas.CaptureMouse();
    }

    private void AnnotateCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(AnnotateCanvas);
        if (_movingArrow != null)
        {
            var delta = pos - _arrowDragStart;
            if (_arrowDragMode == ArrowDragMode.Start) _movingArrow.Start = pos;
            else if (_arrowDragMode == ArrowDragMode.End) _movingArrow.End = pos;
            else { _movingArrow.Start = _arrowStart + delta; _movingArrow.End = _arrowEnd + delta; }
            _movingArrow.Shape.Data = CreateArrowGeometry(_movingArrow.Start, _movingArrow.End);
            UpdateSelectionOutline();
            return;
        }
        if (_movingRectangle != null)
        {
            Canvas.SetLeft(_movingRectangle, _rectangleStart.X + pos.X - _moveStart.X);
            Canvas.SetTop(_movingRectangle, _rectangleStart.Y + pos.Y - _moveStart.Y);
            UpdateSelectionOutline();
            return;
        }
        if (_movingAnnotation != null)
        {
            var delta = pos - _annotationDragStart;
            if (_movingAnnotation is Polyline line && _penStartPoints != null)
                SetPolylinePoints(line, _penStartPoints.Select(point => point + delta));
            else
                SetCanvasPosition(_movingAnnotation, _annotationStartPosition + delta);
            UpdateSelectionOutline();
            return;
        }
        if (!_drawing)
        {
            AnnotateCanvas.Cursor = TryFindArrow(pos, out _, out var mode)
                ? mode == ArrowDragMode.Move ? Cursors.SizeAll : Cursors.Cross
                : _tool == Tool.Arrow ? Cursors.Cross : null;
            return;
        }
        switch (_tool)
        {
            case Tool.Pen:
                _penLine?.Points.Add(pos);
                break;
            case Tool.Rect when _currentShape is Rectangle rect:
                SetCanvasBounds(rect, CreateBounds(_origin, pos));
                break;
            case Tool.Arrow when _currentShape is System.Windows.Shapes.Path path:
                path.Data = CreateArrowGeometry(_origin, pos);
                if (_arrowAnnotations.TryGetValue(path, out var arrow)) arrow.End = pos;
                break;
            case Tool.Mosaic when _currentShape is Border mosaic:
                SetCanvasBounds(mosaic, CreateBounds(_origin, pos));
                break;
        }
    }

    private void AnnotateCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_movingArrow != null)
        {
            var arrow = _movingArrow;
            var start = _arrowStart;
            var end = _arrowEnd;
            var updatedStart = arrow.Start;
            var updatedEnd = arrow.End;
            if (start != updatedStart || end != updatedEnd)
                ExecuteHistoryAction(new EditorAction(() => SetArrowPoints(arrow, start, end),
                    () => SetArrowPoints(arrow, updatedStart, updatedEnd)));
            _movingArrow = null;
            AnnotateCanvas.ReleaseMouseCapture();
            UpdateSelectionOutline();
            return;
        }
        if (_movingRectangle != null)
        {
            var rectangle = _movingRectangle;
            var start = _rectangleStart;
            var end = new Point(GetCanvasLeft(rectangle), GetCanvasTop(rectangle));
            if (start != end)
                ExecuteHistoryAction(new EditorAction(() => SetCanvasPosition(rectangle, start),
                    () => SetCanvasPosition(rectangle, end)));
            _movingRectangle = null;
            AnnotateCanvas.ReleaseMouseCapture();
            UpdateSelectionOutline();
            return;
        }
        if (_movingAnnotation != null)
        {
            var annotation = _movingAnnotation;
            if (annotation is Polyline line && _penStartPoints != null)
            {
                var startPoints = _penStartPoints;
                var endPoints = line.Points.ToArray();
                if (!startPoints.SequenceEqual(endPoints))
                    ExecuteHistoryAction(new EditorAction(
                        () => SetPolylinePoints(line, startPoints),
                        () => SetPolylinePoints(line, endPoints)));
            }
            else
            {
                var start = _annotationStartPosition;
                var end = new Point(GetCanvasLeft(annotation), GetCanvasTop(annotation));
                if (start != end)
                    ExecuteHistoryAction(new EditorAction(
                        () => SetCanvasPosition(annotation, start),
                        () => SetCanvasPosition(annotation, end)));
            }
            _movingAnnotation = null;
            _penStartPoints = null;
            AnnotateCanvas.ReleaseMouseCapture();
            UpdateSelectionOutline();
            return;
        }
        if (!_drawing) return;
        _drawing = false;
        AnnotateCanvas.ReleaseMouseCapture();
        if (_currentShape is Border mosaic) FinalizeMosaic(mosaic);
        _currentShape = null;
        _penLine = null;
    }

    private void CreateRectangleResizeHandle(RectangleDragMode mode, Cursor cursor)
    {
        var handle = new Thumb
        {
            Width = 16, Height = 16, Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(74, 144, 217)), BorderThickness = new Thickness(2),
            Cursor = cursor, Tag = mode, Visibility = Visibility.Collapsed
        };
        handle.DragStarted += (_, _) =>
        {
            if (_selectedAnnotation is not Rectangle rectangle) return;
            _handleResizingRectangle = rectangle;
            _handleOriginalBounds = GetRectangleBounds(rectangle);
        };
        handle.DragDelta += (_, _) =>
        {
            if (_handleResizingRectangle == null || handle.Tag is not RectangleDragMode dragMode) return;
            ResizeRectangle(_handleResizingRectangle, _handleOriginalBounds, dragMode, Mouse.GetPosition(AnnotateCanvas));
            UpdateSelectionOutline();
        };
        handle.DragCompleted += (_, _) =>
        {
            if (_handleResizingRectangle == null) return;
            var rectangle = _handleResizingRectangle;
            var start = _handleOriginalBounds;
            var end = GetRectangleBounds(rectangle);
            if (start != end)
                ExecuteHistoryAction(new EditorAction(() => SetCanvasBounds(rectangle, start), () => SetCanvasBounds(rectangle, end)));
            _handleResizingRectangle = null;
            UpdateSelectionOutline();
        };
        _rectangleResizeHandles.Add(handle);
        AnnotateCanvas.Children.Add(handle);
    }

    private void AddTextAt(Point pos)
    {
        var container = new Grid { Width = 120, Height = 32, MinWidth = 80, MinHeight = 28 };
        var editor = new TextBox
        {
            FontSize = 16, FontFamily = new FontFamily("Microsoft YaHei"), Foreground = CreateAnnotationBrush(),
            Background = Brushes.Transparent, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(3, 1, 3, 1),
            BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xD9))
        };
        var display = new TextBlock
        {
            FontSize = editor.FontSize, FontFamily = editor.FontFamily, Foreground = editor.Foreground,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(3, 1, 3, 1),
            VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed
        };
        var annotation = new TextAnnotation(container, editor, display);
        editor.LostKeyboardFocus += (_, _) => CommitTextAnnotation(annotation);
        display.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2) return;
            BeginTextEditing(annotation);
            e.Handled = true;
        };
        AddTextMoveAndResizeHandles(container);
        container.Children.Insert(0, display);
        container.Children.Insert(1, editor);
        Canvas.SetLeft(container, pos.X);
        Canvas.SetTop(container, pos.Y);
        AddAnnotation(container);
        _textAnnotations.Add(container, annotation);
        editor.Focus();
    }

    private void AddTextMoveAndResizeHandles(Grid container)
    {
        var startSize = new Size();
        var resize = new Thumb { Width = 14, Height = 14, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom, Background = Brushes.Transparent, Opacity = 0, Cursor = Cursors.SizeNWSE };
        resize.DragStarted += (_, _) => startSize = new Size(container.Width, container.Height);
        resize.DragDelta += (_, e) =>
        {
            container.Width = Math.Max(container.MinWidth, container.Width + e.HorizontalChange);
            container.Height = Math.Max(container.MinHeight, container.Height + e.VerticalChange);
            UpdateSelectionOutline();
        };
        resize.DragCompleted += (_, _) =>
        {
            var end = new Size(container.Width, container.Height);
            if (startSize != end)
                ExecuteHistoryAction(new EditorAction(() => SetTextAnnotationSize(container, startSize),
                    () => SetTextAnnotationSize(container, end)));
        };
        var startPosition = new Point();
        var move = new Thumb { Height = 8, HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Transparent, Opacity = 0, Cursor = Cursors.SizeAll };
        move.DragStarted += (_, _) => startPosition = new Point(GetCanvasLeft(container), GetCanvasTop(container));
        move.DragDelta += (_, e) =>
        {
            Canvas.SetLeft(container, GetCanvasLeft(container) + e.HorizontalChange);
            Canvas.SetTop(container, GetCanvasTop(container) + e.VerticalChange);
            UpdateSelectionOutline();
        };
        move.DragCompleted += (_, _) =>
        {
            var end = new Point(GetCanvasLeft(container), GetCanvasTop(container));
            if (startPosition != end)
                ExecuteHistoryAction(new EditorAction(() => SetCanvasPosition(container, startPosition),
                    () => SetCanvasPosition(container, end)));
        };
        container.Children.Add(move);
        container.Children.Add(resize);
    }

    private void BeginTextEditing(TextAnnotation annotation)
    {
        if (annotation.IsEditing) return;
        annotation.IsEditing = true;
        annotation.Display.Visibility = Visibility.Collapsed;
        annotation.Editor.Visibility = Visibility.Visible;
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
        var start = annotation.Display.Text;
        var end = annotation.Editor.Text;
        annotation.Display.Text = end;
        annotation.Editor.Visibility = Visibility.Collapsed;
        annotation.Display.Visibility = Visibility.Visible;
        if (!annotation.IsNew && start != end)
            ExecuteHistoryAction(new EditorAction(() => SetTextAnnotationContent(annotation, start),
                () => SetTextAnnotationContent(annotation, end)));
        annotation.IsNew = false;
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
        mosaic.Background = null;
        mosaic.BorderThickness = new Thickness(0);
        mosaic.IsHitTestVisible = true;
        var image = new Image { Source = CreateMosaicBitmap(bounds), Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        mosaic.Child = image;
    }

    private BitmapSource CreateMosaicBitmap(Rect bounds)
    {
        if (_shot == null) throw new InvalidOperationException("截图不可用。");
        var left = Math.Clamp((int)Math.Floor(bounds.Left), 0, _shot.PixelWidth - 1);
        var top = Math.Clamp((int)Math.Floor(bounds.Top), 0, _shot.PixelHeight - 1);
        var right = Math.Clamp((int)Math.Ceiling(bounds.Right), left + 1, _shot.PixelWidth);
        var bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom), top + 1, _shot.PixelHeight);
        var width = right - left;
        var height = bottom - top;
        var converted = new FormatConvertedBitmap(new CroppedBitmap(_shot, new Int32Rect(left, top, width, height)),
            PixelFormats.Bgra32, null, 0);
        var stride = width * 4;
        var pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);
        const int blockSize = 12;
        for (var blockY = 0; blockY < height; blockY += blockSize)
        for (var blockX = 0; blockX < width; blockX += blockSize)
        {
            var sourceIndex = blockY * stride + blockX * 4;
            var blue = pixels[sourceIndex]; var green = pixels[sourceIndex + 1];
            var red = pixels[sourceIndex + 2]; var alpha = pixels[sourceIndex + 3];
            for (var y = blockY; y < Math.Min(blockY + blockSize, height); y++)
            for (var x = blockX; x < Math.Min(blockX + blockSize, width); x++)
            {
                var index = y * stride + x * 4;
                pixels[index] = blue; pixels[index + 1] = green; pixels[index + 2] = red; pixels[index + 3] = alpha;
            }
        }
        var result = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        result.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        result.Freeze();
        return result;
    }

    private void AddAnnotation(UIElement annotation)
    {
        var action = new EditorAction(() => AnnotateCanvas.Children.Remove(annotation), () =>
        {
            if (!AnnotateCanvas.Children.Contains(annotation)) AnnotateCanvas.Children.Add(annotation);
        });
        ExecuteHistoryAction(action);
        _additionActions[annotation] = action;
        SelectAnnotation(annotation);
    }

    private void DiscardAnnotation(UIElement annotation)
    {
        AnnotateCanvas.Children.Remove(annotation);
        if (!_additionActions.Remove(annotation, out var action)) return;
        var index = _editorHistory.IndexOf(action);
        if (index < 0) return;
        _editorHistory.RemoveAt(index);
        if (index <= _historyIndex) _historyIndex--;
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
            if (current is Rectangle rectangle && rectangle != _selectionOutline && AnnotateCanvas.Children.Contains(rectangle)) return rectangle;
            if (current is Polyline line && AnnotateCanvas.Children.Contains(line)) return line;
            if (current is System.Windows.Shapes.Path path && _arrowAnnotations.ContainsKey(path)) return path;
            if (current is Grid grid && _textAnnotations.ContainsKey(grid)) return grid;
            if (current is Border border && _additionActions.ContainsKey(border)) return border;
        }
        return null;
    }

    private void SelectAnnotation(UIElement? annotation)
    {
        _selectedAnnotation = annotation;
        if (annotation is not Grid grid || !_textAnnotations.TryGetValue(grid, out var text) || !text.IsEditing)
            AnnotateCanvas.Focus();
        UpdateSelectionOutline();
    }

    private void UpdateSelectionOutline()
    {
        if (_selectionOutline == null || _selectedAnnotation == null || !AnnotateCanvas.Children.Contains(_selectedAnnotation))
        {
            if (_selectionOutline != null) _selectionOutline.Visibility = Visibility.Collapsed;
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
        if (_selectedAnnotation is Rectangle) ShowRectangleResizeHandles(bounds);
        else HideRectangleResizeHandles();
    }

    private void HideRectangleResizeHandles()
    {
        foreach (var handle in _rectangleResizeHandles) handle.Visibility = Visibility.Collapsed;
    }

    private void ShowRectangleResizeHandles(Rect bounds)
    {
        var corners = new[] { bounds.TopLeft, bounds.TopRight, bounds.BottomLeft, bounds.BottomRight };
        for (var i = 0; i < _rectangleResizeHandles.Count; i++)
        {
            var handle = _rectangleResizeHandles[i];
            Canvas.SetLeft(handle, corners[i].X - handle.Width / 2);
            Canvas.SetTop(handle, corners[i].Y - handle.Height / 2);
            handle.Visibility = Visibility.Visible;
        }
    }

    private Rect GetAnnotationBounds(UIElement annotation) => annotation switch
    {
        Rectangle rectangle => GetRectangleBounds(rectangle),
        System.Windows.Shapes.Path path when _arrowAnnotations.TryGetValue(path, out var arrow) =>
            CreateBounds(arrow.Start, arrow.End),
        Polyline line when line.Points.Count > 0 => new Rect(line.Points.Min(p => p.X), line.Points.Min(p => p.Y),
            line.Points.Max(p => p.X) - line.Points.Min(p => p.X), line.Points.Max(p => p.Y) - line.Points.Min(p => p.Y)),
        Grid grid => new Rect(GetCanvasLeft(grid), GetCanvasTop(grid), grid.Width, grid.Height),
        Border border => new Rect(GetCanvasLeft(border), GetCanvasTop(border), border.Width, border.Height),
        _ => Rect.Empty
    };

    private bool TryFindArrow(Point position, out ArrowAnnotation annotation, out ArrowDragMode mode)
    {
        foreach (var candidate in _arrowAnnotations.Values.Reverse())
        {
            if ((position - candidate.Start).Length <= 20) { annotation = candidate; mode = ArrowDragMode.Start; return true; }
            if ((position - candidate.End).Length <= 20) { annotation = candidate; mode = ArrowDragMode.End; return true; }
        }
        foreach (var candidate in _arrowAnnotations.Values.Reverse())
        {
            if (DistanceToSegment(position, candidate.Start, candidate.End) <= 14)
            { annotation = candidate; mode = ArrowDragMode.Move; return true; }
        }
        annotation = null!; mode = ArrowDragMode.Move; return false;
    }

    private static double DistanceToSegment(Point point, Point start, Point end)
    {
        var segment = end - start;
        if (segment.LengthSquared < 0.001) return (point - start).Length;
        var projection = Math.Clamp(Vector.Multiply(point - start, segment) / segment.LengthSquared, 0, 1);
        return (point - (start + segment * projection)).Length;
    }

    private static Geometry CreateArrowGeometry(Point start, Point end)
    {
        var direction = end - start;
        if (direction.Length < 1) return Geometry.Empty;
        direction.Normalize();
        var backward = -direction;
        var left = Rotate(backward, 28 * Math.PI / 180) * 12 + end;
        var right = Rotate(backward, -28 * Math.PI / 180) * 12 + end;
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

    private static Rect CreateBounds(Point first, Point second) => new(
        Math.Min(first.X, second.X), Math.Min(first.Y, second.Y),
        Math.Abs(first.X - second.X), Math.Abs(first.Y - second.Y));

    private static Rect GetRectangleBounds(Rectangle rectangle) => new(
        GetCanvasLeft(rectangle), GetCanvasTop(rectangle), rectangle.Width, rectangle.Height);

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

    private static void SetPolylinePoints(Polyline line, IEnumerable<Point> points)
    {
        line.Points = new PointCollection(points);
    }

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

    private static void ResizeRectangle(Rectangle rectangle, Rect original, RectangleDragMode mode, Point position)
    {
        const double minimumSize = 8;
        var left = original.Left; var top = original.Top; var right = original.Right; var bottom = original.Bottom;
        switch (mode)
        {
            case RectangleDragMode.TopLeft: left = Math.Min(position.X, right - minimumSize); top = Math.Min(position.Y, bottom - minimumSize); break;
            case RectangleDragMode.TopRight: right = Math.Max(position.X, left + minimumSize); top = Math.Min(position.Y, bottom - minimumSize); break;
            case RectangleDragMode.BottomLeft: left = Math.Min(position.X, right - minimumSize); bottom = Math.Max(position.Y, top + minimumSize); break;
            case RectangleDragMode.BottomRight: right = Math.Max(position.X, left + minimumSize); bottom = Math.Max(position.Y, top + minimumSize); break;
        }
        SetCanvasBounds(rectangle, new Rect(left, top, right - left, bottom - top));
    }

    private SolidColorBrush CreateAnnotationBrush() => new(_annotationColor);

    private static void SetArrowPoints(ArrowAnnotation arrow, Point start, Point end)
    {
        arrow.Start = start; arrow.End = end; arrow.Shape.Data = CreateArrowGeometry(start, end);
    }

    private static void SetTextAnnotationSize(Grid container, Size size)
    {
        container.Width = size.Width; container.Height = size.Height;
    }

    private static void SetTextAnnotationContent(TextAnnotation annotation, string text)
    {
        annotation.Editor.Text = text; annotation.Display.Text = text;
        annotation.Editor.Visibility = Visibility.Collapsed;
        annotation.Display.Visibility = Visibility.Visible;
        annotation.IsEditing = false;
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_historyIndex < 0) return;
        _editorHistory[_historyIndex--].Undo();
        if (_selectedAnnotation != null && !AnnotateCanvas.Children.Contains(_selectedAnnotation)) SelectAnnotation(null);
        else UpdateSelectionOutline();
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (_historyIndex >= _editorHistory.Count - 1) return;
        _editorHistory[++_historyIndex].Redo();
        UpdateSelectionOutline();
    }

    private void DeleteSelectedAnnotation()
    {
        if (_selectedAnnotation == null) return;
        var annotation = _selectedAnnotation;
        var index = AnnotateCanvas.Children.IndexOf(annotation);
        if (index < 0) return;
        ExecuteHistoryAction(new EditorAction(() => AnnotateCanvas.Children.Insert(Math.Min(index, AnnotateCanvas.Children.Count), annotation),
            () => AnnotateCanvas.Children.Remove(annotation)));
        SelectAnnotation(null);
    }

    private BitmapSource Compose()
    {
        if (_shot == null) throw new InvalidOperationException("截图不可用。");
        foreach (var annotation in _textAnnotations.Values.ToArray()) CommitTextAnnotation(annotation);
        var outlineVisibility = _selectionOutline?.Visibility ?? Visibility.Collapsed;
        if (_selectionOutline != null) _selectionOutline.Visibility = Visibility.Collapsed;
        var handleVisibilities = _rectangleResizeHandles.Select(handle => handle.Visibility).ToArray();
        foreach (var handle in _rectangleResizeHandles) handle.Visibility = Visibility.Collapsed;
        var canvasTransform = AnnotateCanvas.LayoutTransform;
        AnnotateCanvas.LayoutTransform = Transform.Identity;
        var annotationVisual = new DrawingVisual();
        using (var dc = annotationVisual.RenderOpen())
            dc.DrawRectangle(new VisualBrush(AnnotateCanvas), null, new Rect(0, 0, _shot.PixelWidth, _shot.PixelHeight));
        var annotations = new RenderTargetBitmap(_shot.PixelWidth, _shot.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        annotations.Render(annotationVisual);
        AnnotateCanvas.LayoutTransform = canvasTransform;
        if (_selectionOutline != null) _selectionOutline.Visibility = outlineVisibility;
        for (var i = 0; i < _rectangleResizeHandles.Count; i++) _rectangleResizeHandles[i].Visibility = handleVisibilities[i];
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var output = new Rect(0, 0, _shot.PixelWidth, _shot.PixelHeight);
            dc.DrawImage(_shot, output);
            dc.DrawImage(annotations, output);
        }
        var result = new RenderTargetBitmap(_shot.PixelWidth, _shot.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual);
        result.Freeze();
        return result;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetImage(Compose()); FinishInlineSession(); }
        catch (Exception ex) { ShowStatus("复制失败: " + ex.Message); }
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        try { new PinnedScreenshotWindow(Compose(), _pinnedOpacity).Show(); FinishInlineSession(); }
        catch (Exception ex) { ShowStatus("钉图失败: " + ex.Message); }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "PNG 图片|*.png|JPEG 图片|*.jpg",
            FilterIndex = _defaultSaveFormat == "Jpeg" ? 2 : 1,
            FileName = $"截图_{DateTime.Now:yyyyMMdd_HHmmss}.{(_defaultSaveFormat == "Jpeg" ? "jpg" : "png")}",
            Title = "保存截图"
        };
        if (!string.IsNullOrWhiteSpace(_defaultSaveDirectory) && Directory.Exists(_defaultSaveDirectory))
            dialog.InitialDirectory = _defaultSaveDirectory;
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            BitmapEncoder encoder = System.IO.Path.GetExtension(dialog.FileName).Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                ? new JpegBitmapEncoder { QualityLevel = _jpegQuality } : new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(Compose()));
            using var stream = new FileStream(dialog.FileName, FileMode.Create);
            encoder.Save(stream);
            ShowStatus("已保存: " + dialog.FileName);
        }
        catch (Exception ex) { ShowStatus("保存失败: " + ex.Message); }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => FinishInlineSession();

    private void FinishInlineSession()
    {
        Close();
    }

    private void ShowStatus(string text)
    {
        HintText.Text = text;
        HintText.Visibility = Visibility.Visible;
    }

    private void HandleEditorKeyDown(KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.FocusedElement is not TextBox)
        {
            if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { Redo_Click(this, e); e.Handled = true; return; }
            if (e.Key == Key.Z) { Undo_Click(this, e); e.Handled = true; return; }
            if (e.Key == Key.Y) { Redo_Click(this, e); e.Handled = true; return; }
        }
        if (e.Key == Key.Delete && Keyboard.FocusedElement is not TextBox)
        {
            DeleteSelectedAnnotation();
            e.Handled = true;
        }
    }
}
