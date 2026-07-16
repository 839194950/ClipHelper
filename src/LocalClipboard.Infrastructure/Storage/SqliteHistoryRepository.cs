using System.Globalization;
using LocalClipboard.Core.Abstractions;
using LocalClipboard.Core.Models;
using Microsoft.Data.Sqlite;

namespace LocalClipboard.Infrastructure.Storage;

public sealed class SqliteHistoryRepository : IHistoryRepository
{
    private const string SelectedColumns = "id, content_type, text_content, content_hash, image_path, thumbnail_path, width, height, encoded_size, created_at, last_used_at, is_favorite";
    private readonly string connectionString;

    public SqliteHistoryRepository(string databasePath)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
    }

    public async Task<ClipboardEntry?> GetLatestAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectedColumns} FROM clipboard_entries ORDER BY last_used_at DESC LIMIT 1";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadEntry(reader) : null;
    }

    public async Task<IReadOnlyList<ClipboardEntry>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectedColumns} FROM clipboard_entries ORDER BY last_used_at ASC";
        return await ReadManyAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<ClipboardEntry>> QueryAsync(HistoryQuery query, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {SelectedColumns} FROM clipboard_entries
            WHERE ($search IS NULL OR text_content LIKE $pattern ESCAPE '\')
              AND ($type IS NULL OR content_type = $type)
              AND ($favorites = 0 OR is_favorite = 1)
            ORDER BY last_used_at DESC
            LIMIT $limit OFFSET $offset
            """;
        command.Parameters.AddWithValue("$search", query.Search is null ? DBNull.Value : query.Search);
        command.Parameters.AddWithValue("$pattern", query.Search is null ? DBNull.Value : $"%{EscapeLike(query.Search)}%");
        command.Parameters.AddWithValue("$type", query.ContentType is null ? DBNull.Value : (int)query.ContentType.Value);
        command.Parameters.AddWithValue("$favorites", query.FavoritesOnly ? 1 : 0);
        command.Parameters.AddWithValue("$limit", Math.Clamp(query.Limit, 1, 200));
        command.Parameters.AddWithValue("$offset", Math.Max(query.Offset, 0));
        return await ReadManyAsync(command, cancellationToken);
    }

    public async Task InsertAsync(ClipboardEntry entry, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO clipboard_entries (id, content_type, text_content, content_hash, image_path, thumbnail_path, width, height, encoded_size, created_at, last_used_at, is_favorite)
            VALUES ($id, $type, $text, $hash, $image, $thumbnail, $width, $height, $size, $created, $used, $favorite)
            """;
        AddEntryParameters(command, entry);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task TouchAsync(Guid id, DateTimeOffset usedAt, CancellationToken cancellationToken) => await ExecuteAsync(
        "UPDATE clipboard_entries SET last_used_at = $used WHERE id = $id", id, usedAt, cancellationToken);

    public async Task SetFavoriteAsync(Guid id, bool isFavorite, CancellationToken cancellationToken) => await ExecuteAsync(
        "UPDATE clipboard_entries SET is_favorite = $favorite WHERE id = $id", id, isFavorite, cancellationToken);

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken) => await ExecuteAsync(
        "DELETE FROM clipboard_entries WHERE id = $id", id, null, cancellationToken);

    public async Task ClearAsync(bool includeFavorites, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = includeFavorites ? "DELETE FROM clipboard_entries" : "DELETE FROM clipboard_entries WHERE is_favorite = 0";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        SqliteProviderBootstrap.EnsureInitialized();
        SqliteConnection connection = new(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await SqliteSchema.EnsureCreatedAsync(connection, cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task<IReadOnlyList<ClipboardEntry>> ReadManyAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        List<ClipboardEntry> entries = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) entries.Add(ReadEntry(reader));
        return entries;
    }

    private static ClipboardEntry ReadEntry(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)), (ClipboardContentType)reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt64(8),
        DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture), DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture), reader.GetBoolean(11));

    private static void AddEntryParameters(SqliteCommand command, ClipboardEntry entry)
    {
        command.Parameters.AddWithValue("$id", entry.Id.ToString()); command.Parameters.AddWithValue("$type", (int)entry.ContentType);
        command.Parameters.AddWithValue("$text", (object?)entry.TextContent ?? DBNull.Value); command.Parameters.AddWithValue("$hash", entry.ContentHash);
        command.Parameters.AddWithValue("$image", (object?)entry.ImagePath ?? DBNull.Value); command.Parameters.AddWithValue("$thumbnail", (object?)entry.ThumbnailPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$width", entry.Width); command.Parameters.AddWithValue("$height", entry.Height); command.Parameters.AddWithValue("$size", entry.EncodedSize);
        command.Parameters.AddWithValue("$created", entry.CreatedAt.ToString("O", CultureInfo.InvariantCulture)); command.Parameters.AddWithValue("$used", entry.LastUsedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$favorite", entry.IsFavorite ? 1 : 0);
    }

    private async Task ExecuteAsync(string sql, Guid id, object? value, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand(); command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id.ToString());
        if (sql.Contains("$used", StringComparison.Ordinal)) command.Parameters.AddWithValue("$used", ((DateTimeOffset)value!).ToString("O", CultureInfo.InvariantCulture));
        if (sql.Contains("$favorite", StringComparison.Ordinal)) command.Parameters.AddWithValue("$favorite", (bool)value! ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string EscapeLike(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
