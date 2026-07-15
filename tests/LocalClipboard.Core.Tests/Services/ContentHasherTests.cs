using LocalClipboard.Core.Services;

namespace LocalClipboard.Core.Tests.Services;

public sealed class ContentHasherTests
{
    [Fact]
    public void HashText_NormalizesLineEndings()
    {
        var crlfHash = ContentHasher.HashText("first\r\nsecond");
        var crHash = ContentHasher.HashText("first\rsecond");
        var lfHash = ContentHasher.HashText("first\nsecond");

        Assert.Equal(lfHash, crlfHash);
        Assert.Equal(lfHash, crHash);
    }

    [Fact]
    public void HashText_DoesNotTrimMeaningfulWhitespace()
    {
        var valueHash = ContentHasher.HashText("value");
        var paddedValueHash = ContentHasher.HashText(" value ");

        Assert.NotEqual(valueHash, paddedValueHash);
    }

    [Fact]
    public void HashBytes_ReturnsStableLowercaseHex()
    {
        byte[] bytes = [1, 2, 3];

        var firstHash = ContentHasher.HashBytes(bytes);
        var secondHash = ContentHasher.HashBytes(bytes);

        Assert.Equal(64, firstHash.Length);
        Assert.Equal(firstHash.ToLowerInvariant(), firstHash);
        Assert.Equal(firstHash, secondHash);
    }
}
