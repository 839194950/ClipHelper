using Microsoft.Win32;
using System.Runtime.Versioning;

namespace LocalClipboard.Infrastructure.Windows;

[SupportedOSPlatform("windows")]
public sealed class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LocalClipboard";

    public bool IsEnabled(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        string? value = key?.GetValue(ValueName) as string;
        return string.Equals(value, Quote(executablePath), StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(string executablePath, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Unable to open the current user's startup registry key.");

        if (enabled)
        {
            key.SetValue(ValueName, Quote(executablePath), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static string Quote(string path) => string.Concat('"', Path.GetFullPath(path), '"');
}
