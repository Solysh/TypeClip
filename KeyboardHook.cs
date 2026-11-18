using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TypeClip;

/// <summary>
/// Low-level keyboard hook. Unlike RegisterHotKey it lets us decide per keystroke: the hotkey is swallowed only
/// when the foreground process is on the allowlist, and passes through untouched everywhere else.
/// The callback runs on the thread that installed it (the UI thread) and must return fast.
/// </summary>
sealed class KeyboardHook : IDisposable
{
    readonly Native.LowLevelKeyboardProc _proc; // keep a reference so the delegate isn't collected
    IntPtr _hook;
    Hotkey _hotkey = Hotkey.Default;
    string _hotkeyText = "";
    bool _hotkeyErrorShown;
    volatile bool _swallowing;
    long _swallowTick;

    volatile bool _swallowEsc;

    public bool Enabled { get; set; } = true;
    /// <summary>Set while text is being typed; makes Esc abort it.</summary>
    public volatile bool Typing;
    public bool TriggerKeyHeld => _swallowing;
    public event Action? Triggered;
    public event Action? Aborted;
    public event Action<string>? Error;

    public KeyboardHook()
    {
        _proc = HookProc;
        _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc, Native.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && Handle(wParam, lParam)) return (IntPtr)1;
        }
        catch { /* never throw out of a hook */ }
        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <returns>true to swallow the event.</returns>
    bool Handle(IntPtr wParam, IntPtr lParam)
    {
        var k = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
        if (k.dwExtraInfo == Native.Marker) return false; // our own injected keys

        int msg = (int)wParam;
        bool down = msg is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
        bool up = msg is Native.WM_KEYUP or Native.WM_SYSKEYUP;

        // Esc aborts typing in progress. The press is swallowed so the remote session doesn't see it.
        if (k.vkCode == 0x1B)
        {
            if (down && Typing) { _swallowEsc = true; Aborted?.Invoke(); return true; }
            if (up && _swallowEsc) { _swallowEsc = false; return true; }
        }

        var cfg = Config.Current;
        RefreshHotkey(cfg.Hotkey);
        if (k.vkCode != _hotkey.Vk) return false;

        if (down && cfg.DebugLog)
            Log.Write($"key seen: enabled={Enabled} mods={_hotkey.ModifiersMatch()} fg={Native.ProcessNameOf(Native.GetForegroundWindow())}");

        if (down)
        {
            if (_swallowing)
            {
                // Auto-repeat of a swallowed press. Self-heal if the key-up was somehow missed.
                if (Environment.TickCount64 - _swallowTick < 3000) return true;
                _swallowing = false;
            }
            if (!Enabled || !_hotkey.ModifiersMatch()) return false;

            var name = Native.ProcessNameOf(Native.GetForegroundWindow());
            if (name == null || !cfg.AllowedProcesses.Contains(name)) return false; // not our window: pass through

            _swallowing = true;
            _swallowTick = Environment.TickCount64;
            Triggered?.Invoke();
            return true;
        }

        if (up && _swallowing)
        {
            _swallowing = false;
            return true;
        }
        return false;
    }

    void RefreshHotkey(string text)
    {
        if (text == _hotkeyText) return;
        _hotkeyText = text;
        if (Hotkey.TryParse(text, out var hk))
        {
            _hotkey = hk;
            _hotkeyErrorShown = false;
        }
        else if (!_hotkeyErrorShown)
        {
            _hotkeyErrorShown = true;
            Error?.Invoke($"Invalid hotkey \"{text}\"; using {(_hotkey == Hotkey.Default ? "Ctrl+Shift+V" : "the previous hotkey")}.");
        }
    }

    /// <summary>
    /// Windows calls the most recently installed low-level hook first. mstsc installs its own hook when it gets focus,
    /// which puts it ahead of ours. Re-installing (new hook first, then remove the old one) moves us back to the front.
    /// </summary>
    public void Reinstall()
    {
        var fresh = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc, Native.GetModuleHandle(null), 0);
        if (fresh == IntPtr.Zero) return;
        var old = _hook;
        _hook = fresh;
        if (old != IntPtr.Zero) Native.UnhookWindowsHookEx(old);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            Native.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
