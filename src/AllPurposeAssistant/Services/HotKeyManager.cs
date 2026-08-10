using System.Windows.Interop;
using AllPurposeAssistant.Helpers;

namespace AllPurposeAssistant.Services;

public class HotKeyManager : IDisposable
{
    private IntPtr _handle;
    private readonly Dictionary<int, Action> _callbacks = new();
    private int _nextId = 1;
    private bool _initialized;
    private bool _hookAttached;

    public void Initialize(IntPtr handle)
    {
        if (_initialized) return;
        _handle = handle;
        _initialized = true;
    }

    public int Register(int modifiers, int key, Action callback)
    {
        var id = _nextId;
        if (!NativeMethods.RegisterHotKey(_handle, id, modifiers, key))
            return 0;

        _nextId++;
        _callbacks[id] = callback;

        if (!_hookAttached)
        {
            var source = HwndSource.FromHwnd(_handle);
            source?.AddHook(WndProc);
            _hookAttached = true;
        }

        return id;
    }

    public void Unregister(int id)
    {
        NativeMethods.UnregisterHotKey(_handle, id);
        _callbacks.Remove(id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (_callbacks.TryGetValue(id, out var callback))
            {
                callback();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _callbacks.Keys.ToList())
            NativeMethods.UnregisterHotKey(_handle, id);
        _callbacks.Clear();
    }
}
