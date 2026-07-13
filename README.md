# 通用知识库智能体 · Electron 版

这是一个不绑定学校、企业、政府部门或行业的通用知识库智能体。工程完全使用 Electron/TypeScript，不依赖 .NET 运行时。

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

仓库只提供源码，不附带 Logo、应用图标、PDF/知识库文档、模型、运行时 DLL、EXE 或构建产物。首次执行 Embedding / ASR 时，Transformers.js 会把 ONNX 模型下载至本地模型缓存；下载完成后可离线运行。Embedding 模型不可用时会自动切换到 384 维本地 Hash 检索。

管理员可以在运行后自行选择 Logo 和桌宠资源。`resources/`、`src/renderer/public/` 以及常见图片、文档、音视频格式均被 Git 忽略，不会误提交用户资产。未配置 Logo 时界面显示纯 CSS 文字占位，托盘使用操作系统提供的应用图标。

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
- `resources`：用户自行准备的桌宠资源及本地运行时（不纳入仓库）
