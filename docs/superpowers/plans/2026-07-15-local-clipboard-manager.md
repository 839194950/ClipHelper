# Local Clipboard Manager Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 构建一个 Windows 10/11 x64 本地剪贴板历史工具，后台保存纯文本与图片，并通过 Alt+V 快速搜索、筛选、收藏和恢复历史内容。

**Architecture:** 使用单进程 WinForms 应用和 ApplicationContext 管理托盘生命周期；Core 项目保存领域模型与可测试规则，Infrastructure 项目封装 SQLite、图片文件、Windows API 与配置，App 项目只负责组合依赖和界面。剪贴板通知采用 Windows 消息驱动，不使用轮询；文本存 SQLite，图片存 PNG 文件并在数据库保存元数据。

**Tech Stack:** C# 14、.NET 10 LTS、WinForms、Microsoft.Data.Sqlite.Core 10.0.10、SQLitePCLRaw.bundle_winsqlite3 3.0.3、xUnit、Windows Clipboard API、RegisterHotKey、Named Mutex、Named Pipe

**Non-Goals For V1:** 自动粘贴、文件列表、HTML/富文本、OCR、标签分组、云同步、账户、遥测、应用黑名单、敏感内容识别、静态数据加密、自动更新、安装器和 ARM64 发布。

---

## Scope And File Map

实现前创建以下结构，后续任务只在标明的文件中工作：

    LocalClipboard.slnx
    Directory.Build.props
    src/
      LocalClipboard.Core/
        LocalClipboard.Core.csproj
        Models/ClipboardContentType.cs
        Models/ClipboardCapture.cs
        Models/ClipboardEntry.cs
        Models/HistoryQuery.cs
        Models/RetentionLimits.cs
        Abstractions/IHistoryRepository.cs
        Abstractions/IImageStore.cs
        Services/ContentHasher.cs
        Services/RetentionPolicy.cs
        Services/HistoryService.cs
      LocalClipboard.Infrastructure/
        LocalClipboard.Infrastructure.csproj
        Storage/SqliteHistoryRepository.cs
        Storage/SqliteSchema.cs
        Storage/PngImageStore.cs
        Settings/AppSettings.cs
        Settings/JsonSettingsStore.cs
        Windows/ClipboardReader.cs
        Windows/ClipboardMonitorWindow.cs
        Windows/GlobalHotkeyManager.cs
        Windows/SingleInstanceCoordinator.cs
        Windows/StartupManager.cs
        Diagnostics/RollingFileLogger.cs
      LocalClipboard.App/
        LocalClipboard.App.csproj
        Program.cs
        AppPaths.cs
        TrayApplicationContext.cs
        UI/PopupForm.cs
        UI/ClipboardEntryView.cs
        UI/SettingsForm.cs
        UI/ClearHistoryDialog.cs
        UI/ThemePalette.cs
    tests/
      LocalClipboard.Core.Tests/
        LocalClipboard.Core.Tests.csproj
        Services/ContentHasherTests.cs
        Services/RetentionPolicyTests.cs
        Services/HistoryServiceTests.cs
      LocalClipboard.Infrastructure.Tests/
        LocalClipboard.Infrastructure.Tests.csproj
        Storage/SqliteHistoryRepositoryTests.cs
        Storage/PngImageStoreTests.cs
        Settings/JsonSettingsStoreTests.cs
      LocalClipboard.App.IntegrationTests/
        LocalClipboard.App.IntegrationTests.csproj
        Windows/ClipboardIntegrationTests.cs
        Windows/GlobalHotkeyIntegrationTests.cs
        Windows/SingleInstanceIntegrationTests.cs
    scripts/
      publish.ps1
      verify-release.ps1
    README.md

---

### Task 1: Install Toolchain And Scaffold Solution

**Files:**
- Create: Directory.Build.props
- Create: LocalClipboard.slnx
- Create: src/LocalClipboard.Core/LocalClipboard.Core.csproj
- Create: src/LocalClipboard.Infrastructure/LocalClipboard.Infrastructure.csproj
- Create: src/LocalClipboard.App/LocalClipboard.App.csproj
- Create: tests/LocalClipboard.Core.Tests/LocalClipboard.Core.Tests.csproj
- Create: tests/LocalClipboard.Infrastructure.Tests/LocalClipboard.Infrastructure.Tests.csproj
- Create: tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj

- [ ] **Step 1: Install the required .NET 10 SDK**

Run:

    winget install --id Microsoft.DotNet.SDK.10 --exact --accept-package-agreements --accept-source-agreements
    dotnet --list-sdks

Expected: the SDK list contains one 10.0.x entry. Do not continue with only a runtime installed.

- [ ] **Step 2: Scaffold the solution and projects**

Run from the repository root:

    dotnet new sln -n LocalClipboard --format slnx
    dotnet new classlib -n LocalClipboard.Core -o src/LocalClipboard.Core -f net10.0
    dotnet new classlib -n LocalClipboard.Infrastructure -o src/LocalClipboard.Infrastructure -f net10.0-windows
    dotnet new winforms -n LocalClipboard.App -o src/LocalClipboard.App -f net10.0-windows
    dotnet new xunit -n LocalClipboard.Core.Tests -o tests/LocalClipboard.Core.Tests -f net10.0
    dotnet new xunit -n LocalClipboard.Infrastructure.Tests -o tests/LocalClipboard.Infrastructure.Tests -f net10.0-windows
    dotnet new xunit -n LocalClipboard.App.IntegrationTests -o tests/LocalClipboard.App.IntegrationTests -f net10.0-windows
    dotnet sln LocalClipboard.slnx add src/LocalClipboard.Core/LocalClipboard.Core.csproj
    dotnet sln LocalClipboard.slnx add src/LocalClipboard.Infrastructure/LocalClipboard.Infrastructure.csproj
    dotnet sln LocalClipboard.slnx add src/LocalClipboard.App/LocalClipboard.App.csproj
    dotnet sln LocalClipboard.slnx add tests/LocalClipboard.Core.Tests/LocalClipboard.Core.Tests.csproj
    dotnet sln LocalClipboard.slnx add tests/LocalClipboard.Infrastructure.Tests/LocalClipboard.Infrastructure.Tests.csproj
    dotnet sln LocalClipboard.slnx add tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj

Expected: seven projects appear in LocalClipboard.slnx.

- [ ] **Step 3: Add project references and SQLite package**

Run:

    dotnet add src/LocalClipboard.Infrastructure/LocalClipboard.Infrastructure.csproj reference src/LocalClipboard.Core/LocalClipboard.Core.csproj
    dotnet add src/LocalClipboard.App/LocalClipboard.App.csproj reference src/LocalClipboard.Core/LocalClipboard.Core.csproj src/LocalClipboard.Infrastructure/LocalClipboard.Infrastructure.csproj
    dotnet add tests/LocalClipboard.Core.Tests/LocalClipboard.Core.Tests.csproj reference src/LocalClipboard.Core/LocalClipboard.Core.csproj
    dotnet add tests/LocalClipboard.Infrastructure.Tests/LocalClipboard.Infrastructure.Tests.csproj reference src/LocalClipboard.Core/LocalClipboard.Core.csproj src/LocalClipboard.Infrastructure/LocalClipboard.Infrastructure.csproj
    dotnet add tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj reference src/LocalClipboard.Core/LocalClipboard.Core.csproj src/LocalClipboard.Infrastructure/LocalClipboard.Infrastructure.csproj
    dotnet add src/LocalClipboard.Infrastructure/LocalClipboard.Infrastructure.csproj package Microsoft.Data.Sqlite.Core --version 10.0.10
    dotnet add src/LocalClipboard.Infrastructure/LocalClipboard.Infrastructure.csproj package SQLitePCLRaw.bundle_winsqlite3 --version 3.0.3

Expected: restore succeeds with no package downgrade or audit warning. Use Microsoft.Data.Sqlite.Core with the Windows system SQLite bundle so the solution does not transitively restore vulnerable SQLitePCLRaw.lib.e_sqlite3 2.1.11 (NU1903 / GHSA-2m69-gcr7-jv3q); do not disable NuGet audit or suppress the warning.

- [ ] **Step 4: Add shared compiler settings**

Create Directory.Build.props:

    <Project>
      <PropertyGroup>
        <LangVersion>14.0</LangVersion>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
        <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
        <Deterministic>true</Deterministic>
      </PropertyGroup>
    </Project>

Delete generated Class1.cs files and the generated Form1.cs/Form1.Designer.cs files. Keep Program.cs for replacement in Task 10.

- [ ] **Step 5: Verify the empty solution builds**

Run:

    dotnet build LocalClipboard.slnx -warnaserror
    dotnet test LocalClipboard.slnx --no-build

Expected: build succeeds and the three generated test projects pass their template tests.

- [ ] **Step 6: Commit the scaffold**

Run:

    git add Directory.Build.props LocalClipboard.slnx src tests
    git commit -m "build: scaffold clipboard manager solution"

---

### Task 2: Define Core Models And Content Hashing

**Files:**
- Create: src/LocalClipboard.Core/Models/ClipboardContentType.cs
- Create: src/LocalClipboard.Core/Models/ClipboardCapture.cs
- Create: src/LocalClipboard.Core/Models/ClipboardEntry.cs
- Create: src/LocalClipboard.Core/Models/HistoryQuery.cs
- Create: src/LocalClipboard.Core/Models/RetentionLimits.cs
- Create: src/LocalClipboard.Core/Services/ContentHasher.cs
- Test: tests/LocalClipboard.Core.Tests/Services/ContentHasherTests.cs

- [ ] **Step 1: Write failing hashing tests**

Create ContentHasherTests.cs:

    using LocalClipboard.Core.Services;

    namespace LocalClipboard.Core.Tests.Services;

    public sealed class ContentHasherTests
    {
        [Fact]
        public void HashText_NormalizesLineEndings()
        {
            Assert.Equal(
                ContentHasher.HashText("first\r\nsecond"),
                ContentHasher.HashText("first\nsecond"));
        }

        [Fact]
        public void HashText_DoesNotTrimMeaningfulWhitespace()
        {
            Assert.NotEqual(
                ContentHasher.HashText("value"),
                ContentHasher.HashText(" value "));
        }

        [Fact]
        public void HashBytes_ReturnsStableLowercaseHex()
        {
            string hash = ContentHasher.HashBytes([1, 2, 3]);

            Assert.Equal(64, hash.Length);
            Assert.Equal(hash.ToLowerInvariant(), hash);
            Assert.Equal(hash, ContentHasher.HashBytes([1, 2, 3]));
        }
    }

