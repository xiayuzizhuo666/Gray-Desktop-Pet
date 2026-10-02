# Gray Desktop Pet

Windows 独立透明桌宠。沿用 Gray 原图，包含待机、左右走动、挥手、跳跃等九组动作与十六方向视线。

## 安装与启动

系统要求：Windows 10/11，已有 .NET Framework 4.8 或更新版本。

- 安装版：在 Releases 下载 `Gray-Desktop-Pet-1.1.0-Setup.msi`，双击安装。仅安装到当前用户的 `%LOCALAPPDATA%\Programs\GrayDesktopPet`，并创建开始菜单快捷方式。无需管理员，不设开机自启。
- 便携版：下载 portable ZIP，解压整个目录后双击 `GrayPet.exe`。保留旁边的 `assets`、`frames.json` 和 `GrayPet.exe.config`。
- 卸载安装版：先退出 Gray，再在 Windows“已安装的应用”中卸载 **Gray Desktop Pet**。也可重新运行 MSI 选择 Remove。

安装包未进行代码签名。若系统或组织策略阻止运行，请遵守该策略；不要关闭安全功能。安装包不会自动下载或安装 .NET。

## 操作

| 操作 | 结果 |
| --- | --- |
| 按住人物拖动 | 移动，松手后限制到最近显示器的工作区 |
| 单击 / 双击 | 挥手 / 跳跃 |
| 右键人物 | 大小、动作、暂停/恢复、自动动作、视线跟随、退出 |
| 右键托盘 Gray 图标 | 显示、暂停/恢复、退出 |
| 双击托盘图标 | 找回 Gray |
| 再次启动 | 保留原实例，新进程安静退出 |

默认大小 125%。自动动作约每七秒选择一次。手动选动作会关闭自动动作，可重新勾选。暂停冻结动画和行走，仍可拖动或选择动作首帧；恢复后继续播放。正常运行不联网、不写设置、不包含聊天模型。退出后不留后台进程。位置和偏好不持久保存。

## 构建

在 Windows 上运行 `./build.ps1`，使用系统现有 .NET Framework C# 编译器，无第三方包。源码在 `source`。构建安装包运行 `./packaging/build-installer.ps1`，使用 Windows 自带 `makecab.exe` 与 Windows Installer COM，输出到 `out`；不下载工具，不修改执行策略。

先退出 Gray，再运行 `GrayPet.exe --self-test`。测试在程序目录输出 `test-results.txt`、`smoke-test-results.txt` 和 `window-render.png`。`--smoke-test` 通过实际启动入口运行约三秒后自动退出。诊断需要可写目录。

## 验证范围

应用测试通过 1,525 项断言，覆盖原图、帧时序、菜单/托盘命令、透明置顶属性、暂停、边界、重复启动和退出清理。实体鼠标操作与命中、真实多屏混合 DPI、实时缩放、显示器拔插尚未实测。应用自身的渲染图不能替代桌面截图。详见 [测试说明](docs/TESTING.md)。

## 权利说明

当前没有指定开源许可证；图像权利独立于代码。请阅读 [NOTICE.md](NOTICE.md)。
