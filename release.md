# DSH Launcher 发布与开源范围评估

本文档供仓库维护者在上传 Git 和创建 Release 前自行检查，不必作为最终用户文档发布。

## 建议的项目名称

仓库名称建议：`DSH-Launcher-Windows`

展示名称建议：`DSH Launcher for Windows`

简短描述：

> Windows 11 托盘版 DeepSeek Harness 启动器，支持 DSH 服务启停、版本检查、稳定版更新、系统通知和配置目录快捷入口。

建议明确标注这是非官方项目，避免项目名称、描述和图标让用户误认为由 DeepSeek 官方发布。

## 建议开源的文件

以下文件构成可复现的源码、构建和测试内容，建议提交：

| 文件或目录 | 用途 | 建议 |
| --- | --- | --- |
| `Program.cs` | Launcher 主程序源码 | 必须开源 |
| `DSHTray.csproj` | .NET 项目和依赖配置 | 必须开源 |
| `构建DSH托盘程序.bat` | 单文件发布构建入口 | 建议开源 |
| `DSHLauncherTests/Regression.cs` | 服务状态回归测试 | 建议开源 |
| `DSHLauncherTests/Regression.csproj` | 测试项目配置 | 建议开源 |
| `README.md` | 项目说明和使用方法 | 必须开源 |
| `.gitignore` | 排除构建缓存与本地文件 | 必须补充并开源 |
| `LICENSE` | 程序源码采用 MIT，美术素材采用 CC BY-NC-SA 4.0 | 必须开源 |
| `icon/*.ico` | EXE 和托盘使用的“鲸鱼娘”图标 | 可按 CC BY-NC-SA 4.0 开源，必须保留署名及协议要求 |

## 可选开源的文件

| 文件 | 说明 |
| --- | --- |
| `DSH.bat` | 早期批处理实现，可作为设计来源或备用方案；若不再维护，可不上传，避免用户误用 |
| `deepseek chan.png` | 原始图片当前不参与构建；可按 CC BY-NC-SA 4.0 上传，仍须保留署名及协议要求 |
| `release.md` | 可留在私有维护资料中，也可以公开作为构建发布说明 |

## 不应提交到 Git 的内容

以下内容是本机构建缓存、生成物或可能包含环境信息，不建议提交：

```text
.dotnet-cli/
bin/
obj/
DSHLauncherTests/bin/
DSHLauncherTests/obj/
发布/
发布修复版/
*.user
*.suo
*.pdb
```

也不要提交以下用户数据：

```text
%USERPROFILE%\.dsh\
Token、Cookie、访问密钥
终端日志和崩溃转储
包含真实用户名的本地路径配置
```

## 建议的 `.gitignore`

正式上传前建议在仓库根目录创建 `.gitignore`，至少包含：

```gitignore
.dotnet-cli/
bin/
obj/
**/bin/
**/obj/
发布/
发布修复版/
.vs/
*.user
*.suo
*.pdb
```

## 图标与名称的版权风险

目前项目使用“鲸鱼娘”图标，已知素材信息如下：

- 角色原型：上善无形「溟月」
- DeepSeek 女仆装二创：ZipZipPipe
- 美术素材许可：CC BY-NC-SA 4.0

公开和再分发时需要：

1. 保留角色原型、二创作者和 CC BY-NC-SA 4.0 署名及协议链接。
2. 标明对原素材所作的修改（如裁剪、缩放、转换为 ICO）。
3. 不得将美术素材用于商业目的。
4. 衍生美术素材必须按相同或兼容协议共享。
5. 保留“非官方项目”声明，避免图标造成官方应用的误解。

注意：CC BY-NC-SA 4.0 的要求不只是署名，还包含非商业性使用和相同方式共享。该协议仅覆盖美术素材；程序源码独立采用 MIT License。

## Release 建议包含的文件

Git 仓库用于存放源码；GitHub Release 建议只附加：

```text
DSH Launcher.exe
SHA256SUMS.txt
```

不需要把整个 `发布` 目录压缩上传，因为当前发布产物本身就是单文件 EXE。

Release 说明建议包含：

- 版本号和发布日期。
- 新功能、修复项及已知问题。
- Windows 11 x64、Node.js、npm 和 DSH 的运行要求。
- “无需安装 .NET Runtime”的说明。
- EXE 的 SHA-256。
- 未进行代码签名时的 SmartScreen 提示说明。
- 非官方项目声明。

## 发布前检查清单

- [ ] 确认 `dotnet build DSHTray.csproj -c Release` 无错误。
- [ ] 确认回归测试全部通过。
- [ ] 重新执行 Release 单文件发布。
- [ ] 在另一台仅安装 Node.js、npm 和 DSH 的 Windows 11 x64 电脑测试。
- [ ] 验证启动、关闭、更新、浏览器打开和配置目录功能。
- [ ] 验证中文系统与不同用户名路径。
- [ ] 扫描仓库，确认没有 Token、个人配置或绝对路径泄漏。
- [ ] 在仓库和 Release 中保留“上善无形「溟月」 / ZipZipPipe / CC BY-NC-SA 4.0”署名。
- [ ] 确认发布内容保留 `LICENSE`，其中源码为 MIT、美术素材为 CC BY-NC-SA 4.0。
- [ ] 为 Release EXE 生成并公布 SHA-256。
- [ ] 如面向更多用户分发，评估购买代码签名证书。

## 当前建议结论

源码部分可以公开，但在正式上传前至少应完成三件事：

1. 添加 `.gitignore`，排除缓存和二进制生成目录。
2. 选择并添加源码许可证。
3. 确认现有图标和原始 PNG 的版权及再分发权限；无法确认时更换图标。
