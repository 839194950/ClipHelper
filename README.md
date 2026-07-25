# ClipHelper

ClipHelper 是一个面向 Windows 的本地剪贴板历史工具。它在系统托盘中运行，记录复制过的文本和图片，并提供搜索、筛选、收藏与快速恢复功能。

项目以本地使用为主，不依赖在线账户或云端服务。历史记录保存在当前用户的本机数据目录中。

## 下载与运行

在仓库的 [Releases](https://github.com/839194950/ClipHelper/releases) 页面下载 `ClipHelper.exe`，双击即可运行。

- 支持 Windows 10 / Windows 11 x64。
- 发布文件为自包含单文件程序，不需要单独安装 .NET。
- 程序启动后常驻系统托盘。
- 默认使用 `Alt + V` 打开剪贴板历史窗口。

首次运行时，Windows 可能显示 SmartScreen 提示。这是因为发布文件暂未进行商业代码签名，可以在确认文件来源后选择继续运行。

## 功能

- 自动记录复制的文本和图片。
- 使用统一时间线按最近使用时间展示历史记录。
- 搜索文本内容，并按全部、文本、图片或收藏进行筛选。
- 点击或按 `Enter` 将选中的记录重新写入系统剪贴板。
- 使用方向键切换记录，按 `Delete` 删除记录，按 `Esc` 关闭窗口。
- 收藏需要长期保留的记录，收藏内容不参与普通记录自动清理。
- 连续复制相同内容时进行去重，并更新该记录的最近使用时间。
- 图片以 PNG 文件保存，并生成列表缩略图。
- 支持修改全局快捷键和设置开机启动。
- 支持托盘菜单打开历史、打开设置、清空历史和退出程序。
- 使用单实例运行；重复启动程序时会唤起已有实例。

## 数据保存

应用数据默认保存在：

```text
%LOCALAPPDATA%\LocalClipboard
```

其中包括 SQLite 历史数据库、设置文件、图片、缩略图、日志和恢复文件。ClipHelper 不会主动上传这些数据。

默认保留规则：

- 普通记录最多 500 条。
- 普通记录最长保留 30 天。
- 图片缓存安全上限为 1 GB。
- 单张图片编码后超过 20 MB 时不保存。
- 收藏记录不受上述自动清理规则限制。

## 从源码构建

开发环境需要 Windows 和 .NET 10 SDK。

```powershell
dotnet build LocalClipboard.slnx
dotnet test LocalClipboard.slnx
```

生成 Windows x64 自包含单文件：

```powershell
dotnet publish src/LocalClipboard.App/LocalClipboard.App.csproj `
  -p:PublishProfile=WinX64SingleFile `
  -p:DebugSymbols=false `
  -p:DebugType=None
```

## 当前限制

- 仅支持 Windows x64。
- 只记录文本和图片，不处理文件列表、富文本或其他剪贴板格式。
- 不提供跨设备同步、云端备份或图片 OCR。
- 发布文件暂未进行代码签名。

## 许可证

本项目使用 [MIT License](LICENSE)。
