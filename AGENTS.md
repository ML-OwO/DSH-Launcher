# 开发范围

- 本项目位于 `C:\Users\Molan\Project\DSH Launcher`，后续开发仅在此目录进行，不修改相邻项目。
- Windows 目标为 Windows 11 x64；Linux 仅支持银河麒麟 V10 桌面版 x86_64（UKUI）。
- `src/DSHLauncher.Core` 只放不依赖桌面环境或操作系统的逻辑。
- `src/DSHLauncher.Windows` 和 `src/DSHLauncher.Linux` 分别维护平台实现；Windows 尚未接入 Core，不要为了统一结构强行改写业务逻辑。
- `tests` 放回归测试，`scripts` 放辅助脚本，`releases` 放各平台产物。
- `local` 是迁移保留的旧文档、参考副本和缓存，不提交到 Git。
- `ISO` 和 `VM` 是本地测试资源，不提交到 Git，不修改或删除其中的数据，除非任务明确要求。
- 源码采用 MIT；`icon` 素材采用 CC BY-NC-SA 4.0，保留原署名。
