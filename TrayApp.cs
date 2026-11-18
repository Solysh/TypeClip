using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace TypeClip;

sealed class TrayApp : ApplicationContext
{
    readonly NotifyIcon _icon;
    readonly ToolStripMenuItem _toggle;
    readonly ToolStripMenuItem _startup;
    readonly KeyboardHook _hook;
    readonly System.Windows.Forms.Timer _hookTimer;
    readonly Icon _iconOn = LoadIcon("typeclip.ico");
    readonly Icon _iconOff = LoadIcon("typeclip-off.ico");
    readonly Control _ui = new(); // handle gives us a way to marshal onto the UI thread
    readonly CancellationTokenSource _exitCts = new();
    CancellationTokenSource? _typeCts;
    volatile bool _busy;

    public TrayApp()
    {
        _ = _ui.Handle;
        Config.Init();

        _toggle = new ToolStripMenuItem("Disable", null, (_, _) => SetEnabled(!_hook!.Enabled));
        _startup = new ToolStripMenuItem("Start with Windows") { Checked = Startup.IsEnabled() };
        _startup.Click += (_, _) => ToggleStartup();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_toggle);
        menu.Items.Add(_startup);
        menu.Items.Add("Open config", null, (_, _) => OpenConfig());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _icon = new NotifyIcon { Icon = _iconOn, Text = "TypeClip", ContextMenuStrip = menu, Visible = true };

        Config.Error += msg => _ui.BeginInvoke(() => Balloon(msg));
        _hook = new KeyboardHook();
        _hook.Error += Balloon;
        _hook.Triggered += () => _ui.BeginInvoke(OnTrigger); // return from the hook immediately
        _hook.Aborted += () => _typeCts?.Cancel();

        // mstsc installs its own keyboard hook that can run ahead of ours; re-install ours while an allowlisted
        // window is in front so we stay first in the chain.
        _hookTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _hookTimer.Tick += (_, _) =>
        {
            if (_busy || _hook.TriggerKeyHeld) return;
            var name = Native.ProcessNameOf(Native.GetForegroundWindow());
            if (name != null && Config.Current.AllowedProcesses.Contains(name))
            {
                _hook.Reinstall();
                if (Config.Current.DebugLog) Log.Write("hook reinstalled for " + name);
            }
        };
        _hookTimer.Start();
    }

    static Icon LoadIcon(string resource)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
        return s != null ? new Icon(s) : SystemIcons.Application;
    }

    void SetEnabled(bool enabled)
    {
        _hook.Enabled = enabled;
        _toggle.Text = enabled ? "Disable" : "Enable";
        _icon.Icon = enabled ? _iconOn : _iconOff;
        _icon.Text = enabled ? "TypeClip" : "TypeClip (disabled)";
        if (!enabled) _typeCts?.Cancel(); // Disable also aborts typing in progress
    }

    void ToggleStartup()
    {
        try
        {
            Startup.Set(!_startup.Checked);
            _startup.Checked = Startup.IsEnabled();
        }
        catch (Exception ex) { Balloon("Could not change startup setting: " + ex.GetType().Name); }
    }

    void OnTrigger()
    {
        if (_busy) return;
        try
        {
            var cfg = Config.Current;
            string? raw = ReadClipboardText();
            if (string.IsNullOrEmpty(raw)) return; // empty / non-text clipboard: do nothing

            string text = Typer.Prepare(raw, cfg);
            if (text.Length == 0) return;

            if (Typer.ExceedsLimit(text, cfg))
            {
                Balloon($"Clipboard has {text.Length:N0} characters; the limit is {cfg.MaxChars:N0} (maxChars). Nothing was typed.");
                return;
            }

            _busy = true;
            _hook.Typing = true;
            _typeCts = CancellationTokenSource.CreateLinkedTokenSource(_exitCts.Token);
            var ct = _typeCts.Token;
            Task.Run(() =>
            {
                try { Typer.Type(text, cfg, () => _hook.TriggerKeyHeld, ct); }
                finally { _hook.Typing = false; _busy = false; }
            });
        }
        catch (Exception ex)
        {
            _hook.Typing = false;
            _busy = false;
            Balloon("Typing failed: " + ex.GetType().Name); // never include clipboard content
        }
    }

    static string? ReadClipboardText()
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                return Clipboard.ContainsText(TextDataFormat.UnicodeText)
                    ? Clipboard.GetText(TextDataFormat.UnicodeText)
                    : null;
            }
            catch (ExternalException) { Thread.Sleep(20); } // clipboard briefly locked by another app
        }
        return null;
    }

    void Balloon(string message) => _icon.ShowBalloonTip(4000, "TypeClip", message, ToolTipIcon.Warning);

    static void OpenConfig()
    {
        try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{Config.FilePath}\"") { UseShellExecute = true }); }
        catch { }
    }

    void ExitApp()
    {
        _exitCts.Cancel();
        _hookTimer.Stop();
        _hook.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        ExitThread();
    }
}
