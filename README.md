# Knowledge Base QA Agent

一个面向 Windows 展厅、前台和触摸屏场景的知识库问答小程序。它把文档检索、语音输入输出和一个常驻桌面的小助手放在一起：访客点一下就能问，管理员按 `Ctrl+1` 进入配置。

项目现在还是 WPF/.NET 9 桌面应用，重点放在离线可跑、部署简单和对 OpenAI-compatible 服务的兼容上。

## 能做什么

- 导入 PDF、Word、文本和网页内容，建立本地 SQLite 知识库
- 通过向量检索把上下文交给大模型回答，并保留引用来源
- 支持常见 OpenAI-compatible 聊天、Embedding、ASR 和 TTS 服务
- 可选 Windows TTS、本地 sherpa-onnx/VITS/Kokoro，或云端语音服务
- 透明桌面宠物、触摸问答窗口和语音唤醒入口
- 普通安装版使用 Windows Credential Manager；便携版保存加密后的配置

## 开发环境

- Windows 10/11
- .NET SDK 9
- 可选：WiX Toolset（构建 MSI 时需要）

```powershell
dotnet build KnowledgeBaseQaAgent.sln
dotnet test KnowledgeBaseQaAgent.sln
```

发布自包含的 x64 版本：

```powershell
dotnet publish src\KnowledgeBaseQaAgent.Desktop\KnowledgeBaseQaAgent.Desktop.csproj `
  -c Release -r win-x64 --self-contained true
```

构建 MSI：

```powershell
dotnet build installer\KnowledgeBaseQaAgent.Installer\KnowledgeBaseQaAgent.Installer.wixproj -c Release
```

## 关于本地语音模型

模型、运行时下载和构建产物都不在仓库里。它们体积大、更新快，也各自带有独立的许可条件。需要本地语音时，请自行下载兼容的 sherpa-onnx/VITS/Kokoro 模型，并在管理端把可执行文件和模型路径填进去。

同样地，`Data` 目录、密钥、SQLite 知识库和便携版配置只属于本机，不应提交。

## 目录

```text
src/        WPF 应用
tests/      单元测试
installer/  WiX MSI 安装工程
scripts/    发布脚本
tools/      语音接入试验脚本
```

## 协议

本项目以 [MIT License](LICENSE) 发布。