- [ ] **Step 2: Run the hashing tests and confirm failure**

Run:

    dotnet test tests/LocalClipboard.Core.Tests/LocalClipboard.Core.Tests.csproj --filter FullyQualifiedName~ContentHasherTests

Expected: FAIL because ContentHasher does not exist.

- [ ] **Step 3: Add the models and minimal hasher**

Create ClipboardContentType.cs:

    namespace LocalClipboard.Core.Models;

    public enum ClipboardContentType
    {
        Text = 1,
        Image = 2
    }

Create ClipboardCapture.cs:

    namespace LocalClipboard.Core.Models;

    public sealed record ClipboardCapture(
        ClipboardContentType ContentType,
        string? Text,
        byte[]? PngBytes,
        int Width,
        int Height,
        DateTimeOffset CapturedAt);

Create ClipboardEntry.cs:

    namespace LocalClipboard.Core.Models;

    public sealed record ClipboardEntry(
        Guid Id,
        ClipboardContentType ContentType,
        string? TextContent,
        string ContentHash,
        string? ImagePath,
        string? ThumbnailPath,
        int Width,
        int Height,
        long EncodedSize,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastUsedAt,
        bool IsFavorite);

Create HistoryQuery.cs:

    namespace LocalClipboard.Core.Models;

    public sealed record HistoryQuery(
        string? Search = null,
        ClipboardContentType? ContentType = null,
        bool FavoritesOnly = false,
        int Limit = 100,
        int Offset = 0);

Create RetentionLimits.cs:

    namespace LocalClipboard.Core.Models;

    public sealed record RetentionLimits(
        int MaximumEntries,
        TimeSpan MaximumAge,
        long MaximumImageBytes,
        long MaximumSingleImageBytes)
    {
        public static RetentionLimits Default { get; } = new(
            MaximumEntries: 500,
            MaximumAge: TimeSpan.FromDays(30),
            MaximumImageBytes: 1_073_741_824,
            MaximumSingleImageBytes: 20_971_520);
    }

Create ContentHasher.cs:

    using System.Security.Cryptography;
    using System.Text;

    namespace LocalClipboard.Core.Services;

    public static class ContentHasher
    {
        public static string HashText(string text)
        {
            string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal);
            return HashBytes(Encoding.UTF8.GetBytes(normalized));
        }

        public static string HashBytes(ReadOnlySpan<byte> bytes)
        {
            Span<byte> digest = stackalloc byte[32];
            SHA256.HashData(bytes, digest);
            return Convert.ToHexStringLower(digest);
        }
    }

- [ ] **Step 4: Run tests and build Core**

Run:

    dotnet test tests/LocalClipboard.Core.Tests/LocalClipboard.Core.Tests.csproj --filter FullyQualifiedName~ContentHasherTests
    dotnet build src/LocalClipboard.Core/LocalClipboard.Core.csproj -warnaserror

Expected: all three hashing tests pass and Core builds without warnings.

- [ ] **Step 5: Commit core models**

Run:

    git add src/LocalClipboard.Core tests/LocalClipboard.Core.Tests/Services/ContentHasherTests.cs
    git commit -m "feat: add clipboard history core models"

---

### Task 3: Implement Retention Policy And History Service

**Files:**
- Create: src/LocalClipboard.Core/Abstractions/IHistoryRepository.cs
- Create: src/LocalClipboard.Core/Abstractions/IImageStore.cs
- Create: src/LocalClipboard.Core/Services/RetentionPolicy.cs
- Create: src/LocalClipboard.Core/Services/HistoryService.cs
- Test: tests/LocalClipboard.Core.Tests/Services/RetentionPolicyTests.cs
- Test: tests/LocalClipboard.Core.Tests/Services/HistoryServiceTests.cs

- [ ] **Step 1: Write failing retention tests**

Create RetentionPolicyTests.cs with fixed timestamps:

    using LocalClipboard.Core.Models;
    using LocalClipboard.Core.Services;

    namespace LocalClipboard.Core.Tests.Services;

    public sealed class RetentionPolicyTests
    {
        private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public void SelectForDeletion_ProtectsFavoritesAndDeletesExpiredEntries()
        {
            ClipboardEntry expired = Entry("expired", Now.AddDays(-31));
            ClipboardEntry favorite = Entry("favorite", Now.AddDays(-90), favorite: true);

            IReadOnlySet<Guid> result = RetentionPolicy.SelectForDeletion(
                [expired, favorite], Now, RetentionLimits.Default);

            Assert.Contains(expired.Id, result);
            Assert.DoesNotContain(favorite.Id, result);
        }

        [Fact]
        public void SelectForDeletion_DeletesOldestEntriesBeyondCount()
        {
            RetentionLimits limits = RetentionLimits.Default with { MaximumEntries = 2 };
            ClipboardEntry oldest = Entry("oldest", Now.AddMinutes(-3));
            ClipboardEntry middle = Entry("middle", Now.AddMinutes(-2));
            ClipboardEntry newest = Entry("newest", Now.AddMinutes(-1));

            IReadOnlySet<Guid> result = RetentionPolicy.SelectForDeletion(
                [oldest, middle, newest], Now, limits);

            Assert.Contains(oldest.Id, result);
            Assert.DoesNotContain(middle.Id, result);
            Assert.DoesNotContain(newest.Id, result);
        }

        [Fact]
        public void SelectForDeletion_DeletesOldestImagesBeyondByteLimit()
        {
            RetentionLimits limits = RetentionLimits.Default with { MaximumImageBytes = 15 };
            ClipboardEntry oldest = Entry("oldest", Now.AddMinutes(-2), size: 10);
            ClipboardEntry newest = Entry("newest", Now.AddMinutes(-1), size: 10);

            IReadOnlySet<Guid> result = RetentionPolicy.SelectForDeletion(
                [oldest, newest], Now, limits);

            Assert.Contains(oldest.Id, result);
            Assert.DoesNotContain(newest.Id, result);
        }

        private static ClipboardEntry Entry(string text, DateTimeOffset usedAt, bool favorite = false, long size = 0) =>
            new(Guid.NewGuid(), size == 0 ? ClipboardContentType.Text : ClipboardContentType.Image,
                text, text, size == 0 ? null : text + ".png", null, 0, 0, size,
                usedAt, usedAt, favorite);
    }

- [ ] **Step 2: Run retention tests and confirm failure**

Run:

    dotnet test tests/LocalClipboard.Core.Tests/LocalClipboard.Core.Tests.csproj --filter FullyQualifiedName~RetentionPolicyTests

Expected: FAIL because RetentionPolicy does not exist.

- [ ] **Step 3: Implement the pure retention selector**

Create RetentionPolicy.cs:

    using LocalClipboard.Core.Models;

    namespace LocalClipboard.Core.Services;

    public static class RetentionPolicy
    {
        public static IReadOnlySet<Guid> SelectForDeletion(
            IReadOnlyCollection<ClipboardEntry> entries,
            DateTimeOffset now,
            RetentionLimits limits)
        {
            var deletions = new HashSet<Guid>();
            List<ClipboardEntry> ordinary = entries
                .Where(entry => !entry.IsFavorite)
                .OrderBy(entry => entry.LastUsedAt)
                .ToList();

            DateTimeOffset cutoff = now - limits.MaximumAge;
            foreach (ClipboardEntry entry in ordinary.Where(entry => entry.LastUsedAt < cutoff))
            {
                deletions.Add(entry.Id);
            }

            List<ClipboardEntry> remaining = ordinary.Where(entry => !deletions.Contains(entry.Id)).ToList();
            foreach (ClipboardEntry entry in remaining.Take(Math.Max(0, remaining.Count - limits.MaximumEntries)))
            {
                deletions.Add(entry.Id);
            }

            long imageBytes = remaining
                .Where(entry => !deletions.Contains(entry.Id) && entry.ContentType == ClipboardContentType.Image)
                .Sum(entry => entry.EncodedSize);

            foreach (ClipboardEntry entry in remaining.Where(entry =>
                         !deletions.Contains(entry.Id) && entry.ContentType == ClipboardContentType.Image))
            {
                if (imageBytes <= limits.MaximumImageBytes)
                {
                    break;
                }

                deletions.Add(entry.Id);
                imageBytes -= entry.EncodedSize;
            }

            return deletions;
        }
    }

- [ ] **Step 4: Add repository and image-store contracts**

Create IHistoryRepository.cs:

    using LocalClipboard.Core.Models;

    namespace LocalClipboard.Core.Abstractions;

    public interface IHistoryRepository
    {
        Task<ClipboardEntry?> GetLatestAsync(CancellationToken cancellationToken);
        Task<IReadOnlyList<ClipboardEntry>> GetAllAsync(CancellationToken cancellationToken);
        Task<IReadOnlyList<ClipboardEntry>> QueryAsync(HistoryQuery query, CancellationToken cancellationToken);
        Task InsertAsync(ClipboardEntry entry, CancellationToken cancellationToken);
        Task TouchAsync(Guid id, DateTimeOffset usedAt, CancellationToken cancellationToken);
        Task SetFavoriteAsync(Guid id, bool isFavorite, CancellationToken cancellationToken);
        Task DeleteAsync(Guid id, CancellationToken cancellationToken);
        Task ClearAsync(bool includeFavorites, CancellationToken cancellationToken);
    }

Create IImageStore.cs:

    namespace LocalClipboard.Core.Abstractions;

    public sealed record StoredImage(
        string ImagePath,
        string ThumbnailPath,
        int Width,
        int Height,
        long EncodedSize);

    public interface IImageStore
    {
        Task<StoredImage> SaveAsync(Guid entryId, string hash, byte[] pngBytes, int width, int height, CancellationToken cancellationToken);
        Task DeleteAsync(string? imagePath, string? thumbnailPath, CancellationToken cancellationToken);
        Task DeleteOrphansAsync(IReadOnlySet<string> referencedRelativePaths, CancellationToken cancellationToken);
    }

- [ ] **Step 5: Write failing HistoryService tests using in-memory fakes**

Create HistoryServiceTests.cs. The fake repository keeps a List<ClipboardEntry> and exposes Entries. GetLatestAsync returns the greatest LastUsedAt; GetAllAsync returns oldest first; QueryAsync applies search/type/favorite/limit/offset; InsertAsync adds unless ThrowOnInsert is true; TouchAsync and SetFavoriteAsync replace the matching immutable record; DeleteAsync removes one; ClearAsync removes all or only non-favorites. The fake image store returns StoredImage($"images/{entryId:N}.png", $"thumbnails/{entryId:N}.png", width, height, pngBytes.LongLength), increments SaveCalls, and records both paths passed to DeleteAsync.

