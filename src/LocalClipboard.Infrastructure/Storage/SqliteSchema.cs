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
            CREATE INDEX IF NOT EXISTS ix_entries_last_used ON clipboard_entries(last_used_at DESC);
            CREATE INDEX IF NOT EXISTS ix_entries_type_last_used ON clipboard_entries(content_type, last_used_at DESC);
            CREATE INDEX IF NOT EXISTS ix_entries_favorite_last_used ON clipboard_entries(is_favorite, last_used_at DESC);
            CREATE INDEX IF NOT EXISTS ix_entries_hash ON clipboard_entries(content_hash);
            """;

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
