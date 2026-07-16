namespace LocalClipboard.Infrastructure.Storage;

internal static class SqliteProviderBootstrap
{
    private static readonly object SyncRoot = new();
    private static bool initialized;

    public static void EnsureInitialized()
    {
        if (Volatile.Read(ref initialized)) return;

        lock (SyncRoot)
        {
            if (initialized) return;

            try
            {
                SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_winsqlite3());
                SQLitePCL.raw.FreezeProvider(true);
                Volatile.Write(ref initialized, true);
            }
            catch
            {
                Volatile.Write(ref initialized, false);
                throw;
            }
        }
    }
}
