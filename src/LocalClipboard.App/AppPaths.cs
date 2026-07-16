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
        return new(
            root,
            Path.Combine(root, "history.db"),
            Path.Combine(root, "settings.json"),
            root,
            Path.Combine(root, "logs"),
            Path.Combine(root, "recovery"));
    }
}
