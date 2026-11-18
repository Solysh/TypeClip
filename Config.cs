using System.Text.Json;

namespace TypeClip;

sealed class Settings
{
    public string Hotkey { get; set; } = "Ctrl+Shift+V";
    public string[] AllowedProcesses { get; set; } = ["mstsc.exe"];
    public int CharDelayMs { get; set; } = 20;
    /// <summary>"enter" | "shiftEnter" | "join"</summary>
    public string NewlineMode { get; set; } = "enter";
    /// <summary>Refuse to type clipboards longer than this (0 = no limit). Esc aborts typing in progress.</summary>
    public int MaxChars { get; set; } = 5000;
    public bool StripTrailingNewline { get; set; } = true;
    /// <summary>"unicode" (KEYEVENTF_UNICODE) | "scancode" (ASCII via scan codes, Unicode for the rest)</summary>
    public string InputMode { get; set; } = "unicode";
    /// <summary>Writes typeclip.log next to the exe: hotkey events and process names only, never clipboard text.</summary>
    public bool DebugLog { get; set; } = false;
}

static class Log
{
    static readonly string Path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Environment.ProcessPath)!, "typeclip.log");
    public static void Write(string msg)
    {
        try { File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff} {msg}{Environment.NewLine}"); } catch { }
    }
}

/// <summary>typeclip.json next to the exe. Reloaded automatically when the file changes.</summary>
static class Config
{
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string FilePath { get; } =
        Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "typeclip.json");

    static Settings _current = Normalize(new Settings());
    static volatile bool _dirty = true;
    static FileSystemWatcher? _watcher;

    /// <summary>Raised (possibly off the UI thread) when the file is invalid; previous settings stay active.</summary>
    public static event Action<string>? Error;

    public static void Init()
    {
        try
        {
            if (!File.Exists(FilePath))
                File.WriteAllText(FilePath, JsonSerializer.Serialize(new Settings(), Json));
        }
        catch { /* read-only folder: run on defaults */ }

        try
        {
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(FilePath)!, Path.GetFileName(FilePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += (_, _) => _dirty = true;
            _watcher.Created += (_, _) => _dirty = true;
            _watcher.Renamed += (_, _) => _dirty = true;
        }
        catch { /* no live reload; restart to pick up edits */ }
    }

    public static Settings Current
    {
        get
        {
            if (_dirty) Reload();
            return _current;
        }
    }

    static void Reload()
    {
        _dirty = false;
        try
        {
            using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var s = JsonSerializer.Deserialize<Settings>(fs, Json) ?? new Settings();
            _current = Normalize(s);
        }
        catch (IOException) { _dirty = true; } // editor still writing; retry on next access
        catch (Exception ex) { Error?.Invoke("typeclip.json is invalid, keeping previous settings: " + ex.Message); }
    }

    internal static Settings Normalize(Settings s)
    {
        s.MaxChars = Math.Max(0, s.MaxChars);
        s.AllowedProcesses = (s.AllowedProcesses ?? [])
            .Select(p => p.Trim().ToLowerInvariant())
            .Where(p => p.Length > 0)
            .Select(p => p.EndsWith(".exe") ? p : p + ".exe")
            .ToArray();
        s.CharDelayMs = Math.Clamp(s.CharDelayMs, 0, 1000);
        if (!new[] { "enter", "shiftenter", "join" }.Contains(s.NewlineMode?.ToLowerInvariant())) s.NewlineMode = "enter";
        s.NewlineMode = s.NewlineMode!.ToLowerInvariant();
        s.InputMode = string.Equals(s.InputMode, "scancode", StringComparison.OrdinalIgnoreCase) ? "scancode" : "unicode";
        s.Hotkey ??= "Ctrl+Shift+V";
        return s;
    }
}
