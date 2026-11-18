<#
Manual end-to-end smoke test: types into a real Notepad and reads the result back. Needs a desktop session and
NO Notepad already open (it clears and then closes Notepad). Copies the exe to a temp folder so your config is untouched.
  pwsh tests\smoke.ps1 [-ExePath path\to\TypeClip.exe]
#>
param([string]$ExePath = (Join-Path $PSScriptRoot "..\bin\Release\net10.0-windows\win-x64\publish\TypeClip.exe"))

if (Get-Process notepad -ErrorAction SilentlyContinue) { "Notepad already running; close it first."; return }
$run = Join-Path ([IO.Path]::GetTempPath()) "typeclip-smoke"
Remove-Item $run -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $run | Out-Null
Copy-Item (Resolve-Path $ExePath) "$run\TypeClip.exe"

Add-Type @"
using System; using System.Runtime.InteropServices;
public class K {
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
"@
function Chord($mods, $key) {
    foreach ($m in $mods) { [K]::keybd_event($m, 0, 0, [UIntPtr]::Zero) }
    [K]::keybd_event($key, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40
    [K]::keybd_event($key, 0, 2, [UIntPtr]::Zero)
    foreach ($m in $mods) { [K]::keybd_event($m, 0, 2, [UIntPtr]::Zero) }
}
function Cfg($mode, $allowed = @("notepad.exe"), $max = 5000, $delay = 5) {
    @{ allowedProcesses = $allowed; charDelayMs = $delay; maxChars = $max; inputMode = $mode } |
        ConvertTo-Json | Set-Content "$run\typeclip.json"
    Start-Sleep -Milliseconds 800 # file watcher
}
function ReadBack { Chord @(0x11) 0x41; Start-Sleep -Milliseconds 200; Chord @(0x11) 0x43; Start-Sleep -Milliseconds 400; ((Get-Clipboard -Raw) -replace "`r`n", "\n") }
function ClearDoc { Chord @(0x11) 0x41; Start-Sleep -Milliseconds 100; Chord @() 0x2E; Start-Sleep -Milliseconds 200 }
$script:fail = 0
function Check($name, $ok, $got) { "{0,-22} {1}  got=[{2}]" -f $name, $(if ($ok) { "PASS" } else { $script:fail++; "FAIL" }), $got }

Cfg "unicode"
Start-Process "$run\TypeClip.exe"; Start-Sleep 2
Start-Process notepad; Start-Sleep 3
[K]::SetForegroundWindow((Get-Process notepad | Sort-Object StartTime | Select-Object -Last 1).MainWindowHandle) | Out-Null
Start-Sleep 1; ClearDoc

# Content cases in both input modes. (No word-ending spaces: Windows autocorrect rewrites typed words in Notepad.)
$cases = @(
    @{ name = "specials"; text = 'a{b}(c)+d^e%f~g [h]'; expect = 'a{b}(c)+d^e%f~g [h]' },
    @{ name = "two-line"; text = "line one`r`nline two`r`n"; expect = 'line one\nline two' },
    @{ name = "unicode"; text = "h$([char]0xE9)llo$([char]0x2713)$([char]0x65E5)$([char]0x672C)"; expect = "h$([char]0xE9)llo$([char]0x2713)$([char]0x65E5)$([char]0x672C)" }
)
foreach ($mode in "unicode", "scancode") {
    Cfg $mode
    foreach ($c in $cases) {
        Set-Clipboard -Value $c.text
        Chord @(0x11, 0x10) 0x56; Start-Sleep -Milliseconds (800 + 60 * $c.text.Length)
        $got = ReadBack
        Check "$mode/$($c.name)" ($got -ceq $c.expect) $got
        ClearDoc
    }
}

# Hotkey passes through when the window is not allowlisted (Notepad pastes plain text itself on Ctrl+Shift+V)
Cfg "unicode" @("mstsc.exe")
Set-Clipboard -Value "PASSTHRU"
Chord @(0x11, 0x10) 0x56; Start-Sleep 1
$got = ReadBack; Check "passthrough" ($got -ceq "PASSTHRU") $got; ClearDoc

# Size guard: over the limit -> nothing typed
Cfg "unicode" @("notepad.exe") 5
Set-Clipboard -Value "abcdefgh"
Chord @(0x11, 0x10) 0x56; Start-Sleep 1
Chord @() 0x5A; Start-Sleep -Milliseconds 300   # sentinel 'z'
$got = ReadBack; Check "size-guard" ($got -ceq "z") $got; ClearDoc

# Abort: Esc partway through a slow 100-char paste leaves it incomplete
Cfg "unicode" @("notepad.exe") 5000 40
$long = "x" * 100
Set-Clipboard -Value $long
Chord @(0x11, 0x10) 0x56; Start-Sleep -Milliseconds 1200
Chord @() 0x1B; Start-Sleep -Milliseconds 800
$got = ReadBack
Check "abort (Esc)" (($got.Length -gt 0) -and ($got.Length -lt 100)) ("{0} of 100 chars" -f $got.Length)

Get-Process TypeClip -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process notepad -ErrorAction SilentlyContinue | Stop-Process -Force
if ($script:fail) { "$script:fail FAILED"; exit 1 } else { "All passed" }
