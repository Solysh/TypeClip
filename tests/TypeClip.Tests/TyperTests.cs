using Xunit;

namespace TypeClip.Tests;

public class TyperTests
{
    static Settings Cfg(bool strip = true, int max = 5000) => new() { StripTrailingNewline = strip, MaxChars = max };

    [Theory]
    [InlineData("a\r\nb", "a\nb")]
    [InlineData("a\rb", "a\nb")]
    [InlineData("a\nb\n", "a\nb")]
    [InlineData("a\r\nb\r\n", "a\nb")]
    [InlineData("a\n\n", "a\n")]         // only ONE trailing newline is stripped
    [InlineData("\n", "")]
    [InlineData("", "")]
    [InlineData("a{b}(c)+d^e%f~g [h]", "a{b}(c)+d^e%f~g [h]")] // specials untouched
    public void Prepare_normalizes_and_strips(string input, string expected) =>
        Assert.Equal(expected, Typer.Prepare(input, Cfg()));

    [Fact]
    public void Prepare_keeps_trailing_newline_when_disabled() =>
        Assert.Equal("a\n", Typer.Prepare("a\r\n", Cfg(strip: false)));

    [Theory]
    [InlineData(5, 5, false)]   // exactly at the limit is allowed
    [InlineData(6, 5, true)]
    [InlineData(100000, 0, false)] // 0 = no limit
    public void ExceedsLimit_boundaries(int length, int max, bool expected) =>
        Assert.Equal(expected, Typer.ExceedsLimit(new string('x', length), Cfg(max: max)));
}
