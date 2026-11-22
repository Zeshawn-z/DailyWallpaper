# Dayframe · Bing Wallpapers

Dayframe 是根据提供的 Dayframe 界面稿实现的 Windows Bing 壁纸管理器。应用使用 WinForms，目标运行环境为 Windows 7 SP1、Windows 10 和 Windows 11，以及 .NET Framework 4.7.2 或更新版本。

## 运行

双击 `BingWallpapers.exe` 即可运行。程序文件已内嵌 AntdUI 和所用字体。如果旧版 Bing Wallpapers 正在托盘运行，请先从托盘退出旧版。Windows 7 SP1 访问 Bing 需要可用的 TLS 1.2 和证书信任链。

## 页面

- **Today** 展示当天和最近八张 Bing 壁纸。可切换预览、选择桌面填充方式、收藏或应用图片。图片可用时优先保存 UHD 原图，文件名采用 `标题_YYYYMMDD.jpg`。点击 **About this image** 打开 Bing 的图片介绍。
- **Library** 以缩略图卡片展示已保存图片。可按标题、标签或日期搜索，并筛选收藏、参与轮换及文件缺失的条目。右侧可编辑标题、管理标签、收藏及轮换状态，也可应用、定位或删除图片。
- **Metadata file** 从 Library 底部打开。可查看实际索引、定位或复制路径、在编辑器打开、导出、补录目录中的 JPEG，或清理缺失条目。重建保留已有图片的元数据。索引更新时会保留上一份 `wallpapers.json.bak`，现有旧版索引可直接读取并在保存时升级。
- **Auto-change** 提供固定间隔、随机间隔和每日新 Bing 图片三种模式。可以从收藏、整个本地库或最近八天 Bing 图片取图，支持下一张预览、预定时间、临时暂停、近期记录、电池供电暂停、后台托盘和开机启动。可用图片不足十张时，会优先选最久未用的图片。
- **Settings** → **Appearance** 可即时切换 English／简体中文和深色／浅色，选择会自动保存。图片标题使用 Bing 提供的对应语言版本，有缺失时显示原题。手工编辑过的标题会保留。新保存的文件使用当时显示的标题，已有文件名继续沿用。
- **Settings** 同时管理保存目录、Bing 市场和 HTTP 代理。Bing 市场决定显示哪些图片，和界面语言分别设置。默认使用美国市场，也可切换到中国市场。默认壁纸库位于桌面的 `Bing Wallpapers`。切换目录时可直接使用目标目录已有的索引。

配置与预览缓存位于 `%LOCALAPPDATA%\BingWallpapers`。设置桌面壁纸时调用 Windows 的 `SystemParametersInfoW` API。

## 构建

使用带有 .NET Framework 4.7.2 Developer Pack 的 Visual Studio、Build Tools 或 .NET SDK。

```powershell
dotnet build .\BingWallpapers.csproj -c Release
```

单文件输出为 `bin\Release\net472\BingWallpapers.exe`。随程序分发的许可证已内嵌在 EXE 中，源文件位于 `Licenses`。
