# AllPurposeAssistant（小帮手）

一个面向 Windows 的桌面效率工具，提供悬浮球和侧边栏两种使用方式，集成截图、剪贴板历史、便签与快捷操作。

## 功能

- 悬浮球与侧边栏模式切换，支持系统托盘常驻
- 区域截图、延时截图，以及截图后的编辑、保存和钉图
- 可配置全局截图快捷键，默认：`Ctrl+Alt+Shift+Z`
- 剪贴板历史：记录文本和图片，最多保留 50 条
- 便签：创建并保存默认便签
- 快捷操作：关机、重启、锁定、打开“我的电脑”，以及自定义应用、文件夹和网址
- 首次启动时可设置开机自启和创建桌面快捷方式
- 可配置截图保存目录、PNG/JPEG 格式、JPEG 质量和钉图透明度

> 快捷操作中的关机、重启和命令执行会直接影响本机，请谨慎使用。

## 运行环境

- Windows 10 或更高版本
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## 构建与运行

在项目根目录执行：

```powershell
dotnet restore
dotnet build src\AllPurposeAssistant\AllPurposeAssistant.csproj
dotnet run --project src\AllPurposeAssistant\AllPurposeAssistant.csproj
```

也可以直接使用 Visual Studio 打开 `AllPurposeAssistant.slnx` 后运行。

## 本地数据

应用设置、便签、快捷操作和剪贴板图片保存在：

```text
%APPDATA%\AllPurposeAssistant
```

这些运行时数据不会随源码提交到仓库。

## 项目结构

```text
src/AllPurposeAssistant/
├─ Helpers/       # Windows API 与屏幕捕获辅助代码
├─ Models/        # 配置、便签、剪贴板和快捷操作模型
├─ Resources/     # 程序图标与界面资源
├─ Services/      # 截图、托盘、热键、持久化等服务
├─ ViewModels/    # 视图模型
└─ Views/         # WPF 窗口和控件
```

## 技术栈

- C# / .NET 10
- WPF
- `H.NotifyIcon`：系统托盘图标
- `Newtonsoft.Json`：本地 JSON 数据持久化