Add these exact tests:

    [Fact]
    public async Task CaptureAsync_ConsecutiveDuplicateTouchesLatestWithoutInserting()
    {
        DateTimeOffset firstTime = new(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
        var repository = new FakeHistoryRepository();
        var images = new FakeImageStore();
        var service = new HistoryService(repository, images, RetentionLimits.Default);

        await service.CaptureAsync(new(ClipboardContentType.Text, "same", null, 0, 0, firstTime), default);
        await service.CaptureAsync(new(ClipboardContentType.Text, "same", null, 0, 0, firstTime.AddMinutes(1)), default);

        Assert.Single(repository.Entries);
        Assert.Equal(firstTime.AddMinutes(1), repository.Entries[0].LastUsedAt);
    }

    [Fact]
    public async Task CaptureAsync_RejectsImageAboveSingleImageLimit()
    {
        var repository = new FakeHistoryRepository();
        var images = new FakeImageStore();
        RetentionLimits limits = RetentionLimits.Default with { MaximumSingleImageBytes = 2 };
        var service = new HistoryService(repository, images, limits);

        ClipboardEntry? result = await service.CaptureAsync(
            new(ClipboardContentType.Image, null, [1, 2, 3], 1, 1, DateTimeOffset.UtcNow), default);

        Assert.Null(result);
        Assert.Empty(repository.Entries);
        Assert.Equal(0, images.SaveCalls);
    }

    [Fact]
    public async Task CaptureAsync_DeletesFilesWhenRepositoryInsertFails()
    {
        var repository = new FakeHistoryRepository { ThrowOnInsert = true };
        var images = new FakeImageStore();
        var service = new HistoryService(repository, images, RetentionLimits.Default);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CaptureAsync(
            new(ClipboardContentType.Image, null, [1, 2, 3], 1, 1, DateTimeOffset.UtcNow), default));

        Assert.Single(images.DeletedImages);
    }

- [ ] **Step 6: Run HistoryService tests and confirm failure**

Run:

    dotnet test tests/LocalClipboard.Core.Tests/LocalClipboard.Core.Tests.csproj --filter FullyQualifiedName~HistoryServiceTests

Expected: FAIL because HistoryService does not exist.

- [ ] **Step 7: Implement HistoryService**

Create HistoryService.cs with this public API and behavior:

    using LocalClipboard.Core.Abstractions;
    using LocalClipboard.Core.Models;

    namespace LocalClipboard.Core.Services;

    public sealed class HistoryService(
        IHistoryRepository repository,
        IImageStore imageStore,
        RetentionLimits limits)
    {
        public async Task<ClipboardEntry?> CaptureAsync(
            ClipboardCapture capture,
            CancellationToken cancellationToken)
        {
            string hash = capture.ContentType switch
            {
                ClipboardContentType.Text when !string.IsNullOrEmpty(capture.Text) =>
                    ContentHasher.HashText(capture.Text),
                ClipboardContentType.Image when capture.PngBytes is { Length: > 0 } =>
                    ContentHasher.HashBytes(capture.PngBytes),
                _ => string.Empty
            };

            if (hash.Length == 0)
            {
                return null;
            }

            if (capture.PngBytes is { LongLength: var imageLength } && imageLength > limits.MaximumSingleImageBytes)
            {
                return null;
            }

            ClipboardEntry? latest = await repository.GetLatestAsync(cancellationToken);
            if (latest is not null && latest.ContentType == capture.ContentType && latest.ContentHash == hash)
            {
                await repository.TouchAsync(latest.Id, capture.CapturedAt, cancellationToken);
                return latest with { LastUsedAt = capture.CapturedAt };
            }

            Guid entryId = Guid.NewGuid();
            StoredImage? storedImage = null;
            if (capture.ContentType == ClipboardContentType.Image)
            {
                storedImage = await imageStore.SaveAsync(
                    entryId, hash, capture.PngBytes!, capture.Width, capture.Height, cancellationToken);
            }

            var entry = new ClipboardEntry(
                entryId, capture.ContentType, capture.Text, hash,
                storedImage?.ImagePath, storedImage?.ThumbnailPath,
                storedImage?.Width ?? 0, storedImage?.Height ?? 0,
                storedImage?.EncodedSize ?? 0,
                capture.CapturedAt, capture.CapturedAt, false);

            try
            {
                await repository.InsertAsync(entry, cancellationToken);
            }
            catch
            {
                if (storedImage is not null)
                {
                    await imageStore.DeleteAsync(storedImage.ImagePath, storedImage.ThumbnailPath, cancellationToken);
                }

                throw;
            }

            await EnforceRetentionAsync(capture.CapturedAt, cancellationToken);
            return entry;
        }

        public Task<IReadOnlyList<ClipboardEntry>> QueryAsync(HistoryQuery query, CancellationToken cancellationToken) =>
            repository.QueryAsync(query, cancellationToken);

        public Task SetFavoriteAsync(Guid id, bool value, CancellationToken cancellationToken) =>
            repository.SetFavoriteAsync(id, value, cancellationToken);

        public async Task DeleteAsync(ClipboardEntry entry, CancellationToken cancellationToken)
        {
            await repository.DeleteAsync(entry.Id, cancellationToken);
            await imageStore.DeleteAsync(entry.ImagePath, entry.ThumbnailPath, cancellationToken);
        }

        public Task ClearAsync(bool includeFavorites, CancellationToken cancellationToken) =>
            repository.ClearAsync(includeFavorites, cancellationToken);

        public async Task EnforceRetentionAsync(DateTimeOffset now, CancellationToken cancellationToken)
        {
            IReadOnlyList<ClipboardEntry> entries = await repository.GetAllAsync(cancellationToken);
            IReadOnlySet<Guid> deletions = RetentionPolicy.SelectForDeletion(entries, now, limits);
            foreach (ClipboardEntry entry in entries.Where(entry => deletions.Contains(entry.Id)))
            {
                await DeleteAsync(entry, cancellationToken);
            }
        }
    }

- [ ] **Step 8: Run all Core tests**

Run:

    dotnet test tests/LocalClipboard.Core.Tests/LocalClipboard.Core.Tests.csproj

Expected: ContentHasherTests, RetentionPolicyTests, and HistoryServiceTests all pass.

- [ ] **Step 9: Commit the core service**

Run:

    git add src/LocalClipboard.Core tests/LocalClipboard.Core.Tests
    git commit -m "feat: add clipboard retention and history service"

### Task 4: Implement SQLite History Repository

**Package and initialization note:** Infrastructure uses Microsoft.Data.Sqlite.Core 10.0.10 with SQLitePCLRaw.bundle_winsqlite3 3.0.3 to bind the Windows system SQLite provider instead of the vulnerable bundled SQLitePCLRaw.lib.e_sqlite3 2.1.11 native library (NU1903 / GHSA-2m69-gcr7-jv3q). Program must call SQLitePCL.Batteries_V2.Init() once before opening any SqliteConnection; this initialization is required by the bundle provider and must not be replaced by audit suppression.

**Files:**
- Create: src/LocalClipboard.Infrastructure/Storage/SqliteSchema.cs
- Create: src/LocalClipboard.Infrastructure/Storage/SqliteHistoryRepository.cs
- Test: tests/LocalClipboard.Infrastructure.Tests/Storage/SqliteHistoryRepositoryTests.cs

- [ ] **Step 1: Write failing repository tests**

Create SqliteHistoryRepositoryTests.cs. Each test creates a unique database under Path.Combine(Path.GetTempPath(), "LocalClipboardTests", Guid.NewGuid() + ".db") and deletes its parent directory in DisposeAsync.

Add these tests:

    [Fact]
    public async Task InsertAndQuery_RoundTripsTextEntry()
    {
        ClipboardEntry entry = TestEntry.Text("hello", new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero));
        await repository.InsertAsync(entry, default);

        IReadOnlyList<ClipboardEntry> result = await repository.QueryAsync(new(Search: "ell"), default);

        ClipboardEntry actual = Assert.Single(result);
        Assert.Equal(entry, actual);
    }

    [Fact]
    public async Task Query_FiltersTypeAndFavoritesAndOrdersNewestFirst()
    {
        ClipboardEntry oldFavorite = TestEntry.Image("old", usedAt: now.AddMinutes(-2), favorite: true);
        ClipboardEntry newFavorite = TestEntry.Image("new", usedAt: now.AddMinutes(-1), favorite: true);
        await repository.InsertAsync(oldFavorite, default);
        await repository.InsertAsync(newFavorite, default);

        IReadOnlyList<ClipboardEntry> result = await repository.QueryAsync(
            new(ContentType: ClipboardContentType.Image, FavoritesOnly: true), default);

        Assert.Equal([newFavorite.Id, oldFavorite.Id], result.Select(entry => entry.Id));
    }

    [Fact]
    public async Task ClearAsync_ProtectsFavoritesUnlessRequested()
    {
        ClipboardEntry ordinary = TestEntry.Text("ordinary", now);
        ClipboardEntry favorite = TestEntry.Text("favorite", now, favorite: true);
        await repository.InsertAsync(ordinary, default);
        await repository.InsertAsync(favorite, default);

        await repository.ClearAsync(includeFavorites: false, default);
        Assert.Equal(favorite.Id, Assert.Single(await repository.GetAllAsync(default)).Id);

        await repository.ClearAsync(includeFavorites: true, default);
        Assert.Empty(await repository.GetAllAsync(default));
    }

Add these additional exact assertions in separate tests:

    Assert.Equal(newest.Id, (await repository.GetLatestAsync(default))!.Id);
    await repository.TouchAsync(oldest.Id, now.AddMinutes(5), default);
    Assert.Equal(oldest.Id, (await repository.GetLatestAsync(default))!.Id);
    await repository.SetFavoriteAsync(oldest.Id, true, default);
    Assert.True((await repository.GetAllAsync(default)).Single(entry => entry.Id == oldest.Id).IsFavorite);
    await repository.DeleteAsync(oldest.Id, default);
    Assert.DoesNotContain(await repository.GetAllAsync(default), entry => entry.Id == oldest.Id);

For pagination, insert three ordered entries and assert QueryAsync(new(Limit: 1, Offset: 1)) returns only the middle entry. Define deterministic TestEntry.Text and TestEntry.Image factory methods in the same test file; each accepts content, usedAt, and optional favorite, and derives ContentHash from the supplied content.

- [ ] **Step 2: Run tests and confirm failure**

Run:

    dotnet test tests/LocalClipboard.Infrastructure.Tests/LocalClipboard.Infrastructure.Tests.csproj --filter FullyQualifiedName~SqliteHistoryRepositoryTests

