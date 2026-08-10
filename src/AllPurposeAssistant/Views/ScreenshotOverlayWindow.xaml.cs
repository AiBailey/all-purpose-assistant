using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using AllPurposeAssistant.Helpers;

namespace AllPurposeAssistant.Views;

// 参数：选区相对虚拟屏幕左上角的物理像素坐标 (left, top, width, height)
public delegate void ScreenshotSelectionCallback(int x, int y, int width, int height);
public delegate void ScreenshotCancelCallback();

public partial class ScreenshotOverlayWindow : Window
{
    private readonly ScreenshotSelectionCallback _onSelected;
    private readonly ScreenshotCancelCallback _onCancel;
    private readonly CaptureBounds _captureBounds;
    private readonly BitmapSource _screenImage;
    private readonly double _dpiScale;
    private Point _startPhysical;

    public ScreenshotOverlayWindow(CaptureBounds captureBounds, BitmapSource screenImage, double dpiScale,
        ScreenshotSelectionCallback onSelected, ScreenshotCancelCallback onCancel)
    {
        _captureBounds = captureBounds;
        _screenImage = screenImage;
        _dpiScale = dpiScale;
        _onSelected = onSelected;
        _onCancel = onCancel;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 以系统 DPI 换算窗口坐标，覆盖整个虚拟桌面。
        Left = _captureBounds.Left / _dpiScale;
        Top = _captureBounds.Top / _dpiScale;
        Width = _captureBounds.Width / _dpiScale;
        Height = _captureBounds.Height / _dpiScale;

        // 蒙层初始铺满
        ResetMasks(0, 0, 0, 0);
        HintText.Text = "拖动选择截图区域，Esc 取消";
    }

    private void ResetMasks(int sx, int sy, int sw, int sh)
    {
        double W = Width, H = Height;
        // 上
        Canvas.SetLeft(MaskTop, 0); Canvas.SetTop(MaskTop, 0);
        MaskTop.Width = W; MaskTop.Height = sy;
        // 下
        Canvas.SetLeft(MaskBottom, 0); Canvas.SetTop(MaskBottom, sy + sh);
        MaskBottom.Width = W; MaskBottom.Height = H - (sy + sh);
        // 左
        Canvas.SetLeft(MaskLeft, 0); Canvas.SetTop(MaskLeft, sy);
        MaskLeft.Width = sx; MaskLeft.Height = sh;
        // 右
        Canvas.SetLeft(MaskRight, sx + sw); Canvas.SetTop(MaskRight, sy);
        MaskRight.Width = W - (sx + sw); MaskRight.Height = sh;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _startPhysical = NativeMethods.GetCursorPos();
        SelectionRect.Visibility = Visibility.Visible;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var cur = NativeMethods.GetCursorPos();
        double x = System.Math.Min(_startPhysical.X, cur.X);
        double y = System.Math.Min(_startPhysical.Y, cur.Y);
        double w = System.Math.Abs(cur.X - _startPhysical.X);
        double h = System.Math.Abs(cur.Y - _startPhysical.Y);
        var displayTopLeft = ToOverlayDip(x, y);

        // 选区矩形
        Canvas.SetLeft(SelectionRect, displayTopLeft.X);
        Canvas.SetTop(SelectionRect, displayTopLeft.Y);
        SelectionRect.Width = w / _dpiScale;
        SelectionRect.Height = h / _dpiScale;

        UpdateMagnifier(cur, (int)w, (int)h);

        ResetMasks((int)displayTopLeft.X, (int)displayTopLeft.Y,
            (int)(w / _dpiScale), (int)(h / _dpiScale));
    }

    private void UpdateMagnifier(Point cursor, int selectionWidth, int selectionHeight)
    {
        const int sampleSize = 18;
        var relativeX = (int)(cursor.X - _captureBounds.Left);
        var relativeY = (int)(cursor.Y - _captureBounds.Top);
        var cropX = Math.Clamp(relativeX - sampleSize / 2, 0, Math.Max(0, _screenImage.PixelWidth - sampleSize));
        var cropY = Math.Clamp(relativeY - sampleSize / 2, 0, Math.Max(0, _screenImage.PixelHeight - sampleSize));
        var cropWidth = Math.Min(sampleSize, _screenImage.PixelWidth - cropX);
        var cropHeight = Math.Min(sampleSize, _screenImage.PixelHeight - cropY);
        if (cropWidth > 0 && cropHeight > 0)
            MagnifierImage.Source = new CroppedBitmap(_screenImage, new Int32Rect(cropX, cropY, cropWidth, cropHeight));

        SelectionInfoText.Text = $"{selectionWidth} × {selectionHeight} px";
        var position = ToOverlayDip(cursor.X, cursor.Y);
        var left = position.X + 18;
        var top = position.Y + 18;
        if (left + Magnifier.Width > Width)
            left = position.X - Magnifier.Width - 18;
        if (top + Magnifier.Height > Height)
            top = position.Y - Magnifier.Height - 18;
        Canvas.SetLeft(Magnifier, Math.Max(0, left));
        Canvas.SetTop(Magnifier, Math.Max(0, top));
        Magnifier.Visibility = Visibility.Visible;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
        Magnifier.Visibility = Visibility.Collapsed;

        var cur = NativeMethods.GetCursorPos();
        double x = System.Math.Min(_startPhysical.X, cur.X);
        double y = System.Math.Min(_startPhysical.Y, cur.Y);
        double w = System.Math.Abs(cur.X - _startPhysical.X);
        double h = System.Math.Abs(cur.Y - _startPhysical.Y);

        if (w < 5 || h < 5)
        {
            // 过小视为取消
            _onCancel?.Invoke();
            Close();
            return;
        }

        _onSelected?.Invoke((int)(x - _captureBounds.Left), (int)(y - _captureBounds.Top),
            (int)w, (int)h);
        Close();
    }

    private Point ToOverlayDip(double physicalX, double physicalY) => new(
        (physicalX - _captureBounds.Left) / _dpiScale,
        (physicalY - _captureBounds.Top) / _dpiScale);

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        // 失去焦点（点了别处）取消
        if (IsVisible)
        {
            _onCancel?.Invoke();
            Close();
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _onCancel?.Invoke();
            Close();
        }
    }
}
