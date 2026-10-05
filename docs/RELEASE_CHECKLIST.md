# 首次 GitHub 发布步骤

1. GitHub 仓库为 `NHT-ICE/english-learning-assistant`，已补齐 MIT LICENSE。
2. 仅上传本公开源码目录；不要上传原开发目录或正在运行的安装目录。
3. 运行 `build.ps1` 和 `test.ps1`，确认便携包内只有 EXE、两个 DLL、无密钥 settings.json、README.md 和 LICENSE。
4. 提交源码，推送到新仓库的 main 分支，检查 Actions 构建结果。
5. 创建 `v1.0.0-beta1` 标签对应的预发布版，上传 `release/EnglishLearningAssistant-1.0.0-beta1-win-x64.zip`。
6. 在发布说明中列出适用系统、需自行申请阿里 API、已知兼容性限制；不要承诺所有应用均已稳定支持。

Releases 的 ZIP 面向直接使用的朋友；仓库源码面向希望补充功能的开发者。后续贡献通过 Issues 与 Pull Requests 收集。

仓库地址：https://github.com/NHT-ICE/english-learning-assistant