Expected: FAIL because SqliteHistoryRepository does not exist.

- [ ] **Step 3: Create the schema initializer**

Create SqliteSchema.cs:

    using Microsoft.Data.Sqlite;

    namespace LocalClipboard.Infrastructure.Storage;

    internal static class SqliteSchema
    {
        public static async Task EnsureCreatedAsync(SqliteConnection connection, CancellationToken cancellationToken)
        {
            const string sql = """
                PRAGMA journal_mode = WAL;
                PRAGMA foreign_keys = ON;
                CREATE TABLE IF NOT EXISTS clipboard_entries (
                    id TEXT PRIMARY KEY,
                    content_type INTEGER NOT NULL,
                    text_content TEXT NULL,
                    content_hash TEXT NOT NULL,
                    image_path TEXT NULL,
                    thumbnail_path TEXT NULL,
                    width INTEGER NOT NULL,
                    height INTEGER NOT NULL,
                    encoded_size INTEGER NOT NULL,
                    created_at TEXT NOT NULL,
                    last_used_at TEXT NOT NULL,
                    is_favorite INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS ix_entries_last_used
                    ON clipboard_entries(last_used_at DESC);
                CREATE INDEX IF NOT EXISTS ix_entries_type_last_used
                    ON clipboard_entries(content_type, last_used_at DESC);
                CREATE INDEX IF NOT EXISTS ix_entries_favorite_last_used
                    ON clipboard_entries(is_favorite, last_used_at DESC);
                CREATE INDEX IF NOT EXISTS ix_entries_hash
                    ON clipboard_entries(content_hash);
                """;

            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

- [ ] **Step 4: Implement connection creation and row mapping**

Create SqliteHistoryRepository.cs with constructor SqliteHistoryRepository(string databasePath). The constructor creates the parent directory and stores this connection string:

    new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
        Pooling = true
    }.ToString();

Add a private OpenAsync method that opens a connection and calls SqliteSchema.EnsureCreatedAsync. Add this exact row mapper:

    private static ClipboardEntry ReadEntry(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        (ClipboardContentType)reader.GetInt32(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.GetInt32(6),
        reader.GetInt32(7),
        reader.GetInt64(8),
        DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture),
        DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture),
        reader.GetBoolean(11));

Use a single SelectedColumns constant in every SELECT so the mapper order cannot drift.

- [ ] **Step 5: Implement insert, latest, all, and touch operations**

Use parameterized SQL only. InsertAsync must execute:

    INSERT INTO clipboard_entries (
        id, content_type, text_content, content_hash, image_path, thumbnail_path,
        width, height, encoded_size, created_at, last_used_at, is_favorite)
    VALUES (
        $id, $type, $text, $hash, $image, $thumbnail,
        $width, $height, $size, $created, $used, $favorite);

Serialize timestamps with value.ToString("O", CultureInfo.InvariantCulture). GetLatestAsync orders by last_used_at DESC LIMIT 1. GetAllAsync orders oldest first because RetentionPolicy expects stable cleanup order. TouchAsync updates only last_used_at for the supplied id.

- [ ] **Step 6: Implement query, favorite, delete, and clear operations**

