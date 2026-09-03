# ClipHelper

ClipHelper is a local clipboard history manager for Windows. It runs in the system tray, records copied text and images, and provides fast search, filtering, favorites, and clipboard restoration.

The application works entirely on your computer. It does not require an online account or cloud service, and it does not upload your clipboard history.

## Download and Run

Download `ClipHelper.exe` from the [Releases](https://github.com/839194950/ClipHelper/releases) page and run it directly.

- Supports Windows 10 and Windows 11 on x64 systems.
- Distributed as a self-contained single executable; no separate .NET installation is required.
- Runs in the system tray after startup.
- Press `Alt + V` to open clipboard history by default.
- English is the default language. You can switch between English and Chinese in Settings.

Windows SmartScreen may display a warning on first launch because the executable is not commercially code-signed. Verify that the file came from this repository before choosing to continue.

## Features

- Automatically records copied text and images.
- Displays clipboard history in a unified timeline ordered by recent use.
- Searches text content and filters entries by All, Text, Images, or Favorites.
- Restores the selected entry to the clipboard with a double-click or `Enter`.
- Supports keyboard navigation with the arrow keys, `Delete` to remove an entry, and `Esc` to close the window.
- Keeps important entries as favorites; favorites are excluded from automatic cleanup.
- Deduplicates consecutive copies of the same content and updates the existing entry's recent-use time.
- Stores images as PNG files and generates thumbnails for the history list.
- Supports a configurable global hotkey and launch-at-startup setting.
- Supports instant English/Chinese switching from Settings, with the selected language saved locally.
- Provides tray menu actions for opening history, opening Settings, clearing history, pausing monitoring, and exiting.
- Runs as a single instance; starting it again opens the existing instance.

## Local Data

Application data is stored by default in:

```text
%LOCALAPPDATA%\LocalClipboard
```

This directory contains the SQLite history database, settings, images, thumbnails, logs, and recovery files. ClipHelper does not intentionally upload this data.

Default retention rules:

- Up to 500 regular history entries.
- Regular entries are retained for up to 30 days.
- The image cache has a 1 GB safety limit.
- Encoded images larger than 20 MB are not stored.
- Favorite entries are excluded from these automatic cleanup limits.

## Build from Source

Development requires Windows and the .NET 10 SDK.

```powershell
dotnet build LocalClipboard.slnx
dotnet test LocalClipboard.slnx
```

To publish a self-contained Windows x64 single-file executable:

```powershell
dotnet publish src/LocalClipboard.App/LocalClipboard.App.csproj `
  -c Release `
  -p:PublishProfile=WinX64SingleFile
```

The published executable is named `ClipHelper.exe`.

## Current Limitations

- Windows x64 only.
- Records text and images, but not file lists, rich text, or other clipboard formats.
- No cross-device synchronization, cloud backup, or image OCR.
- Release binaries are not currently code-signed.

## License

This project is licensed under the [MIT License](LICENSE).
