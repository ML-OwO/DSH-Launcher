# DSH Launcher for Windows

一个适用于 Windows 11 的非官方 DeepSeek Harness（DSH）托盘启动器，可将 `dsh web` 作为后台服务管理。

> 本项目与 DeepSeek 官方无隶属或背书关系。

## 功能

- 启动时检查 DSH 稳定版本和预览版本，并自动启动服务
- 在托盘菜单中启动、关闭 DSH 服务及查看运行状态
- 更新至最新稳定版本，显示更新日志并在完成后重启服务
- 使用 Windows 通知提示版本、启动、关闭和更新结果
- 打开 `%USERPROFILE%\.dsh` 配置目录
- 左键或右键单击托盘图标打开置顶菜单

## 运行要求

- Windows 11 x64
- Node.js 与 npm
- 已全局安装 DSH，且 `dsh --version` 可正常执行

Release 提供的单文件 EXE 已包含 .NET 运行时，无需另行安装 .NET。

## 使用

1. 从 Releases 下载 `DSH Launcher.exe`。
2. 双击运行，程序将进入系统托盘并自动启动 DSH。
3. 单击托盘图标即可管理服务、更新版本或打开配置目录。

## 构建

源码构建需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。双击 `构建DSH托盘程序.bat`，或运行：

```powershell
dotnet publish DSHTray.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false -o 发布
```

生成文件位于 `发布\DSH Launcher.exe`。

## 测试

```powershell
dotnet run --project DSHLauncherTests\Regression.csproj -c Release
```

## 许可

程序源码采用 [MIT License](LICENSE)。

“鲸鱼娘”美术素材采用 [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/deed.zh-hans)：

- 角色原型：上善无形「溟月」
- DeepSeek 女仆装二创：ZipZipPipe

详细条款见 [LICENSE](LICENSE)。
