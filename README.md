# 中文输入英语学习助手

使用微软拼音照常输入中文，稍作停顿即可在悬浮窗看到英文译文。中文输入保持原样，适合在日常交流、工作和科研写作中积累英语表达。

目前为 **1.0.0-beta1**，支持 Windows 10/11 x64。不同软件的输入接口存在差异，欢迎反馈兼容性问题。

## 下载与使用

**[直接下载 Windows x64 运行版（约 172 KB）](https://github.com/NHT-ICE/english-learning-assistant/releases/download/v1.0.0-beta1/EnglishLearningAssistant-1.0.0-beta1-win-x64.zip)**，或查看 [版本说明与其他下载](https://github.com/NHT-ICE/english-learning-assistant/releases/tag/v1.0.0-beta1)。GitHub 的“Download ZIP”下载的是源码，不能直接运行。

1. 解压到自己可写的目录，双击 `EnglishLearningAssistant.exe`。
2. 按首次使用窗口的链接注册阿里云、开通百炼并创建自己的 API Key。
3. 填入自己的 Base URL、密钥与模型名称，默认模型为 `qwen-mt-flash`。
4. 在输入框输入中文，停顿后查看英文浮窗；拖动标题栏可以固定位置。

详细步骤、API 申请链接、费用说明和常见问题见 [使用说明](docs/USER_GUIDE.md)。程序不附带 API 密钥或免费额度，调用费用由用户自己的阿里云账号承担。

## 功能

- 保持系统输入法，不安装或替换输入法。
- 自动翻译已提交的中文，支持输入更新、取消过期请求和本地缓存。
- 浮窗不抢输入焦点，支持拖动、收起和托盘暂停。
- 学习记录按天保存在本地 JSONL 文件中，可关闭记录；目前不自动生成学习总结。
- 内置科研术语表，可通过修改 `src/Glossary.cs` 补充。
- 首次配置后可使用独立便携目录，运行依赖为 .NET Framework 4.8。

## 数据与隐私

翻译时，当前待译中文会发送到用户配置的 API 服务。学习记录包含中文、英文等内容，仅保存在本地 `history/`；密钥使用 Windows 当前用户的 DPAPI 加密，位于 `.secrets/`，不能用于跨账号共享。默认不写诊断日志。

分享软件时使用干净的发布 ZIP。不要上传已经使用过的程序目录、密钥、学习记录或包含个人输入的诊断文件。

## 从源码构建

安装 Visual Studio 2022 或 Build Tools，选择“使用 C++ 的桌面开发”、Windows SDK、.NET Framework 4.8 SDK 与目标包。在项目根目录的 PowerShell 中运行：

```powershell
& .\build.ps1
& .\test.ps1
```

生成便携包位于 `release/`。可用 `& .\build.ps1 -Version '1.0.0-beta2'` 指定版本。脚本会自动查找工具链，生成 Windows UI Automation 互操作程序集，编译 C# 助手及静态运行库的 C++ 兼容模块。无需下载青简或安装 Python、Node.js、Rust。

测试使用模拟输入接口与测试用假密钥，不调用收费翻译 API，不启动全局监听。真实应用的兼容性仍需手动验证。

## 参与改进

可以通过 [Issues](https://github.com/NHT-ICE/english-learning-assistant/issues) 提交问题或建议，或 Fork 后提交 Pull Request。开发结构、测试及反馈要求见 [贡献说明](CONTRIBUTING.md)。自动构建配置会在推送和 PR 时构建、运行回归测试并保存 ZIP；正式版本由维护者在 Releases 中发布。

## 许可证

本项目采用 [MIT License](LICENSE)，允许修改、分发和商用，使用时须保留版权及许可声明。
