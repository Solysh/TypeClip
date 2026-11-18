using System.Diagnostics;
using System.Runtime.InteropServices;
using static TypeClip.Native;

namespace TypeClip;

static class Typer
{
    const uint KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_UNICODE = 0x0004, KEYEVENTF_SCANCODE = 0x0008;
    const ushort VK_RETURN = 0x0D, VK_TAB = 0x09, VK_LSHIFT = 0xA0;

    /// <summary>Normalise line endings to \n and optionally drop one trailing newline.</summary>
    public static string Prepare(string text, Settings cfg)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (cfg.StripTrailingNewline && text.EndsWith('\n')) text = text[..^1];
        return text;
    }

    public static bool ExceedsLimit(string text, Settings cfg) => cfg.MaxChars > 0 && text.Length > cfg.MaxChars;

    /// <summary>Blocking. Run on a worker thread. Aborts if the foreground window changes or ct fires.</summary>
    public static void Type(string text, Settings cfg, Func<bool> triggerKeyHeld, CancellationToken ct)
    {
        // Let the physical hotkey key come up first, then release any modifiers still held.
        var sw = Stopwatch.StartNew();
        while (triggerKeyHeld() && sw.ElapsedMilliseconds < 1000) Thread.Sleep(10);
        ReleaseModifiers();
        Thread.Sleep(30);

        IntPtr target = GetForegroundWindow();
        bool scan = cfg.InputMode == "scancode";

        foreach (char c in text)
        {
            if (ct.IsCancellationRequested || GetForegroundWindow() != target) return;

            if (c == '\n')
            {
                switch (cfg.NewlineMode)
                {
                    case "shiftenter": PressVk(VK_RETURN, scan, shift: true); break;
                    case "join": TypeChar(' ', scan); break;
                    default: PressVk(VK_RETURN, scan, shift: false); break;
                }
            }
            else TypeChar(c, scan);

            if (cfg.CharDelayMs > 0) Thread.Sleep(cfg.CharDelayMs);
        }
    }

    static void ReleaseModifiers()
    {
        // L/R Ctrl, Shift, Alt, Win. Only keys that are actually down get a synthetic key-up.
        foreach (ushort vk in new ushort[] { 0xA2, 0xA3, 0xA0, 0xA1, 0xA4, 0xA5, 0x5B, 0x5C })
            if (IsDown(vk)) Send(Key(vk, (ushort)MapVirtualKey(vk, 0), KEYEVENTF_KEYUP));
    }

    static void TypeChar(char c, bool scancodeMode)
    {
        if (c == '\t') { PressVk(VK_TAB, scancodeMode, shift: false); return; }

        if (scancodeMode && TryScan(c, out ushort sc, out bool shift))
        {
            if (shift) Send(Key(0, ScanOf(VK_LSHIFT), KEYEVENTF_SCANCODE));
            Send(Key(0, sc, KEYEVENTF_SCANCODE), Key(0, sc, KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP));
            if (shift) Send(Key(0, ScanOf(VK_LSHIFT), KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP));
            return;
        }

        // KEYEVENTF_UNICODE: the character itself travels in wScan; surrogate pairs go as two units.
        Send(Key(0, c, KEYEVENTF_UNICODE), Key(0, c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
    }

    /// <summary>Plain ASCII on the current layout, using only Shift. Anything else falls back to Unicode.</summary>
    static bool TryScan(char c, out ushort scan, out bool shift)
    {
        scan = 0; shift = false;
        if (c > 0x7E || c < 0x20) return false;
        short r = VkKeyScan(c);
        if (r == -1) return false;
        int vk = r & 0xFF, state = (r >> 8) & 0xFF;
        if ((state & ~1) != 0) return false; // needs Ctrl/Alt/AltGr
        scan = ScanOf((ushort)vk);
        shift = (state & 1) != 0;
        return scan != 0;
    }

    static ushort ScanOf(ushort vk) => (ushort)MapVirtualKey(vk, 0);

    static void PressVk(ushort vk, bool scancodeMode, bool shift)
    {
        ushort sc = ScanOf(vk);
        if (shift) Send(scancodeMode ? Key(0, ScanOf(VK_LSHIFT), KEYEVENTF_SCANCODE) : Key(VK_LSHIFT, ScanOf(VK_LSHIFT), 0));
        if (scancodeMode)
            Send(Key(0, sc, KEYEVENTF_SCANCODE), Key(0, sc, KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP));
        else
            Send(Key(vk, sc, 0), Key(vk, sc, KEYEVENTF_KEYUP));
        if (shift) Send(scancodeMode ? Key(0, ScanOf(VK_LSHIFT), KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP) : Key(VK_LSHIFT, ScanOf(VK_LSHIFT), KEYEVENTF_KEYUP));
    }

    static INPUT Key(ushort vk, ushort scan, uint flags) => new()
    {
        type = 1,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags, dwExtraInfo = Marker } },
    };

    static void Send(params INPUT[] inputs) =>
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
}
