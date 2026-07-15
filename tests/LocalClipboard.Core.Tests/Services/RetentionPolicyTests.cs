using LocalClipboard.Core.Models;
using LocalClipboard.Core.Services;

namespace LocalClipboard.Core.Tests.Services;

public sealed class RetentionPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SelectForDeletion_DeletesExpiredOrdinaryEntries_ButProtectsFavorites()
    {
        var expired = Entry("expired", Now.AddDays(-3));
        var expiredFavorite = Entry("favorite", Now.AddDays(-3), isFavorite: true);
        var current = Entry("current", Now.AddHours(-1));

        var result = RetentionPolicy.SelectForDeletion(
            [expired, expiredFavorite, current],
            Now,
            new RetentionLimits(10, TimeSpan.FromDays(1), 1_000, 1_000));

        Assert.Equal(new[] { expired.Id }, result);
    }

    [Fact]
    public void SelectForDeletion_DeletesOldestOrdinaryEntriesBeyondMaximumEntries()
    {
        var oldest = Entry("oldest", Now.AddMinutes(-3));
        var middle = Entry("middle", Now.AddMinutes(-2));
        var newest = Entry("newest", Now.AddMinutes(-1));

        var result = RetentionPolicy.SelectForDeletion(
            [oldest, middle, newest],
            Now,
            new RetentionLimits(2, TimeSpan.FromDays(30), 1_000, 1_000));

        Assert.Equal(new[] { oldest.Id }, result);
    }

    [Fact]
    public void SelectForDeletion_DeletesOldestImagesUntilImageBudgetFits()
    {
        var oldest = Entry("oldest", Now.AddMinutes(-2), imagePath: "oldest.png", encodedSize: 10);
        var newest = Entry("newest", Now.AddMinutes(-1), imagePath: "newest.png", encodedSize: 10);

        var result = RetentionPolicy.SelectForDeletion(
            [oldest, newest],
            Now,
            new RetentionLimits(10, TimeSpan.FromDays(30), 15, 1_000));

        Assert.Equal(new[] { oldest.Id }, result);
    }

    private static ClipboardEntry Entry(
        string hash,
        DateTimeOffset lastUsedAt,
        bool isFavorite = false,
        string? imagePath = null,
        long encodedSize = 0)
    {
        return new ClipboardEntry(
            Guid.NewGuid(),
            imagePath is null ? ClipboardContentType.Text : ClipboardContentType.Image,
            imagePath is null ? hash : null,
            hash,
            imagePath,
            imagePath is null ? null : imagePath + ".thumb",
            imagePath is null ? 0 : 10,
            imagePath is null ? 0 : 10,
            encodedSize,
            lastUsedAt,
            lastUsedAt,
            isFavorite);
    }
}
