# v1.0.0-beta1

中文输入英语学习助手的首个便携预发布版本。

- Windows 10/11 x64，需 .NET Framework 4.8。
- 使用微软拼音照常输入中文，英文显示在学习浮窗中。
- 支持拖动定位、托盘暂停、当前输入更新与按天本地学习记录。
- 首次使用配置自己的阿里云百炼 API，默认翻译模型为 `qwen-mt-flash`。程序不附带密钥或 API 额度。
- ZIP 只需解压后运行，不安装或替换输入法，不添加开机启动。
- 程序与源码采用 MIT License。

请下载 `EnglishLearningAssistant-1.0.0-beta1-win-x64.zip`（约 172 KB），解压后双击 `EnglishLearningAssistant.exe`。另附 `.sha256` 文件供核对下载完整性。GitHub 自动提供的 Source code ZIP 是源码，不是运行包。

已完成独立构建与 72 项离线回归检查，GitHub Actions 的 Windows 自动构建和全部检查也已通过。不同软件的文本接口可能影响触发和更新，微信兼容接口属于实验功能，暂不保证所有版本的应用均稳定支持。欢迎通过 Issues 提交可公开的复现步骤。

翻译正文会发送到用户配置的 API 服务；本地历史包含中英文，请勿随软件目录分享。
