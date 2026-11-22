# Bing Wallpapers · 每日壁纸

Windows 桌面壁纸管理程序。应用通过 Bing 中国区的每日图片接口获取最近壁纸，并通过 Windows 的 `SystemParametersInfoW` API 设置桌面背景。

## 运行

解压发布包，双击 `BingWallpapers.exe`。保留同目录下的 `BingWallpapers.exe.config` 和 `AntdUI.dll`。

- 兼容目标为 Windows 7 SP1、Windows 10 和 Windows 11，支持系统采用的 32 位或 64 位 .NET Framework 4.7.2 或更新版本。
- Windows 7 SP1 如未安装运行时，可下载安装 [Microsoft .NET Framework 4.7.2](https://dotnet.microsoft.com/zh-cn/download/dotnet-framework/net472)。访问 Bing 需要可用的 TLS 1.2 和当前网站证书信任链。

## 使用

- **探索壁纸**显示 Bing 返回的最新壁纸和最近发布记录。点缩略图切换预览，点「设为壁纸」下载原图并应用。Bing 提供的 UHD 版本优先，若该尺寸不可用则用 1920×1080。
- **本地收藏**可按标题或日期搜索，查看图片尺寸和版权，重新应用、定位或删除。默认目录是桌面上的 `Bing Wallpapers`。图片按 `壁纸标题_YYYYMMDD.jpg` 命名，日期使用 Bing 中国区的发布日期，图片信息写在同目录下的 `wallpapers.json`。目录更改后，应用读取新目录自己的索引，已有文件留在原目录。
- **自动换图**默认关闭。固定间隔按 Bing 日期从新到旧循环，随机模式在设定的最短和最长分钟数之间抽取下一次间隔，并随机挑选壁纸。可选图片来自最近的 Bing 壁纸与当前收藏，有其他图片可用时会跳过上一张。
- **后台运行**可用「转到托盘」或窗口关闭按钮，双击托盘图标返回。托盘菜单里的「退出」会停止自动换图。
- **网络代理**支持跟随系统、直连、自定义 HTTP 代理。自定义代理示例为 `127.0.0.1:7890`。代理设置只应用于这个程序。

配置和预览缓存位于 `%LOCALAPPDATA%\BingWallpapers`。断网时可重新设置已保存的图片，并浏览此前缓存的 Bing 列表和预览。

## 构建源码

安装包含 .NET Framework 4.7.2 目标包的 Visual Studio 或 Build Tools，也可配合已安装的 .NET Framework 4.7.2 Developer Pack 使用 .NET SDK。

```powershell
dotnet build .\BingWallpapers.csproj -c Release
```

输出位于 `bin\Release\net472`。界面使用 [AntdUI](https://github.com/AntdUI/AntdUI) 的 WinForms 组件。NuGet 会在构建时还原该依赖。许可证见 `Licenses/AntdUI-LICENSE.txt`。
