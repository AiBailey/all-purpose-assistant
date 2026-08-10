using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AllPurposeAssistant.Helpers;
using AllPurposeAssistant.Models;

namespace AllPurposeAssistant.Services;

public class ClipboardService : IDisposable
{
    private const int MaxEntries = 50;
    private readonly object _lock = new();
    private readonly List<ClipboardEntry> _entries = new();
    private HwndSource? _source;
    private bool _running;

    public event Action? Changed;

    // 清空全部剪贴板历史，并删除已保存到磁盘的图片
    public void Clear()
    {
        List<ClipboardEntry> removed;
        lock (_lock)
        {
            removed = _entries.ToList();
            _entries.Clear();
        }
        foreach (var entry in removed)
            CleanupImage(entry);
        Changed?.Invoke();
    }

    public IReadOnlyList<ClipboardEntry> Entries
    {
        get
        {
            lock (_lock)
                return _entries.ToList();
        }
    }

    public void Start()
    {
        if (_running) return;
        _running = true;

        var parameters = new HwndSourceParameters("ClipboardListener")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        NativeMethods.AddClipboardFormatListener(_source.Handle);
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        if (_source != null)
        {
            NativeMethods.RemoveClipboardFormatListener(_source.Handle);
            _source.Dispose();
            _source = null;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
        {
            handled = true;
            Application.Current?.Dispatcher.BeginInvoke(ReadClipboard);
        }
        return IntPtr.Zero;
    }

    private void ReadClipboard()
    {
        try
        {
            var text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
            if (!string.IsNullOrWhiteSpace(text))
            {
                AddText(text);
                return;
            }

            if (Clipboard.ContainsImage())
            {
                AddImage(Clipboard.GetImage());
                return;
            }
        }
        catch
        {
        }
    }

    private void AddText(string text)
    {
        text = text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        lock (_lock)
        {
            var existing = _entries.FirstOrDefault(
                e => e.Type == ClipboardEntryType.Text && e.Text == text);
            if (existing != null)
            {
                if (_entries[0] == existing) return;
                _entries.Remove(existing);
                existing.Timestamp = DateTime.Now;
                _entries.Insert(0, existing);
            }
            else
            {
                _entries.Insert(0, new ClipboardEntry { Type = ClipboardEntryType.Text, Text = text });
            }

            TrimExcess();
        }

        Changed?.Invoke();
    }

    private void AddImage(BitmapSource? image)
    {
        if (image == null) return;

        var fp = ComputeFingerprintHex(image);
        try
        {
            lock (_lock)
            {
                // 全局去重：历史中已有相同图片 → 复用并移到顶部
                if (fp != null)
                {
                    var existing = _entries.FirstOrDefault(
                        e => e.Type == ClipboardEntryType.Image
                             && e.ImageFingerprint == fp);
                    if (existing != null)
                    {
                        if (_entries[0] != existing)
                        {
                            _entries.Remove(existing);
                            existing.Timestamp = DateTime.Now;
                            _entries.Insert(0, existing);
                        }
                        return;
                    }
                }

                // 新图：保存到磁盘并加入历史
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AllPurposeAssistant", "ClipboardImages");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, $"{DateTime.Now:yyyyMMddHHmmssfff}.png");

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var stream = new FileStream(file, FileMode.Create);
                encoder.Save(stream);

                _entries.Insert(0, new ClipboardEntry
                {
                    Type = ClipboardEntryType.Image,
                    ImagePath = file,
                    ImageFingerprint = fp
                });

                TrimExcess();
            }

            Changed?.Invoke();
        }
        catch
        {
        }
    }

    private void TrimExcess()
    {
        while (_entries.Count > MaxEntries)
        {
            var removed = _entries[^1];
            _entries.RemoveAt(_entries.Count - 1);
            CleanupImage(removed);
        }
    }

    // 图片缩放成 16x16 灰度，转成定长十六进制指纹串
    private static string? ComputeFingerprintHex(BitmapSource source)
    {
        try
        {
            const int size = 16;
            var scaled = new TransformedBitmap(source,
                new ScaleTransform(size / (double)source.PixelWidth,
                                   size / (double)source.PixelHeight));
            var data = new byte[size * size * 4];
            scaled.CopyPixels(data, size * 4, 0);
            var sb = new System.Text.StringBuilder(size * size);
            for (int i = 0; i < size * size; i++)
            {
                var gray = (byte)((data[i * 4 + 2] * 3 + data[i * 4 + 1] * 6 + data[i * 4]) / 10);
                sb.Append(gray.ToString("X2"));
            }
            return sb.ToString();
        }
        catch
        {
            return null;
        }
    }

    private void CleanupImage(ClipboardEntry entry)
    {
        if (entry.Type == ClipboardEntryType.Image && entry.ImagePath != null)
        {
            try
            {
                if (File.Exists(entry.ImagePath))
                    File.Delete(entry.ImagePath);
            }
            catch
            {
            }
        }
    }

    public void CopyToClipboard(ClipboardEntry entry)
    {
        try
        {
            if (entry.Type == ClipboardEntryType.Text)
            {
                Clipboard.SetText(entry.Text);
            }
            else if (entry.ImagePath != null && File.Exists(entry.ImagePath))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(entry.ImagePath, UriKind.Absolute);
                bmp.EndInit();
                Clipboard.SetImage(bmp);
            }

            // 点击复制的条目在历史中移到顶部
            MoveToTop(entry);
        }
        catch
        {
        }
    }

    private void MoveToTop(ClipboardEntry entry)
    {
        lock (_lock)
        {
            var idx = _entries.IndexOf(entry);
            if (idx > 0)
            {
                _entries.RemoveAt(idx);
                entry.Timestamp = DateTime.Now;
                _entries.Insert(0, entry);
            }
        }
        Changed?.Invoke();
    }

    public void Dispose()
    {
        Stop();
    }
}
