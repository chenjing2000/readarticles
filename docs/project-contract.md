# ReadArticles 项目契约卡

## 关键行为与数据

- 启动显示折叠文章库，选择文章后读取文章及配套词汇 JSON。编辑只写当前配套词汇文件。真实文章库由用户选择，必须使用工作副本进行破坏性测试。见[文件约定](Library-Layout.md)。
- 文章音频与词、句发音使用不同播放通道并协调互斥。MP3 存在时优先，解码失败不回退 WAV。句子录音仅在用户请求播放该句时检查；缺失提示只针对当前 SID 和当前口音，不在文章加载时扫描或汇总。
- 文章正文 JSON 和音频文件由 ReadArticles 只读；词汇伴随 JSON 是应用管理的用户数据，保存时必须保留现有格式、并发修改检测和原子写入保护。

## 技术与运行环境

- C# / WPF，.NET 10，Windows x64。
- Framework-dependent 单文件发布，不启用 trimming。运行需要 .NET 10 Windows Desktop Runtime x64，补丁下限见 [README](../README.md)。
- 采用便携式目录部署。程序目录必须位于当前用户有写权限的位置；不自动迁移用户数据到 `%LOCALAPPDATA%`。

## 目录与用户数据

- 发布入口：`dist/ReadArticles/ReadArticles.exe`。
- 用户数据根目录：可执行文件同级的 `userdata/`。
- 设置：`userdata/config/settings.json`。
- 设置备份位于 `userdata/backups/settings/`，每个设置文件只保留最近一个有效版本。
- 词汇备份位于 `userdata/backups/vocabulary/`，每个源词汇文件只保留最近一个有效版本；备份标识由规范化完整源路径的 SHA-256 生成。
- `userdata/cache/` 和 `userdata/logs/` 将在后续阶段启用。
- 外部文章库和音频仍由用户选择，绝不自动移动到程序目录或 userdata。
- 旧版 EXE 同级设置文件迁移到新位置时保留原文件；新版与旧版配置同时存在且内容不同时，优先读取新版并向用户显示警告。

## 构建与交付

- `\.\tools\Publish.ps1` → `dist\ReadArticles\ReadArticles.exe`。更新仅替换程序文件；保留 `dist/ReadArticles/userdata/`，旧 `dist/userdata/` 仅验证复制、不自动删除。
- 发布采用临时暂存、验证和同卷替换；失败时保留现有程序。
- 不采用安装器、自包含运行时或在线更新器。

## 验证

- `\.\tools\Test.ps1 -Suite Core`、`-Suite UI` 或 `-Suite Full`，统一从 `tests/TestData/` 读取固定测试资源；临时副本位于系统临时目录，输出位于 `artifacts/`。
- 发布后运行 `\.\tools\Test-PublishedApp.ps1`。
- 业务和控件检查之外，用真实库验证媒体匹配、播放切换、词汇保存和实际声卡输出。见[测试说明](Testing.md)。

[项目说明](../README.md) · [专属检查](../docs/skills/project-code-review/SKILL.md)
