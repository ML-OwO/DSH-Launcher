# DSH Launcher for Windows

一个面向 Windows 11 的非官方 DeepSeek Harness（DSH）托盘启动器。

它将原本需要长期占用终端窗口的 `dsh web` 封装为托盘程序，并提供服务启停、版本检查、稳定版本更新和系统通知等常用功能。发布版可打包为单个 EXE，适合复制到其他已配置好 DSH 环境的 Windows 电脑上直接使用。

> 本项目不是 DeepSeek 官方项目，与 DeepSeek 官方无隶属或背书关系。

## 功能

- 启动 Launcher 后自动检查本机 DSH 版本。
- 从 npm 查询 `@deepseek-ai/dsh` 的稳定版本和最新预览版本。
- 检测到新的稳定版本时，在托盘菜单中提供更新入口。
- 更新前自动关闭正在运行的 DSH 服务。
- 使用可见的 PowerShell 终端执行 npm 更新并显示完整日志。
- 更新成功后自动关闭更新终端并重新启动 DSH 服务。
- Launcher 启动后默认运行 `dsh web`，保留 DSH 默认打开浏览器的行为。
- 等待 DSH 输出 Web 地址和浏览器启动信息后，再发送“服务已启动”通知。
- 根据实际 DSH 进程和监听端口同步托盘中的运行状态。
- 通过 Windows 通知提示版本、启动、关闭、更新成功或失败等状态。
- 左键或右键单击托盘图标均可打开置顶菜单。
- 快速打开 `%USERPROFILE%\.dsh` 配置目录。

## 托盘菜单

- DSH 服务状态
- 更新至最新稳定版本（仅发现更新时显示）
- 打开 DSH 配置目录
- 启动 DSH 服务
- 关闭 DSH 服务
- 退出 DSH Launcher

## 运行要求

- Windows 11 x64
- 已安装 Node.js 和 npm
- 已全局安装 DSH，并确保以下命令可在终端中正常运行：

```powershell
dsh --version
npm --version
```

单文件发布版包含所需的 .NET 运行时，使用者不需要另外安装 .NET Runtime 或 .NET SDK。

## 使用方法

1. 从 Releases 下载 `DSH Launcher.exe`。
2. 将 EXE 放在任意目录，例如桌面。
3. 双击运行，程序将进入系统托盘并自动检查版本、启动 DSH 服务。
4. 单击托盘图标可启动或关闭服务、更新稳定版本、打开配置目录或退出 Launcher。

如果 Windows SmartScreen 提示来源未知，请先核对 Release 页面提供的 SHA-256，再根据自己的信任判断是否运行。未签名的个人构建通常会触发此类提示。

## 从源码构建

构建环境需要安装 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。在项目目录运行：

```powershell
dotnet publish DSHTray.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false -o 发布
```

也可以双击 `构建DSH托盘程序.bat`。生成文件位于：

```text
发布\DSH Launcher.exe
```

## 测试

服务状态回归测试位于 `DSHLauncherTests`：

```powershell
dotnet run --project DSHLauncherTests\Regression.csproj -c Release
```

测试覆盖启动包装进程退出后的状态同步、菜单启停状态、停止关联进程，以及更新期间的状态保持。

## 版本更新使用的命令

Launcher 不依赖 pnpm。主要调用如下：

```powershell
dsh --version
npm view @deepseek-ai/dsh versions --json
npm install -g @deepseek-ai/dsh@<目标版本> --force --progress=true --loglevel=info
dsh web
```

更新时会先解析 `npm.cmd` 的绝对路径，再通过带 UTF-8 BOM 的临时 PowerShell 脚本执行，以减少不同系统环境下的命令解析和中文乱码问题。

## 配置与隐私

DSH 配置目录默认为 `%USERPROFILE%\.dsh`。Launcher 不会将该目录复制到项目目录，也不会主动上传其中的数据。

提交代码前请确认没有把个人配置、Token、日志或其他敏感信息加入 Git。

## 开源许可

程序源码、项目配置、构建脚本和测试代码采用 [MIT License](LICENSE) 开源。

“鲸鱼娘”相关图标及图片素材采用 [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/deed.zh-hans) 授权：

- 角色原型：上善无形「溟月」
- DeepSeek 女仆装二创：ZipZipPipe

使用和再分发相关素材时须保留署名，并遵守非商业性使用及相同方式共享等要求。详细条款见 [`LICENSE`](LICENSE)。

MIT License 只适用于程序部分；美术素材的 CC BY-NC-SA 4.0 条款保持独立。使用者可以依据 MIT 许可商业使用程序源码，但不能因此商业使用“鲸鱼娘”素材。商业分发时应替换为拥有相应商业授权的图标。
