using System.Reflection;

namespace LocalClipboard.App;

internal static class TrayIconFactory
{
    private const string ResourceName = "LocalClipboard.App.Assets.ClipHelper.ico";

    internal static Icon Create()
    {
        Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded icon resource '{ResourceName}' was not found.");
        using (stream)
        using (var icon = new Icon(stream))
        {
            return (Icon)icon.Clone();
        }
    }
}
