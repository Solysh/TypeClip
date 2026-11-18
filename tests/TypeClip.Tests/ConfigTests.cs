using Xunit;

namespace TypeClip.Tests;

public class ConfigTests
{
    [Fact]
    public void Defaults_are_sane()
    {
        var s = Config.Normalize(new Settings());
        Assert.Equal("Ctrl+Shift+V", s.Hotkey);
        Assert.Equal(["mstsc.exe"], s.AllowedProcesses);
        Assert.Equal(20, s.CharDelayMs);
        Assert.Equal("enter", s.NewlineMode);
        Assert.Equal("unicode", s.InputMode);
        Assert.Equal(5000, s.MaxChars);
    }

    [Fact]
    public void Allowlist_is_lowercased_suffixed_and_blanks_dropped()
    {
        var s = Config.Normalize(new Settings { AllowedProcesses = ["MSTSC.EXE", " vmconnect ", "", "  "] });
        Assert.Equal(["mstsc.exe", "vmconnect.exe"], s.AllowedProcesses);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(20, 20)]
    [InlineData(99999, 1000)]
    public void Delay_is_clamped(int input, int expected) =>
        Assert.Equal(expected, Config.Normalize(new Settings { CharDelayMs = input }).CharDelayMs);

    [Theory]
    [InlineData("enter", "enter")]
    [InlineData("shiftEnter", "shiftenter")]
    [InlineData("JOIN", "join")]
    [InlineData("bogus", "enter")]
    public void NewlineMode_is_validated(string input, string expected) =>
        Assert.Equal(expected, Config.Normalize(new Settings { NewlineMode = input }).NewlineMode);

    [Theory]
    [InlineData("scancode", "scancode")]
    [InlineData("SCANCODE", "scancode")]
    [InlineData("unicode", "unicode")]
    [InlineData("bogus", "unicode")]
    public void InputMode_is_validated(string input, string expected) =>
        Assert.Equal(expected, Config.Normalize(new Settings { InputMode = input }).InputMode);

    [Fact]
    public void Negative_MaxChars_becomes_unlimited() =>
        Assert.Equal(0, Config.Normalize(new Settings { MaxChars = -1 }).MaxChars);
}