Build QueryAsync from fixed optional predicates rather than concatenating user text:

    WHERE ($search IS NULL OR text_content LIKE $pattern ESCAPE '\')
      AND ($type IS NULL OR content_type = $type)
      AND ($favorites = 0 OR is_favorite = 1)
    ORDER BY last_used_at DESC
    LIMIT $limit OFFSET $offset;

Escape backslash, percent, and underscore in search text before surrounding it with percent signs. Clamp Limit to 1..200 and Offset to zero or greater.

SetFavoriteAsync uses UPDATE clipboard_entries SET is_favorite = $favorite WHERE id = $id. DeleteAsync uses DELETE by id. ClearAsync chooses one of these two fixed statements:

    DELETE FROM clipboard_entries;
    DELETE FROM clipboard_entries WHERE is_favorite = 0;

- [ ] **Step 7: Run repository tests**

Run:

    dotnet test tests/LocalClipboard.Infrastructure.Tests/LocalClipboard.Infrastructure.Tests.csproj --filter FullyQualifiedName~SqliteHistoryRepositoryTests

Expected: every repository test passes. Inspect the temporary database manually once with a SQLite viewer only if a test fails; do not add viewer dependencies.

- [ ] **Step 8: Commit SQLite storage**

Run:

    git add src/LocalClipboard.Infrastructure/Storage tests/LocalClipboard.Infrastructure.Tests/Storage
    git commit -m "feat: persist clipboard history in sqlite"

---

### Task 5: Implement PNG Image And Thumbnail Storage

**Files:**
- Modify: src/LocalClipboard.Infrastructure/LocalClipboard.Infrastructure.csproj
- Create: src/LocalClipboard.Infrastructure/Storage/PngImageStore.cs
- Test: tests/LocalClipboard.Infrastructure.Tests/Storage/PngImageStoreTests.cs

- [ ] **Step 1: Enable Windows drawing support**

Add this property to LocalClipboard.Infrastructure.csproj:

    <UseWindowsForms>true</UseWindowsForms>

Do not add a cross-platform image package; the product is Windows-only and WinForms already depends on the Windows desktop runtime.

- [ ] **Step 2: Write failing image-store tests**

Create PngImageStoreTests.cs with a fresh temporary root per test. Generate valid PNG bytes with a 4x2 Bitmap and ImageFormat.Png.

Add these tests:

    [Fact]
    public async Task SaveAsync_WritesOriginalAndBoundedThumbnail()
    {
        byte[] png = CreatePng(width: 400, height: 200);

        StoredImage stored = await store.SaveAsync(Guid.NewGuid(), new string('a', 64), png, 400, 200, default);

        Assert.True(File.Exists(Path.Combine(root, stored.ImagePath)));
        Assert.True(File.Exists(Path.Combine(root, stored.ThumbnailPath)));
        using Image thumbnail = Image.FromFile(Path.Combine(root, stored.ThumbnailPath));
        Assert.True(thumbnail.Width <= 320);
        Assert.True(thumbnail.Height <= 220);
        Assert.Equal(png.LongLength, stored.EncodedSize);
    }

    [Fact]
    public async Task DeleteAsync_RemovesBothFilesAndIgnoresMissingFiles()
    {
        StoredImage stored = await store.SaveAsync(Guid.NewGuid(), new string('b', 64), CreatePng(10, 10), 10, 10, default);

        await store.DeleteAsync(stored.ImagePath, stored.ThumbnailPath, default);
        await store.DeleteAsync(stored.ImagePath, stored.ThumbnailPath, default);

        Assert.False(File.Exists(Path.Combine(root, stored.ImagePath)));
        Assert.False(File.Exists(Path.Combine(root, stored.ThumbnailPath)));
    }

    [Fact]
    public async Task DeleteOrphansAsync_DeletesOnlyUnreferencedImageFiles()
    {
        StoredImage keep = await store.SaveAsync(Guid.NewGuid(), new string('c', 64), CreatePng(10, 10), 10, 10, default);
        StoredImage remove = await store.SaveAsync(Guid.NewGuid(), new string('d', 64), CreatePng(10, 10), 10, 10, default);

        await store.DeleteOrphansAsync(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { keep.ImagePath, keep.ThumbnailPath }, default);

        Assert.True(File.Exists(Path.Combine(root, keep.ImagePath)));
        Assert.False(File.Exists(Path.Combine(root, remove.ImagePath)));
    }

- [ ] **Step 3: Run tests and confirm failure**

Run:

    dotnet test tests/LocalClipboard.Infrastructure.Tests/LocalClipboard.Infrastructure.Tests.csproj --filter FullyQualifiedName~PngImageStoreTests

Expected: FAIL because PngImageStore does not exist.

- [ ] **Step 4: Implement safe image writes and thumbnails**

Create PngImageStore.cs. Constructor receives rootPath and creates images and thumbnails directories. SaveAsync must:

1. Validate hash is exactly 64 lowercase hexadecimal characters.
2. Build a unique base name {entryId:N}-{first 12 hash characters}; this prevents non-consecutive duplicate images from sharing a deletable file.
3. Write original bytes to images/{baseName}.png through a {baseName}.tmp file.
4. Decode the PNG with Image.FromStream to reject invalid data.
5. Scale it to fit within 320x220 without enlarging.
6. Draw the thumbnail using a new 32-bit ARGB Bitmap, Graphics.InterpolationMode = HighQualityBicubic, and save it as thumbnails/{baseName}.png through a temporary file.
7. Replace existing destination files atomically with File.Move(temp, destination, overwrite: true).
8. On failure, delete temporary files and any newly created unmatched destination before rethrowing.

Use relative paths with forward slashes in StoredImage so database values remain independent of the machine path. Convert them through a private ResolveRelativePath method that rejects rooted paths and any path containing .. segments.

- [ ] **Step 5: Implement deletion and orphan cleanup**

DeleteAsync resolves each non-null relative path and calls File.Delete only when it remains under rootPath. DeleteOrphansAsync enumerates only *.png files directly under images and thumbnails, computes their normalized relative paths, and deletes files absent from referencedRelativePaths. Cancellation is checked between files.

- [ ] **Step 6: Run image-store tests and all infrastructure tests**

Run:

    dotnet test tests/LocalClipboard.Infrastructure.Tests/LocalClipboard.Infrastructure.Tests.csproj

Expected: image-store and repository tests pass without leaving files in the temporary test roots.

- [ ] **Step 7: Commit image storage**

Run:

    git add src/LocalClipboard.Infrastructure tests/LocalClipboard.Infrastructure.Tests/Storage
    git commit -m "feat: store clipboard images and thumbnails"

---

### Task 6: Persist Settings And Manage User Startup

**Files:**
- Create: src/LocalClipboard.Infrastructure/Settings/AppSettings.cs
- Create: src/LocalClipboard.Infrastructure/Settings/JsonSettingsStore.cs
- Create: src/LocalClipboard.Infrastructure/Windows/StartupManager.cs
- Test: tests/LocalClipboard.Infrastructure.Tests/Settings/JsonSettingsStoreTests.cs

- [ ] **Step 1: Write failing settings tests**

Create JsonSettingsStoreTests.cs:

    [Fact]
    public async Task LoadAsync_ReturnsDefaultsWhenFileDoesNotExist()
    {
        AppSettings settings = await store.LoadAsync(default);

        Assert.True(settings.StartWithWindows);
        Assert.Equal(HotkeyModifiers.Alt, settings.HotkeyModifiers);
        Assert.Equal(Keys.V, settings.HotkeyKey);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsSettings()
    {
        var expected = new AppSettings(false, HotkeyModifiers.Control | HotkeyModifiers.Shift, Keys.Space);

        await store.SaveAsync(expected, default);

        Assert.Equal(expected, await store.LoadAsync(default));
    }

    [Fact]
    public async Task LoadAsync_MovesInvalidJsonToRecoveryAndReturnsDefaults()
    {
        await File.WriteAllTextAsync(settingsPath, "{ invalid json");

        AppSettings settings = await store.LoadAsync(default);

        Assert.Equal(AppSettings.Default, settings);
        Assert.Single(Directory.GetFiles(recoveryDirectory, "settings-*.invalid.json"));
    }

- [ ] **Step 2: Run settings tests and confirm failure**

Run:

    dotnet test tests/LocalClipboard.Infrastructure.Tests/LocalClipboard.Infrastructure.Tests.csproj --filter FullyQualifiedName~JsonSettingsStoreTests

Expected: FAIL because AppSettings and JsonSettingsStore do not exist.

- [ ] **Step 3: Implement the settings model and atomic JSON store**

Create AppSettings.cs:

    using System.Windows.Forms;

    namespace LocalClipboard.Infrastructure.Settings;

    [Flags]
    public enum HotkeyModifiers : uint
    {
        None = 0,
        Alt = 1,
        Control = 2,
        Shift = 4,
        Windows = 8
    }

    public sealed record AppSettings(
        bool StartWithWindows,
        HotkeyModifiers HotkeyModifiers,
        Keys HotkeyKey)
    {
        public static AppSettings Default { get; } = new(true, HotkeyModifiers.Alt, Keys.V);
    }

JsonSettingsStore constructor receives settingsPath and recoveryDirectory. SaveAsync serializes with JsonSerializerOptions { WriteIndented = true }, writes settingsPath + ".tmp", then calls File.Move(temp, settingsPath, overwrite: true). LoadAsync returns AppSettings.Default when missing; on JsonException or NotSupportedException it moves the invalid file to recovery/settings-{UTC timestamp}.invalid.json and returns defaults. Do not catch UnauthorizedAccessException or IOException; callers must log and surface those failures.

- [ ] **Step 4: Implement current-user startup registration**

Create StartupManager.cs with const registry path Software\Microsoft\Windows\CurrentVersion\Run and value name LocalClipboard. Implement:

    public bool IsEnabled(string executablePath)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        string? value = key?.GetValue(ValueName) as string;
        return string.Equals(value, Quote(executablePath), StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(string executablePath, bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            key.SetValue(ValueName, Quote(executablePath), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static string Quote(string path) => $"\"{Path.GetFullPath(path)}\"";

Do not write HKLM and do not request elevation.

- [ ] **Step 5: Run settings tests and build infrastructure**

Run:

    dotnet test tests/LocalClipboard.Infrastructure.Tests/LocalClipboard.Infrastructure.Tests.csproj --filter FullyQualifiedName~JsonSettingsStoreTests
    dotnet build src/LocalClipboard.Infrastructure/LocalClipboard.Infrastructure.csproj -warnaserror

Expected: settings tests pass and the registry code compiles on net10.0-windows.

- [ ] **Step 6: Commit settings and startup management**

Run:

    git add src/LocalClipboard.Infrastructure/Settings src/LocalClipboard.Infrastructure/Windows/StartupManager.cs tests/LocalClipboard.Infrastructure.Tests/Settings
    git commit -m "feat: persist settings and manage startup"

### Task 7: Capture And Restore The Windows Clipboard

**Files:**
- Create: src/LocalClipboard.Infrastructure/Windows/ClipboardReader.cs
- Create: src/LocalClipboard.Infrastructure/Windows/ClipboardWriter.cs
- Create: src/LocalClipboard.Infrastructure/Windows/ClipboardMonitorWindow.cs
- Test: tests/LocalClipboard.App.IntegrationTests/Windows/ClipboardIntegrationTests.cs

- [ ] **Step 1: Write Windows clipboard integration tests**

Mark the test collection with CollectionDefinition(DisableParallelization = true). Run every clipboard test on an STA thread using a helper that starts a Thread, calls SetApartmentState(ApartmentState.STA), captures exceptions through TaskCompletionSource, and joins the thread.

Add these tests:

    [Fact]
    public Task ReadAsync_RetriesAndReadsText() => StaTest.RunAsync(async () =>
    {
        Clipboard.SetText("clipboard text");
        ClipboardCapture? capture = await ClipboardReader.ReadAsync(default);

        Assert.NotNull(capture);
        Assert.Equal(ClipboardContentType.Text, capture.ContentType);
        Assert.Equal("clipboard text", capture.Text);
    });

    [Fact]
    public Task ReadAsync_EncodesBitmapAsPng() => StaTest.RunAsync(async () =>
    {
        using var bitmap = new Bitmap(16, 8);
        Clipboard.SetImage(bitmap);

        ClipboardCapture? capture = await ClipboardReader.ReadAsync(default);

        Assert.NotNull(capture?.PngBytes);
        Assert.Equal(16, capture.Width);
        Assert.Equal(8, capture.Height);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], capture.PngBytes![..4]);
    });

    [Fact]
    public Task Writer_RestoresTextAndSuppressesOneNotification() => StaTest.RunAsync(() =>
    {
        var gate = new FakeSuppressionGate();
        var writer = new ClipboardWriter(gate);

        writer.Write(CreateTextEntry("restored"), imageRoot: string.Empty);

        Assert.Equal("restored", Clipboard.GetText());
        Assert.Equal(1, gate.Suppressions);
        Assert.Equal(0, gate.Cancellations);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Writer_CancelsSuppressionWhenWriteFails() => StaTest.RunAsync(() =>
    {
        var gate = new FakeSuppressionGate();
        var writer = new ClipboardWriter(gate);
        ClipboardEntry missingImage = CreateImageEntry("images/missing.png");

        Assert.ThrowsAny<IOException>(() => writer.Write(missingImage, testRoot));
        Assert.Equal(1, gate.Suppressions);
        Assert.Equal(1, gate.Cancellations);
        return Task.CompletedTask;
    });

    private sealed class FakeSuppressionGate : IClipboardSuppressionGate
    {
        public int Suppressions { get; private set; }
        public int Cancellations { get; private set; }
        public void SuppressNextNotification() => Suppressions++;
        public void CancelSuppression() => Cancellations++;
    }

Define CreateTextEntry and CreateImageEntry as private helpers in ClipboardIntegrationTests.cs; construct complete ClipboardEntry records directly so this test does not depend on helpers from another test project.

- [ ] **Step 2: Run integration tests and confirm failure**

Run:

    dotnet test tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj --filter FullyQualifiedName~ClipboardIntegrationTests

Expected: FAIL because the clipboard classes do not exist. If the machine session cannot access an interactive clipboard, the test must report a clear Skip reason rather than hang.

- [ ] **Step 3: Implement clipboard reading with bounded retry**

Create ClipboardReader.cs as a static class. ReadAsync must verify Thread.CurrentThread.GetApartmentState() == ApartmentState.STA, then attempt five reads with delays of 20, 40, 80, and 160 milliseconds after ExternalException. On each attempt:

1. If Clipboard.ContainsImage(), clone Clipboard.GetImage(), encode it to PNG in a MemoryStream, and return ClipboardCapture(Image, null, bytes, width, height, UtcNow).
2. Else if Clipboard.ContainsText(TextDataFormat.UnicodeText), return ClipboardCapture(Text, Clipboard.GetText(UnicodeText), null, 0, 0, UtcNow).
3. Else return null.

After the fifth ExternalException, return null. Allow cancellation during delays. Dispose every Image and MemoryStream deterministically.

- [ ] **Step 4: Implement clipboard restoration**

Create ClipboardWriter.cs:

    using LocalClipboard.Core.Models;

    namespace LocalClipboard.Infrastructure.Windows;

    public interface IClipboardSuppressionGate
    {
        void SuppressNextNotification();
        void CancelSuppression();
    }

    public sealed class ClipboardWriter(IClipboardSuppressionGate suppressionGate)
    {
        public void Write(ClipboardEntry entry, string imageRoot)
        {
            suppressionGate.SuppressNextNotification();
            try
            {
                if (entry.ContentType == ClipboardContentType.Text)
                {
                    Clipboard.SetText(entry.TextContent ?? string.Empty, TextDataFormat.UnicodeText);
                    return;
                }

                string imagePath = Path.GetFullPath(Path.Combine(imageRoot, entry.ImagePath!));
                using Image image = Image.FromFile(imagePath);
                using var clone = new Bitmap(image);
                Clipboard.SetImage(clone);
            }
            catch
            {
                suppressionGate.CancelSuppression();
                throw;
            }
        }
    }

- [ ] **Step 5: Implement the hidden clipboard message window**

Create ClipboardMonitorWindow.cs as a sealed partial class deriving NativeWindow and implementing IDisposable plus IClipboardSuppressionGate. Constructor creates a message-only handle, calls AddClipboardFormatListener, and accepts Func<CancellationToken, Task> onClipboardChanged. Override WndProc:

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmClipboardUpdate)
        {
            if (Interlocked.Exchange(ref suppressNext, 0) == 0)
            {
                _ = NotifyChangedAsync();
            }
        }

        base.WndProc(ref message);
    }

Expose SuppressNextNotification() as Interlocked.Exchange(ref suppressNext, 1) and CancelSuppression() as Interlocked.Exchange(ref suppressNext, 0). Serialize NotifyChangedAsync with SemaphoreSlim(1, 1) so rapid updates cannot run multiple clipboard reads concurrently. Dispose removes the listener, releases the handle, and disposes the semaphore.

Use these P/Invokes with LibraryImport("user32.dll"):

    private static partial bool AddClipboardFormatListener(nint hwnd);
    private static partial bool RemoveClipboardFormatListener(nint hwnd);

Set WmClipboardUpdate to 0x031D. Throw Win32Exception when listener registration fails.

- [ ] **Step 6: Run clipboard integration tests**

Run from an interactive Windows desktop session:

    dotnet test tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj --filter FullyQualifiedName~ClipboardIntegrationTests

Expected: text, image, writer, and one-shot suppression tests pass.

- [ ] **Step 7: Commit clipboard integration**

Run:

    git add src/LocalClipboard.Infrastructure/Windows tests/LocalClipboard.App.IntegrationTests/Windows
    git commit -m "feat: monitor and restore windows clipboard"

---

### Task 8: Register Global Hotkey And Coordinate A Single Instance

**Files:**
- Create: src/LocalClipboard.Infrastructure/Windows/GlobalHotkeyManager.cs
- Create: src/LocalClipboard.Infrastructure/Windows/SingleInstanceCoordinator.cs
- Test: tests/LocalClipboard.App.IntegrationTests/Windows/GlobalHotkeyIntegrationTests.cs
- Test: tests/LocalClipboard.App.IntegrationTests/Windows/SingleInstanceIntegrationTests.cs

- [ ] **Step 1: Write failing hotkey tests**

Create GlobalHotkeyIntegrationTests.cs on an STA thread:

    [Fact]
    public Task Register_RejectsACombinationAlreadyOwnedByAnotherWindow() => StaTest.RunAsync(() =>
    {
        using var first = new GlobalHotkeyManager();
        using var second = new GlobalHotkeyManager();
        first.Register(HotkeyModifiers.Control | HotkeyModifiers.Shift, Keys.F12);

        Assert.Throws<InvalidOperationException>(() =>
            second.Register(HotkeyModifiers.Control | HotkeyModifiers.Shift, Keys.F12));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Unregister_AllowsTheCombinationToBeRegisteredAgain() => StaTest.RunAsync(() =>
    {
        using var first = new GlobalHotkeyManager();
        first.Register(HotkeyModifiers.Control | HotkeyModifiers.Shift, Keys.F11);
        first.Unregister();

        using var second = new GlobalHotkeyManager();
        second.Register(HotkeyModifiers.Control | HotkeyModifiers.Shift, Keys.F11);
        return Task.CompletedTask;
    });

- [ ] **Step 2: Implement the hotkey message window**

Create GlobalHotkeyManager.cs deriving NativeWindow and implementing IDisposable. Create a message-only handle in the constructor. Register(modifiers, key) first unregisters the old combination, calls RegisterHotKey(handle, id: 1, (uint)modifiers, (uint)key), and throws InvalidOperationException with the Win32 error code when registration fails. WndProc invokes a HotkeyPressed event when message.Msg == 0x0312 and message.WParam == 1. Unregister calls UnregisterHotKey only when registered. Dispose unregisters and releases the handle.

- [ ] **Step 3: Run hotkey tests**

Run:

    dotnet test tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj --filter FullyQualifiedName~GlobalHotkeyIntegrationTests

Expected: both hotkey ownership tests pass. Use F11/F12 in tests to avoid the product default Alt+V and reduce conflicts.

- [ ] **Step 4: Write failing single-instance tests**

Create SingleInstanceIntegrationTests.cs:

    [Fact]
    public async Task SecondCoordinatorSignalsPrimaryToShowWindow()
    {
        string name = "LocalClipboard.Tests." + Guid.NewGuid().ToString("N");
        await using var primary = new SingleInstanceCoordinator(name);
        await using var secondary = new SingleInstanceCoordinator(name);
        Assert.True(primary.TryAcquirePrimary());
        Assert.False(secondary.TryAcquirePrimary());

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task<string> message = primary.WaitForMessageAsync(timeout.Token);
        await secondary.SendShowMessageAsync(timeout.Token);

        Assert.Equal("show", await message);
    }

- [ ] **Step 5: Implement mutex and named-pipe coordination**

Create SingleInstanceCoordinator.cs implementing IAsyncDisposable:

- Constructor receives a base name and derives mutex name Local\{name} and pipe name {name}.pipe.
- TryAcquirePrimary creates Mutex(initiallyOwned: true, mutexName, out bool createdNew), stores it only when createdNew, and returns createdNew.
- WaitForMessageAsync creates one NamedPipeServerStream with PipeDirection.In, maximum one server, PipeTransmissionMode.Byte, PipeOptions.Asynchronous; it waits for a connection and reads one UTF-8 line with StreamReader.
- SendShowMessageAsync connects a NamedPipeClientStream with a two-second linked timeout and writes the exact line show with AutoFlush enabled.
- DisposeAsync releases and disposes the owned mutex and any active pipe.

The production name must include the current user SID with backslashes replaced by underscores, preventing sessions for different Windows users from colliding.

- [ ] **Step 6: Run single-instance tests**

Run:

    dotnet test tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj --filter FullyQualifiedName~SingleInstanceIntegrationTests

Expected: the primary receives show from the secondary within five seconds.

- [ ] **Step 7: Commit Windows activation infrastructure**

Run:

    git add src/LocalClipboard.Infrastructure/Windows tests/LocalClipboard.App.IntegrationTests/Windows
    git commit -m "feat: add global hotkey and single instance activation"

---

### Task 9: Add Bounded Diagnostics And Recovery Paths

**Files:**
- Create: src/LocalClipboard.Infrastructure/Diagnostics/RollingFileLogger.cs
- Create: src/LocalClipboard.App/AppPaths.cs
- Test: tests/LocalClipboard.Infrastructure.Tests/Diagnostics/RollingFileLoggerTests.cs

- [ ] **Step 1: Write failing logger tests**

Create RollingFileLoggerTests.cs:

    [Fact]
    public async Task WriteAsync_DoesNotPersistClipboardContent()
    {
        var logger = new RollingFileLogger(logDirectory, maximumBytes: 1024);
        await logger.WriteAsync("clipboard_read_failed", new InvalidOperationException("secret clipboard text"), default);

        string log = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(logDirectory)));
        Assert.Contains("clipboard_read_failed", log);
        Assert.Contains(nameof(InvalidOperationException), log);
        Assert.DoesNotContain("secret clipboard text", log);
    }

    [Fact]
    public async Task WriteAsync_RotatesBeforeDirectoryExceedsLimit()
    {
        var logger = new RollingFileLogger(logDirectory, maximumBytes: 300);
        for (int index = 0; index < 20; index++)
        {
            await logger.WriteAsync("event_" + index, new InvalidOperationException(), default);
        }

        long total = Directory.GetFiles(logDirectory).Sum(path => new FileInfo(path).Length);
        Assert.True(total <= 600);
    }

- [ ] **Step 2: Implement content-safe rolling logs**

RollingFileLogger constructor receives logDirectory and maximumBytes, creates the directory, and writes daily files named localclipboard-yyyyMMdd.log. WriteAsync appends one JSON line with timestamp, eventName, exception type, stack trace, and HResult. It must never serialize Exception.Message, Data, inner exception messages, or caller-supplied clipboard values.

Before append, delete oldest log files until existing total size is at most maximumBytes. After append, if total is above maximumBytes, keep the newest file and delete older files until total is at most maximumBytes * 2. Serialize writes through SemaphoreSlim.

- [ ] **Step 3: Add canonical application paths**

Create AppPaths.cs:

    namespace LocalClipboard.App;

    internal sealed record AppPaths(
        string Root,
        string Database,
        string Settings,
        string ImagesRoot,
        string Logs,
        string Recovery)
    {
        public static AppPaths CreateDefault()
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LocalClipboard");
            Directory.CreateDirectory(root);
            return new(root, Path.Combine(root, "history.db"), Path.Combine(root, "settings.json"),
                root, Path.Combine(root, "logs"), Path.Combine(root, "recovery"));
        }
    }

- [ ] **Step 4: Define database recovery behavior**

Add an internal Program-level factory in Task 12 that opens SqliteHistoryRepository and runs GetLatestAsync once. Catch only SqliteException indicating malformed/corrupt database. Move history.db, history.db-wal, and history.db-shm when present into recovery with the same UTC timestamp suffix, then construct a fresh repository. Other IO and permission errors must remain fatal and be shown to the user without deleting files.

- [ ] **Step 5: Run diagnostics tests**

Run:

    dotnet test tests/LocalClipboard.Infrastructure.Tests/LocalClipboard.Infrastructure.Tests.csproj --filter FullyQualifiedName~RollingFileLoggerTests

Expected: logs omit exception messages and remain bounded.

- [ ] **Step 6: Commit diagnostics**

Run:

    git add src/LocalClipboard.Infrastructure/Diagnostics src/LocalClipboard.App/AppPaths.cs tests/LocalClipboard.Infrastructure.Tests/Diagnostics
    git commit -m "feat: add bounded local diagnostics"

### Task 10: Build The Unified Timeline Popup

**Files:**
- Create: src/LocalClipboard.App/UI/ThemePalette.cs
- Create: src/LocalClipboard.App/UI/ClipboardEntryView.cs
- Create: src/LocalClipboard.App/UI/PopupQueryState.cs
- Create: src/LocalClipboard.App/UI/PopupForm.cs
- Modify: tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj
- Test: tests/LocalClipboard.App.IntegrationTests/UI/PopupFormTests.cs

- [ ] **Step 1: Reference the App project from UI integration tests**

Run:

    dotnet add tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj reference src/LocalClipboard.App/LocalClipboard.App.csproj

Add InternalsVisibleTo("LocalClipboard.App.IntegrationTests") in src/LocalClipboard.App/Properties/AssemblyInfo.cs so tests can instantiate internal forms without making product UI public.

- [ ] **Step 2: Write failing query-state and window tests**

Create PopupFormTests.cs on an STA thread:

    [Fact]
    public void QueryState_BuildsFavoritesImageQuery()
    {
        var state = new PopupQueryState("screen", PopupFilter.Favorites, offset: 100);

        HistoryQuery query = state.ToHistoryQuery();

        Assert.Equal("screen", query.Search);
        Assert.True(query.FavoritesOnly);
        Assert.Null(query.ContentType);
        Assert.Equal(100, query.Offset);
        Assert.Equal(100, query.Limit);
    }

    [Fact]
    public Task PopupForm_UsesApprovedWindowBehavior() => StaTest.RunAsync(() =>
    {
        using PopupForm form = PopupForm.CreateForTest();

        Assert.Equal(FormBorderStyle.None, form.FormBorderStyle);
        Assert.False(form.ShowInTaskbar);
        Assert.Equal(new Size(560, 620), form.ClientSize);
        Assert.True(form.KeyPreview);
        return Task.CompletedTask;
    });

- [ ] **Step 3: Implement query state**

Create PopupQueryState.cs:

    using LocalClipboard.Core.Models;

    namespace LocalClipboard.App.UI;

    internal enum PopupFilter { All, Text, Images, Favorites }

    internal sealed record PopupQueryState(string? Search, PopupFilter Filter, int Offset = 0)
    {
        public HistoryQuery ToHistoryQuery() => new(
            Search: string.IsNullOrWhiteSpace(Search) ? null : Search,
            ContentType: Filter switch
            {
                PopupFilter.Text => ClipboardContentType.Text,
                PopupFilter.Images => ClipboardContentType.Image,
                _ => null
            },
            FavoritesOnly: Filter == PopupFilter.Favorites,
            Limit: 100,
            Offset: Math.Max(0, Offset));
    }

- [ ] **Step 4: Implement theme palette and entry view model**

ThemePalette reads SystemInformation.HighContrast and the Windows AppsUseLightTheme registry value. Expose Background, Surface, Border, PrimaryText, SecondaryText, Accent, and Selection colors. If registry access fails, use the light palette. Do not force the entire process into a theme unsupported by WinForms.

ClipboardEntryView wraps ClipboardEntry plus optional Image Thumbnail. It implements IDisposable and disposes only the thumbnail instance it owns. DisplayText returns the first 180 text characters with line breaks collapsed, or a string such as “1920 × 1080 image” for image entries.

- [ ] **Step 5: Implement PopupForm layout**

Construct the form in code, not a designer:

- ClientSize 560x620, FormBorderStyle.None, ShowInTaskbar false, KeyPreview true, AutoScaleMode.Dpi.
- A 48-pixel top panel containing a borderless TextBox with placeholder “搜索剪贴板历史…”.
- A 38-pixel filter panel containing four flat buttons: 全部, 文本, 图片, 收藏.
- An owner-drawn ListBox filling the remaining area with IntegralHeight false and ItemHeight 78.
- A 28-pixel footer showing “↑↓ 选择  Enter 恢复  Delete 删除  Esc 关闭”.

The owner-draw handler renders time, text summary or thumbnail, dimensions, and a star glyph. It must never call Image.FromFile inside OnDrawItem; thumbnails are loaded before items are added and disposed when results are replaced.

- [ ] **Step 6: Implement search, filtering, pagination, and keyboard behavior**

PopupForm constructor receives imageRoot plus these delegates:

    Func<HistoryQuery, CancellationToken, Task<IReadOnlyList<ClipboardEntry>>> queryEntries
    Func<ClipboardEntry, CancellationToken, Task> deleteEntry
    Func<Guid, bool, CancellationToken, Task> setFavorite
    Func<ClipboardEntry, Task> activateEntry

CreateForTest supplies delegates returning an empty list or completed tasks. Use a WinForms Timer with Interval 150 to debounce search. RefreshAsync cancels the previous query with CancellationTokenSource, calls queryEntries(state.ToHistoryQuery()), loads thumbnails only for returned image entries, replaces list items, and selects index zero when present.

When the list scrolls within five items of the end and the previous query returned 100 items, request the next page once and append it. Reset offset to zero when search or filter changes.

Handle keys exactly:

- Enter: invoke activateEntry for the selected entry, then Hide().
- Delete: call deleteEntry for the selected entry, dispose its view, remove it, and preserve neighboring selection.
- Escape: Hide().
- Up/Down: allow ListBox default navigation even while search has focus by forwarding the key.

Handle a mouse click inside the star hit rectangle separately: call setFavorite(entry.Id, !entry.IsFavorite), update the local record with the returned state assumption, invalidate that row, and do not activate or close the popup.

On Deactivate, call Hide unless a child confirmation dialog is open. On every ShowPopup call, position the form in the mouse screen working area at horizontal center and 18 percent from the top, clamp to bounds, show, activate, focus the search box, and refresh current results.

- [ ] **Step 7: Run popup tests and manually inspect layout**

Run:

    dotnet test tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj --filter FullyQualifiedName~PopupFormTests

Expected: query and window behavior tests pass. Defer real visual and DPI inspection until Task 12, when the actual tray application can open PopupForm with captured clipboard data.

- [ ] **Step 8: Commit the popup UI**

Run:

    git add src/LocalClipboard.App/UI tests/LocalClipboard.App.IntegrationTests
    git commit -m "feat: add unified clipboard timeline popup"

---

### Task 11: Add Settings And Clear-History Dialogs

**Files:**
- Create: src/LocalClipboard.App/UI/SettingsForm.cs
- Create: src/LocalClipboard.App/UI/ClearHistoryDialog.cs
- Test: tests/LocalClipboard.App.IntegrationTests/UI/SettingsFormTests.cs

- [ ] **Step 1: Write failing dialog tests**

Create SettingsFormTests.cs on an STA thread:

    [Fact]
    public Task SettingsForm_ShowsDefaultHotkeyAndStartupState() => StaTest.RunAsync(() =>
    {
        using SettingsForm form = SettingsForm.CreateForTest(AppSettings.Default, cacheBytes: 25 * 1024 * 1024);

        Assert.True(form.StartWithWindowsChecked);
        Assert.Equal("Alt + V", form.HotkeyDisplayText);
        Assert.Contains("25", form.CacheUsageText);
        return Task.CompletedTask;
    });

    [Fact]
    public Task ClearDialog_DefaultsToProtectFavorites() => StaTest.RunAsync(() =>
    {
        using var dialog = new ClearHistoryDialog();

        Assert.False(dialog.IncludeFavorites);
        return Task.CompletedTask;
    });

- [ ] **Step 2: Implement the settings form**

SettingsForm constructor receives current AppSettings, cache bytes, data directory, Func<AppSettings, Task<bool>> saveSettings, and Action openDataDirectory. Build a fixed 460x360 dialog with:

- 开机启动 CheckBox.
- Read-only hotkey TextBox that captures KeyDown and formats modifiers plus one non-modifier key.
- Validation label for “快捷键已被其他程序占用”.
- Read-only labels for “最多 500 条 / 30 天” and current cache usage.
- “打开数据目录”, “保存”, and “取消” buttons.

Reject modifier-only combinations, Win+V, and keys without Alt/Control/Shift/Windows. On Save, call saveSettings; close only when it returns true. CreateForTest uses no-op delegates and exposes internal read-only properties used by tests.

- [ ] **Step 3: Implement the clear-history confirmation**

ClearHistoryDialog is a fixed 390x190 modal dialog. Text states that ordinary history will be deleted permanently. Include an unchecked “同时删除收藏内容” CheckBox, destructive “清空” button, and “取消” button. Expose IncludeFavorites only after DialogResult.OK.

- [ ] **Step 4: Run dialog tests and manual keyboard checks**

Run:

    dotnet test tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj --filter FullyQualifiedName~SettingsFormTests

Expected: tests pass. Manually verify Tab order, Enter on Save, Escape on Cancel, and no accidental clearing when the dialog closes via the title bar.

- [ ] **Step 5: Commit settings UI**

Run:

    git add src/LocalClipboard.App/UI tests/LocalClipboard.App.IntegrationTests/UI
    git commit -m "feat: add clipboard settings and clear dialogs"

---

### Task 12: Compose The Tray Application Lifecycle

**Files:**
- Replace: src/LocalClipboard.App/Program.cs
- Create: src/LocalClipboard.App/TrayApplicationContext.cs
- Modify: src/LocalClipboard.Core/Services/HistoryService.cs
- Modify: src/LocalClipboard.Infrastructure/Windows/ClipboardMonitorWindow.cs
- Test: tests/LocalClipboard.Core.Tests/Services/HistoryServiceTests.cs

- [ ] **Step 1: Add failing service tests for restore and clear behavior**

Add to HistoryServiceTests.cs:

    [Fact]
    public async Task MarkUsedAsync_UpdatesLastUsedTime()
    {
        var repository = new FakeHistoryRepository();
        var service = new HistoryService(repository, new FakeImageStore(), RetentionLimits.Default);
        ClipboardEntry entry = await service.CaptureAsync(TextCapture("value", now), default) ?? throw new XunitException();

        await service.MarkUsedAsync(entry.Id, now.AddMinutes(1), default);

        Assert.Equal(now.AddMinutes(1), repository.Entries.Single().LastUsedAt);
    }

    [Fact]
    public async Task ClearAsync_DeletesImageFilesAndProtectsFavorites()
    {
        var repository = new FakeHistoryRepository();
        var images = new FakeImageStore();
        var service = new HistoryService(repository, images, RetentionLimits.Default);
        ClipboardEntry ordinaryImage = await service.CaptureAsync(ImageCapture([1], now), default) ?? throw new XunitException();
        ClipboardEntry favoriteText = await service.CaptureAsync(TextCapture("favorite", now.AddSeconds(1)), default) ?? throw new XunitException();
        await service.SetFavoriteAsync(favoriteText.Id, true, default);

        await service.ClearAsync(includeFavorites: false, default);

        Assert.Equal(favoriteText.Id, Assert.Single(repository.Entries).Id);
        Assert.Contains(images.DeletedImages, item => item.ImagePath == ordinaryImage.ImagePath);
    }

- [ ] **Step 2: Fix HistoryService clear and restore semantics**

Add:

    public Task MarkUsedAsync(Guid id, DateTimeOffset usedAt, CancellationToken cancellationToken) =>
        repository.TouchAsync(id, usedAt, cancellationToken);

Replace ClearAsync with an implementation that loads all entries, filters to all entries when includeFavorites is true or only non-favorites otherwise, and calls DeleteAsync for each selected entry. Do not call repository.ClearAsync from HistoryService because that would leave image files behind.

- [ ] **Step 3: Add pause state to the clipboard monitor**

Add public bool IsPaused { get; set; } to ClipboardMonitorWindow. WndProc must skip updates when IsPaused is true, without consuming suppressNext. Pausing does not unregister the listener, so resuming is immediate.

- [ ] **Step 4: Implement TrayApplicationContext**

Constructor receives all concrete dependencies and creates:

- NotifyIcon with visible icon and tooltip “Local Clipboard”.
- Context menu items: 打开历史, 暂停监听, 设置, 清空历史, 退出.
- ClipboardMonitorWindow callback that calls ClipboardReader.ReadAsync then HistoryService.CaptureAsync. Before capture, if PNG bytes exceed RetentionLimits.Default.MaximumSingleImageBytes, skip the service call and show one non-repeating tray notification for the current process.
- GlobalHotkeyManager registered from AppSettings.
- PopupForm activation callback that calls ClipboardWriter.Write, HistoryService.MarkUsedAsync(UtcNow), then refreshes the popup on next open.

Double-clicking the tray icon and clicking 打开历史 call ShowPopup. 暂停监听 toggles monitor.IsPaused, changes menu text to 恢复监听, and swaps to a paused icon. 清空历史 shows ClearHistoryDialog and calls service.ClearAsync(dialog.IncludeFavorites). 退出 hides and disposes NotifyIcon before ExitThread.

Settings save behavior must register the new hotkey before persisting it. If registration fails, restore the previous hotkey and return false. If the startup flag changed, call StartupManager.SetEnabled(Environment.ProcessPath!, value). Save settings atomically only after both operations succeed.

Catch background callback exceptions at the context boundary and send only event names plus exception types to RollingFileLogger configured with maximumBytes: 5_242_880. Show tray notifications only for oversized images, recovered database, and settings/hotkey actions requiring user intervention.

- [ ] **Step 5: Implement Program startup and secondary-instance exit**

Program.Main must be marked STAThread and perform this order:

1. ApplicationConfiguration.Initialize().
2. Call SQLitePCL.Batteries_V2.Init() before any code can open a SqliteConnection; SQLitePCLRaw.bundle_winsqlite3 requires this provider initialization.
3. Create AppPaths and RollingFileLogger.
4. Build the per-user SingleInstanceCoordinator name from WindowsIdentity.GetCurrent().User.Value.
5. If TryAcquirePrimary is false, call SendShowMessageAsync and return.
6. Load settings; if StartWithWindows is true, refresh the current-user startup path.
7. Open SQLite repository. Treat SQLite primary error codes 11 (CORRUPT) and 26 (NOTADB) as confirmed corruption, move database sidecar files into recovery, and retry once.
8. Create PngImageStore and delete orphan files using paths referenced by repository.GetAllAsync.
9. Create HistoryService and run EnforceRetentionAsync(UtcNow).
10. Create TrayApplicationContext and start a named-pipe listener loop that calls ShowPopup for each show message.
11. Call Application.Run(context).

Wrap fatal startup failures in one MessageBox with the data directory and a concise error category. Log the exception type and stack, then exit non-zero without deleting user data.

- [ ] **Step 6: Verify the composed application manually**

Run:

    dotnet run --project src/LocalClipboard.App/LocalClipboard.App.csproj

Verify in order:

1. No main window appears and the tray icon is visible.
2. Copy text and an image, then press Alt+V; both appear newest first.
3. Enter restores selected text and does not create a duplicate history item.
4. Pause prevents new captures and resume restores capture.
5. Starting a second process opens the existing popup and exits.
6. Changing the hotkey updates registration; a conflicting hotkey keeps the old value.
7. Clear protects favorites unless the checkbox is selected.
8. Exit removes the tray icon immediately.

- [ ] **Step 7: Run all automated tests**

Run:

    dotnet test LocalClipboard.slnx

Expected: all Core, Infrastructure, and Windows integration tests pass from an interactive session.

- [ ] **Step 8: Commit the runnable application**

Run:

    git add src tests
    git commit -m "feat: compose tray clipboard application"

### Task 13: Configure Single-File Publishing And Release Verification

**Files:**
- Modify: src/LocalClipboard.App/LocalClipboard.App.csproj
- Modify: src/LocalClipboard.App/Program.cs
- Create: scripts/publish.ps1
- Create: scripts/verify-release.ps1

- [ ] **Step 1: Add release properties to the App project**

Add this Release-only property group to LocalClipboard.App.csproj:

    <PropertyGroup Condition="'$(Configuration)' == 'Release'">
      <RuntimeIdentifier>win-x64</RuntimeIdentifier>
      <SelfContained>true</SelfContained>
      <PublishSingleFile>true</PublishSingleFile>
      <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
      <PublishTrimmed>false</PublishTrimmed>
      <DebugType>none</DebugType>
      <DebugSymbols>false</DebugSymbols>
      <AssemblyName>LocalClipboard</AssemblyName>
      <Product>Local Clipboard</Product>
      <Version>0.1.0</Version>
    </PropertyGroup>

Keep trimming disabled because WinForms and reflection-based JSON serialization require an explicit trim audit before enabling it.

- [ ] **Step 2: Add a release smoke-test argument**

Before single-instance acquisition in Program.Main, recognize exact argument --smoke-test. In this mode:

1. Create a unique temporary root.
2. Initialize SqliteHistoryRepository, PngImageStore, and JsonSettingsStore.
3. Insert and query one text entry through HistoryService.
4. Save and reload AppSettings.Default.
5. Delete the temporary root.
6. Return exit code 0.

Do not register a tray icon, clipboard listener, startup entry, or global hotkey in smoke-test mode. Return exit code 1 after writing a sanitized exception type to stderr if the smoke test fails.

- [ ] **Step 3: Create the publish script**

Create scripts/publish.ps1:

    [CmdletBinding()]
    param(
        [string]$Configuration = 'Release',
        [string]$Output = (Join-Path $PSScriptRoot '..\artifacts\publish\win-x64')
    )

    $ErrorActionPreference = 'Stop'
    $repo = Resolve-Path (Join-Path $PSScriptRoot '..')
    Push-Location $repo
    try {
        dotnet test LocalClipboard.slnx -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
        $artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repo 'artifacts'))
        $resolvedOutput = [IO.Path]::GetFullPath($Output)
        $artifactsPrefix = $artifactsRoot.TrimEnd('\') + '\'
        if (-not $resolvedOutput.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Publish output must stay under the repository artifacts directory.'
        }
        Remove-Item -LiteralPath $resolvedOutput -Recurse -Force -ErrorAction SilentlyContinue
        dotnet publish src/LocalClipboard.App/LocalClipboard.App.csproj -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $resolvedOutput
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
        & (Join-Path $PSScriptRoot 'verify-release.ps1') -PublishDirectory $resolvedOutput
    }
    finally {
        Pop-Location
    }

- [ ] **Step 4: Create the release verification script**

Create scripts/verify-release.ps1:

    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$PublishDirectory)

    $ErrorActionPreference = 'Stop'
    $directory = Resolve-Path $PublishDirectory
    $files = @(Get-ChildItem -LiteralPath $directory -File)
    $exe = @($files | Where-Object Name -eq 'LocalClipboard.exe')
    if ($exe.Count -ne 1) { throw 'Expected exactly one LocalClipboard.exe.' }
    if ($files.Count -ne 1) { throw "Expected one-file release, found $($files.Count) files." }
    $process = Start-Process -FilePath $exe[0].FullName -ArgumentList '--smoke-test' -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { throw "Smoke test failed with exit code $($process.ExitCode)." }
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $exe[0].FullName
    $sizeMb = [Math]::Round($exe[0].Length / 1MB, 2)
    Write-Host "Verified $($exe[0].Name), $sizeMb MB, SHA256 $($hash.Hash)"

