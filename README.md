# DSH Launcher

非官方 DeepSeek Harness（DSH）托盘启动器，用图形菜单管理 `dsh web`，无需保持终端窗口打开。

## 功能

- 启动时检查稳定版本（RC）和预览版本（Alpha），自动启动服务并打开浏览器。
- 在托盘中启动、关闭服务、查看运行状态和打开配置目录。
- 更新前停止服务，在终端显示更新日志，成功后关闭更新窗口并重新启动服务。
- 系统通知提示版本、服务状态和更新结果。

## 支持的平台

| 版本 | 适用系统 |
| --- | --- |
| Windows | Windows 11 x64 |
| Linux | **仅适用于银河麒麟桌面操作系统 V10 x86_64（64 位 x86，UKUI 桌面）** |

Linux 版不适用于 32 位 x86、ARM 或龙芯架构；其他 Linux 发行版及麒麟 V11 暂不在支持范围内。

两个版本均为自带 .NET 运行时的单文件程序。使用前需安装 Node.js、npm 和 DSH，并确保 `dsh --version` 可正常执行。

Linux 桌面还需 GTK3、libappindicator3、notify-send、xdg-open 和 x-terminal-emulator。Node/npm/dsh 应位于桌面会话的 PATH 中；也支持 `~/.local/opt/node-current/bin`。

## 使用

Windows：运行 `DSH Launcher.exe`，单击托盘图标管理服务。

Linux：为 `DSH-Launcher` 添加执行权限后运行，或在文件管理器中双击：

```sh
chmod +x DSH-Launcher
./DSH-Launcher
```

配置目录为 Windows 的 `%USERPROFILE%\.dsh` 或 Linux 的 `~/.dsh`。

Linux 版可在 Launcher 重启后识别自己启动的服务，不接管其他终端手动启动的 DSH。诊断日志保存在 `~/.local/state/dsh-launcher`。

### Linux 浏览器

DSH 页面需要较新的浏览器。旧版 Chromium 102 缺少所需 JavaScript API，可能导致插件加载失败。

Launcher 优先使用 `~/.local/opt/dsh-browser/opt/google/chrome/google-chrome`，不存在时使用系统默认浏览器。浏览器需自行准备，不包含在 Launcher 中。

## 项目结构

```text
src/
  DSHLauncher.Core/           # 可复用逻辑，目前由 Linux 版使用
  DSHLauncher.Windows/        # Windows 托盘程序
  DSHLauncher.Linux/          # 麒麟托盘程序
tests/
  DSHLauncher.Core.Tests/     # 核心逻辑测试
  DSHLauncher.Windows.Tests/  # Windows 回归测试
icon/                        # 共用图标
scripts/                     # 原始 DSH 批处理脚本
publish/                     # 本地构建产物，不提交
```

## 构建与测试

源码构建需要 .NET 8 SDK。在项目根目录运行 PowerShell 脚本：

```powershell
./build-windows.ps1
./build-linux.ps1
```

生成文件分别位于 `publish/win-x64` 和 `publish/linux-x64`，各自附带 `SHA256SUMS.txt`。Windows 版请在 Windows 上构建和测试。

```powershell
dotnet run --project tests/DSHLauncher.Core.Tests -c Release
dotnet run --project tests/DSHLauncher.Windows.Tests/Regression.csproj -c Release
```

Linux 上也可直接构建：

```sh
dotnet publish src/DSHLauncher.Linux -c Release -r linux-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o publish/linux-x64
```

## 许可

程序源码采用 [MIT License](LICENSE)。

“鲸鱼娘”美术素材采用 [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/deed.zh-hans)：

- 角色原型：上善无形「溟月」
- DeepSeek 女仆装二创：ZipZipPipe
