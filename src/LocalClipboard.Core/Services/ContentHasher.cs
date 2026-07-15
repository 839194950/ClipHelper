using System.Security.Cryptography;
using System.Text;

namespace LocalClipboard.Core.Services;

public static class ContentHasher
{
    public static string HashText(string text)
    {
        var normalizedText = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return HashBytes(Encoding.UTF8.GetBytes(normalizedText));
    }

    public static string HashBytes(ReadOnlySpan<byte> bytes)
    {
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(bytes, digest);
        return Convert.ToHexStringLower(digest);
    }
}