- [ ] **Step 5: Publish and verify the portable executable**

Run:

    powershell -ExecutionPolicy Bypass -File scripts/publish.ps1

Expected: all tests pass, artifacts/publish/win-x64 contains only LocalClipboard.exe, the smoke test exits zero, and the script prints size plus SHA-256.

- [ ] **Step 6: Commit release automation**

Run:

    git add src/LocalClipboard.App scripts
    git commit -m "build: add verified single-file release"

---

### Task 14: Document Usage, Privacy, And Manual Acceptance

**Files:**
- Create: README.md
- Create: docs/manual-test-checklist.md
- Create: docs/performance-baseline.md

- [ ] **Step 1: Write the user README**

README.md must include these concrete sections:

- Download and run LocalClipboard.exe; no administrator permission is required.
- Default Alt+V shortcut and how to change it.
- Tray actions: open, pause, settings, clear, exit.
- Keyboard actions: arrows, Enter, Delete, Escape.
- Data path %LocalAppData%\LocalClipboard and complete removal instructions.
- Retention rules: 500 ordinary items, 30 days, 1 GB non-favorite image cache, 20 MB per image.
- Favorites are exempt from automatic cleanup and can exceed 1 GB.
- Privacy statement: offline only, no telemetry, no encryption, and no password filtering.
- Supported formats and explicit exclusions.
- Build command powershell -ExecutionPolicy Bypass -File scripts/publish.ps1.

