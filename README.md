# 知识库智能助手 · Electron 重构版

这是原 WPF 项目的完全 Electron/TypeScript 重构。工程内没有 C# 源码，也不依赖 .NET 运行时。

## 已实现

- React 管理端、访客端、透明桌宠窗口、系统托盘及全局快捷键
- 8 个可编辑预设问题，自定义标题、Logo、问候语、桌宠序列帧与台词
- 管理端密码保护，默认密码为 `123456`，登录后可修改
- 70 个模型服务商配置项；OpenAI 兼容、Anthropic、Gemini 协议可直接调用
- 本地 Embedding、ASR 和单一 VITS TTS 兜底；Embedding 与 ASR 也可切换远程服务
- SQLite 知识库、文件导入、RAG 检索与 AES-GCM API 密钥存储

## 本地开发

```powershell
npm install
npm run dev
```

仓库是纯源码分支，不包含私有 Logo、桌宠帧、PDF/知识库文档、模型、运行时 DLL 或 EXE。首次执行 Embedding / ASR 时，Transformers.js 会把 ONNX 模型下载至本地模型缓存；下载完成后可离线运行。Embedding 模型不可用时会自动切换到 384 维本地 Hash 检索。

本地开发时可自行放置 `resources/brand/`、`resources/pet/` 和 `src/renderer/public/`，这些目录及常见图片、文档、音视频格式已被 Git 忽略，不会误提交个人资产。

如需本地 TTS，请从现有 portable 的 `Tools/VITS` 导入当前唯一使用的 `sherpa-onnx-vits-zh-ll`：

```powershell
npm run prepare:runtime -- "C:\path\to\Tools\VITS"
```

不传路径时，脚本会尝试读取本项目维护环境中的旧 portable 目录。导入的 `resources/runtime/tts/` 已被 Git 忽略，不会提交模型或二进制文件。

## 构建

```powershell
npm run build
npm run package
```

数据默认保存在 Electron userData 目录；便携包存在 `portable.flag` 或使用 `--portable` 时保存到相邻的 `Data` 目录。SQLite 表结构与旧版知识库兼容。

## 架构

- `src/main`：Electron 主进程、SQLite、RAG、LLM、本地模型与 VITS
- `src/preload`：最小化、类型安全的 IPC 桥
- `src/renderer`：React 访客端、管理端与透明桌宠窗口
- `resources`：品牌、CC0 桌宠帧及本地运行时
