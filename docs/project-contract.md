# ReadArticles 项目契约卡

## 关键行为与数据

- 启动显示折叠文章库，选择文章后读文章及配套词汇 JSON。编辑只写当前配套词汇文件。用工作副本验证，真实库由用户提供。见[文件约定](Library-Layout.md)。
- 文章音频与词、句发音使用不同播放通道并协调互斥。匹配规则以 [README](../README.md) 为准，MP3 存在时优先，解码失败不回退 WAV。句子录音仅在用户请求播放该句时检查；缺失提示只针对当前 SID 和当前口音，不在文章加载时扫描或汇总。

## 构建与交付

- .NET 10 SDK 构建，Windows x64 框架依赖单 EXE，运行需 .NET 10 Windows Desktop Runtime x64。补丁下限见 README。
- `.\tools\Publish.ps1` → `dist\ReadArticles.exe`，可用 `--library` 指定文章库。

## 验证

- `.\tools\Test.ps1 -Suite Core`、`-Suite UI` 或 `-Suite Full`，统一从 `tests/TestData/` 读取固定测试资源；临时副本位于系统临时目录，输出位于 `artifacts/`。发布后运行 `.\tools\Test-PublishedApp.ps1`。
- 业务和控件检查之外，用真实库验证媒体匹配、播放切换、词汇保存和实际声卡输出。见[测试说明](Testing.md)。

[项目说明](../README.md) · [专属检查](../docs/skills/project-code-review/SKILL.md)