- [ ] **Step 2: Create a reproducible manual test checklist**

Create docs/manual-test-checklist.md with checkboxes for:

1. Fresh launch, tray-only behavior, and startup registry entry.
2. Text capture including multiline, whitespace-only, Unicode, and 100,000-character text.
3. Image capture for screenshot, transparent PNG, 4K image, and over-20-MB rejection.
4. Consecutive duplicate handling and program-write suppression.
5. Search, filters, pagination, delete, favorite, protected clear, and full clear.
6. Pause/resume, hotkey conflict, second-instance activation, and clean exit.
7. Database corruption recovery using a copied test data directory.
8. Multi-monitor placement and 100/125/150/200 percent DPI.
9. Windows light, dark, and high-contrast modes.
10. Network observation confirming no outbound connections.

Every checkbox records Windows version, display scale, result, and notes. Never perform corruption testing against the user's real history directory.

- [ ] **Step 3: Measure startup, popup latency, idle CPU, and memory**

Create docs/performance-baseline.md and record:

- Test machine CPU, RAM, Windows build, and .NET publish version.
- EXE size and SHA-256 from verify-release.ps1.
- Private working set after five idle minutes with 500 text entries and 100 thumbnails.
- Average idle CPU over five minutes.
- Ten cold-start measurements from process start to tray ready.
- Twenty Alt+V measurements from WM_HOTKEY receipt to PopupForm.Shown.

