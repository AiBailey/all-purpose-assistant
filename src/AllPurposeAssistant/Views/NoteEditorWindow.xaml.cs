using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AllPurposeAssistant.Models;
using AllPurposeAssistant.Services;
using Microsoft.Win32;

namespace AllPurposeAssistant.Views;

public partial class NoteEditorWindow : Window
{
    private const double BallSize = 64;
    private readonly NoteService _noteService;
    private NoteItem _note;
    private readonly Point _anchorPoint;
    private ResizeEdge _resizeEdge;
    private Point _resizeStartScreen;
    private double _resizeStartWidth;
    private double _resizeStartHeight;
    private double _resizeStartLeft;
    private double _resizeStartTop;

    public NoteEditorWindow(NoteService noteService, NoteItem note, Point anchorPoint)
    {
        _noteService = noteService;
        _note = note;
        _anchorPoint = anchorPoint;
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PositionNearBall();
        LoadBackground();
        if (!string.IsNullOrEmpty(_note.Content))
        {
            var range = new TextRange(ContentBox.Document.ContentStart, ContentBox.Document.ContentEnd);
            range.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(_note.Content)), DataFormats.Rtf);
        }
    }

    private void RootBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RootBorder.Clip = new RectangleGeometry(new Rect(e.NewSize), 18, 18);
    }

    private void ResizeSurface_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _resizeEdge = GetResizeEdge(e.GetPosition(this));
        if (_resizeEdge == ResizeEdge.None) return;

        _resizeStartScreen = PointToScreen(e.GetPosition(this));
        _resizeStartWidth = Width;
        _resizeStartHeight = Height;
        _resizeStartLeft = Left;
        _resizeStartTop = Top;
        Mouse.Capture(ResizeSurface);
        e.Handled = true;
    }

    private void ResizeSurface_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_resizeEdge == ResizeEdge.None)
        {
            ResizeSurface.Cursor = GetResizeCursor(GetResizeEdge(e.GetPosition(this)));
            return;
        }

        var currentScreen = PointToScreen(e.GetPosition(this));
        var delta = currentScreen - _resizeStartScreen;

        if (_resizeEdge is ResizeEdge.Left or ResizeEdge.TopLeft or ResizeEdge.BottomLeft)
        {
            var width = Math.Max(MinWidth, _resizeStartWidth - delta.X);
            Left = _resizeStartLeft + _resizeStartWidth - width;
            Width = width;
        }
        else if (_resizeEdge is ResizeEdge.Right or ResizeEdge.TopRight or ResizeEdge.BottomRight)
        {
            Width = Math.Max(MinWidth, _resizeStartWidth + delta.X);
        }

        if (_resizeEdge is ResizeEdge.Top or ResizeEdge.TopLeft or ResizeEdge.TopRight)
        {
            var height = Math.Max(MinHeight, _resizeStartHeight - delta.Y);
            Top = _resizeStartTop + _resizeStartHeight - height;
            Height = height;
        }
        else if (_resizeEdge is ResizeEdge.Bottom or ResizeEdge.BottomLeft or ResizeEdge.BottomRight)
        {
            Height = Math.Max(MinHeight, _resizeStartHeight + delta.Y);
        }
    }

    private void ResizeSurface_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_resizeEdge == ResizeEdge.None) return;

        _resizeEdge = ResizeEdge.None;
        Mouse.Capture(null);
        ResizeSurface.Cursor = null;
        e.Handled = true;
    }

    private ResizeEdge GetResizeEdge(Point point)
    {
        const double resizeBorder = 8;
        const double cornerResizeBorder = 18;
        var frameOrigin = RootBorder.TranslatePoint(new Point(), this);
        var leftEdge = frameOrigin.X;
        var rightEdge = leftEdge + RootBorder.ActualWidth;
        var topEdge = frameOrigin.Y;
        var bottomEdge = topEdge + RootBorder.ActualHeight;
        var left = point.X >= leftEdge && point.X <= leftEdge + resizeBorder;
        var right = point.X >= rightEdge - resizeBorder && point.X <= rightEdge;
        var top = point.Y >= topEdge && point.Y <= topEdge + resizeBorder;
        var bottom = point.Y >= bottomEdge - resizeBorder && point.Y <= bottomEdge;
        var topLeft = point.X >= leftEdge && point.X <= leftEdge + cornerResizeBorder
            && point.Y >= topEdge && point.Y <= topEdge + cornerResizeBorder;
        var topRight = point.X >= rightEdge - cornerResizeBorder && point.X <= rightEdge
            && point.Y >= topEdge && point.Y <= topEdge + cornerResizeBorder;
        var bottomLeft = point.X >= leftEdge && point.X <= leftEdge + cornerResizeBorder
            && point.Y >= bottomEdge - cornerResizeBorder && point.Y <= bottomEdge;
        var bottomRight = point.X >= rightEdge - cornerResizeBorder && point.X <= rightEdge
            && point.Y >= bottomEdge - cornerResizeBorder && point.Y <= bottomEdge;

        if (topLeft) return ResizeEdge.TopLeft;
        if (topRight) return ResizeEdge.TopRight;
        if (bottomLeft) return ResizeEdge.BottomLeft;
        if (bottomRight) return ResizeEdge.BottomRight;

        return (left, right, top, bottom) switch
        {
            (true, _, _, _) => ResizeEdge.Left,
            (_, true, _, _) => ResizeEdge.Right,
            (_, _, true, _) => ResizeEdge.Top,
            (_, _, _, true) => ResizeEdge.Bottom,
            _ => ResizeEdge.None
        };
    }

    private static Cursor? GetResizeCursor(ResizeEdge edge) => edge switch
    {
        ResizeEdge.Left or ResizeEdge.Right => Cursors.SizeWE,
        ResizeEdge.Top or ResizeEdge.Bottom => Cursors.SizeNS,
        ResizeEdge.TopLeft or ResizeEdge.BottomRight => Cursors.SizeNWSE,
        ResizeEdge.TopRight or ResizeEdge.BottomLeft => Cursors.SizeNESW,
        _ => null
    };

    private void LoadBackground()
    {
        try
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bg_sidebar.png");
            if (!File.Exists(path)) return;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            NoteBackgroundImage.Source = image;
        }
        catch
        {
        }
    }

    private void PositionNearBall()
    {
        var workArea = SystemParameters.WorkArea;
        // 便签右上角对齐悬浮球左下角：Left 对齐浮球左缘，Top 在球底部之下
        Left = System.Math.Clamp(_anchorPoint.X, workArea.Left, workArea.Right - Width);
        Top = System.Math.Clamp(_anchorPoint.Y + BallSize, workArea.Top, workArea.Bottom - Height);
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveNote();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!Toolbar.IsMouseOver && e.ClickCount == 1)
            DragMove();
    }

    private void ContentBox_TextChanged(object sender, TextChangedEventArgs e)
    {
    }

    private void InsertImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
            Multiselect = false
        };

        if (dlg.ShowDialog() == true)
        {
            var srcPath = dlg.FileName;
            var ext = Path.GetExtension(srcPath);
            var destFileName = $"{_note.Id}_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..4]}{ext}";
            var destPath = Path.Combine(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AllPurposeAssistant", "Images"),
                destFileName);

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            File.Copy(srcPath, destPath, true);

            _note.ImagePaths.Add(Path.Combine("Images", destFileName));

            var img = new Image
            {
                Source = new BitmapImage(new Uri(destPath)),
                MaxWidth = ContentBox.ActualWidth - 40,
                Stretch = Stretch.Uniform
            };
            var container = new InlineUIContainer(img, ContentBox.CaretPosition);
            ContentBox.CaretPosition = container.ElementEnd;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        SaveNote();
        Close();
    }

    private void SaveNote()
    {
        _note.Title = "便签";

        var range = new TextRange(ContentBox.Document.ContentStart, ContentBox.Document.ContentEnd);
        using var ms = new MemoryStream();
        range.Save(ms, DataFormats.Rtf);
        ms.Position = 0;
        _note.Content = new StreamReader(ms).ReadToEnd();

        _noteService.Save(_note);
    }

    private enum ResizeEdge
    {
        None,
        Left,
        Right,
        Top,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }
}
