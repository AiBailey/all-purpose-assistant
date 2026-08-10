using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AllPurposeAssistant.Helpers;

public readonly record struct CaptureBounds(int Left, int Top, int Width, int Height);

public static class ScreenCapture
{
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const int LOGPIXELSX = 88;

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int index);

    private static double GetSystemDpiX()
    {
        IntPtr hdc = GetDC(IntPtr.Zero);
        try
        {
            int dpi = GetDeviceCaps(hdc, LOGPIXELSX);
            return dpi > 0 ? dpi : 96.0;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    // 虚拟桌面截图（所有显示器的物理像素），返回 WPF BitmapSource。
    public static BitmapSource CaptureVirtualScreen(out CaptureBounds bounds)
    {
        bounds = new CaptureBounds(
            GetSystemMetrics(SM_XVIRTUALSCREEN),
            GetSystemMetrics(SM_YVIRTUALSCREEN),
            GetSystemMetrics(SM_CXVIRTUALSCREEN),
            GetSystemMetrics(SM_CYVIRTUALSCREEN));
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException("无法获取虚拟桌面尺寸。");

        using var bmp = new System.Drawing.Bitmap(bounds.Width, bounds.Height,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0,
                new System.Drawing.Size(bounds.Width, bounds.Height));
        }
        return ToBitmapSource(bmp);
    }

    // 从全屏图按物理像素裁剪区域
    public static BitmapSource Crop(BitmapSource source, int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0 || x < 0 || y < 0)
            return source;
        int sw = source.PixelWidth, sh = source.PixelHeight;
        int nx2 = System.Math.Min(x + width, sw);
        int ny2 = System.Math.Min(y + height, sh);
        int nw = nx2 - x, nh = ny2 - y;
        if (nw <= 0 || nh <= 0)
            return source;
        return new CroppedBitmap(source, new Int32Rect(x, y, nw, nh));
    }

    public static BitmapSource ToBitmapSource(System.Drawing.Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;
        var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var result = decoder.Frames[0];
        result.Freeze();
        return result;
    }

    public static double GetDpiScale()
    {
        return GetSystemDpiX() / 96.0;
    }
}