Instrument timing with Stopwatch and sanitized diagnostic event names only; remove verbose timing logs after recording the baseline. Acceptance targets are approximately 80 MB memory, near-zero idle CPU, and 150 ms popup latency. If a target is missed, profile before optimizing and document the measured reason.

- [ ] **Step 4: Run the full acceptance pass**

Run:

    dotnet build LocalClipboard.slnx -c Release -warnaserror
    dotnet test LocalClipboard.slnx -c Release
    powershell -ExecutionPolicy Bypass -File scripts/publish.ps1
    git status --short

Expected: build and tests succeed, release verification succeeds, and git status lists only the three new documentation files before commit.

Complete every applicable checkbox in docs/manual-test-checklist.md. Do not mark environment-specific checks passed without actually performing them.

- [ ] **Step 5: Commit documentation and acceptance evidence**

Run:

    git add README.md docs/manual-test-checklist.md docs/performance-baseline.md
    git commit -m "docs: add usage and release acceptance guide"

---

## Final Verification

After all tasks and commits are complete, run:

    dotnet clean LocalClipboard.slnx -c Release
    dotnet build LocalClipboard.slnx -c Release -warnaserror
    dotnet test LocalClipboard.slnx -c Release
    powershell -ExecutionPolicy Bypass -File scripts/publish.ps1
    git status --short --branch

Expected:

- Every automated test passes.
- LocalClipboard.exe is the only release file.
- The release smoke test exits zero.
- Manual checklist has recorded results for the current machine.
- Performance baseline includes actual measurements.
- Git working tree is clean.

Do not claim the application is complete until these commands and manual checks have been run in the same implementation branch.
