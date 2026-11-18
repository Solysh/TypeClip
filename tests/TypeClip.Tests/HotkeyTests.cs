using Xunit;

namespace TypeClip.Tests;

public class HotkeyTests
{
    [Fact]
    public void Parses_default_hotkey()
    {
        Assert.True(Hotkey.TryParse("Ctrl+Shift+V", out var hk));
        Assert.Equal(new Hotkey(true, true, false, false, 'V'), hk);
    }

    [Theory]
    [InlineData("ctrl + alt + v", true, false, true, false, 'V')]
    [InlineData("Control+Shift+F9", true, true, false, false, 0x78)]
    [InlineData("Win+Shift+Enter", false, true, false, true, 0x0D)]
    [InlineData("Alt+1", false, false, true, false, '1')]
    public void Parses_variants(string text, bool ctrl, bool shift, bool alt, bool win, int vk)
    {
        Assert.True(Hotkey.TryParse(text, out var hk));
        Assert.Equal(new Hotkey(ctrl, shift, alt, win, vk), hk);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("V")]            // no modifier
    [InlineData("Ctrl")]         // no key
    [InlineData("Ctrl+Shift")]   // no key
    [InlineData("Ctrl+A+B")]     // two keys
    [InlineData("Ctrl+Bogus")]   // unknown key name
    [InlineData("Ctrl+999")]     // numeric enum values are not key names
    public void Rejects_invalid(string? text)
    {
        Assert.False(Hotkey.TryParse(text, out var hk));
        Assert.Equal(Hotkey.Default, hk); // falls back to the default on failure
    }
}
