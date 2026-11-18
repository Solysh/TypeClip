# TypeClip

Press **Ctrl+Shift+V** in a Remote Desktop window and TypeClip types your clipboard text as keystrokes. It's for
RDP sessions where normal paste doesn't work. It lives in the system tray and does nothing outside allowlisted apps.

## Build

```powershell
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Output: `bin\Release\net10.0-windows\win-x64\publish\TypeClip.exe` (one self-contained file, no .NET install needed).

## Use

1. Run `TypeClip.exe`. It starts silently in the tray and creates `typeclip.json` next to the exe.
2. Copy text anywhere, focus the Remote Desktop window, press **Ctrl+Shift+V**.
3. Press **Esc** while it's typing to abort. Esc is swallowed only during typing.
4. Tray menu: **Disable/Enable** (icon turns grey; also aborts typing), **Start with Windows**, **Open config**, **Exit**.

Behavior:
- Only acts when the foreground window's process is in `allowedProcesses`. Everywhere else the hotkey passes through untouched.
- Text is sent with `SendInput`, so `{ } ( ) + ^ % ~ [ ]` and non-ASCII come out exactly as copied.
- Clipboards longer than `maxChars` (default 5000) are refused with a tray notice; nothing is typed. One trailing newline is stripped.
- There is no confirmation popup. Multi-line text into a remote shell runs line by line, so check what you copied.
- Typing stops if you switch windows mid-way.
- Clipboard contents are never logged or written anywhere.

## Config (`typeclip.json`)

Edits are picked up automatically when you save, with no restart needed.

| Key | Default | Meaning |
|---|---|---|
| `hotkey` | `"Ctrl+Shift+V"` | Modifiers (`Ctrl`, `Shift`, `Alt`, `Win`) plus one key, joined with `+` |
| `allowedProcesses` | `["mstsc.exe"]` | Exe names the hotkey is active in |
| `charDelayMs` | `20` | Delay between characters; raise for slow sessions (0-1000) |
| `newlineMode` | `"enter"` | `"enter"`, `"shiftEnter"`, or `"join"` (newlines become spaces) |
| `maxChars` | `5000` | Refuse to type longer clipboards (0 = no limit) |
| `stripTrailingNewline` | `true` | Drop one trailing newline |
| `inputMode` | `"unicode"` | `"unicode"` or `"scancode"` (see below) |

### Change the hotkey

Edit `"hotkey"`, e.g. `"Ctrl+Alt+V"` or `"Ctrl+Shift+F9"`. An invalid value shows a tray warning and keeps the
previous hotkey.

### Edit the allowlist

Add exe names (the process name in Task Manager's Details tab):

```json
"allowedProcesses": ["mstsc.exe", "vmconnect.exe"]
```

## If characters come out wrong in a session

Default `unicode` mode sends each character as a Unicode key event, which most RDP sessions accept. If the remote
side drops or garbles them, set `"inputMode": "scancode"`:

- ASCII is sent as hardware scan codes (like a physical keyboard). Anything else still goes out as Unicode.
- Scan codes are translated using the *local* keyboard layout, so it works best when local and remote layouts match.
- CapsLock on the remote side will flip letter case in this mode.

## Notes and limits

- If the target window runs **as administrator**, run TypeClip as administrator too, or Windows will block the keystrokes.
- Windows' own autocorrect (Settings > Time & language > Typing) can rewrite words typed into local apps such as
  Notepad. It doesn't affect text typed into a remote session.
- mstsc installs its own keyboard hook that can run ahead of TypeClip's, so TypeClip re-installs its hook every
  500 ms while an allowlisted window is in front. If the hotkey is ignored anyway, set `"debugLog": true` and check
  `typeclip.log` (key events and process names only, never clipboard text).
- It uses a low-level keyboard hook rather than `RegisterHotKey` so the hotkey is only swallowed in allowlisted windows.

## Development

```powershell
dotnet test tests\TypeClip.Tests      # unit tests: hotkey parsing, text prep, config validation, size guard
pwsh tests\smoke.ps1                  # end-to-end in real Notepad (close Notepad first)
pwsh tools\make-icons.ps1             # regenerate Assets\*.ico
```
