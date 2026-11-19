# ReadArticles — 开发交接（2026-11-19）

## 当前稳定基线

- **仓库及分支：** [chenjing2000/readarticles](https://github.com/chenjing2000/readarticles)，正式基线为 `main`，提交 `3fc6e217c94313535579788fb179160b5f17f451`（PR [#17](https://github.com/chenjing2000/readarticles/pull/17) 已于 2026-11-19 完成 squash merge）。历史 PR #16 已合并，#15 已关闭；不要再从旧迁移分支开发。
- **交付状态：** PR #17 的键盘焦点修复、WebView2 词汇 Enter/Space 操作、设置无变化跳过写入及备份轮换、会话级面板宽度不持久化、遗留 WPF 词汇命令清理、高亮通知简化，以及相应测试和 CI 整理均已合入 `main`。
- **验收证据：** 合并前针对 `e45c1dd` 的 Windows Release 构建成功；Core 10/10 组，UI 13/13 组，Full 6/6 组（应用回归 93/93、测试源文件不变 2/2），发布及 `Test-PublishedApp.ps1` 4/4；用户使用最终发布 EXE 完成 **20/20 项 Windows 实机验收**，覆盖目录树键盘、Passage/Script、词汇操作、音频、设置及退出。合并后 `main` 的 [Windows regression tests](https://github.com/chenjing2000/readarticles/actions/runs/44425315302) 为 **Success**。这些是本轮基线的结果，不自动证明未来修改通过。
- **Git 收尾：** 用户已将本地 `main` 快进至 `3fc6e21`，`git status --short` 为空；本轮本地及远程 `fix/postmigration-review*` 分支已删除，旧 V2 stash 已清空。远程 `feature/exercise` 是独立工作，**保留，不属于本轮清理范围**。
- **下一轮开发约定：** 先检查当前 GitHub `main` 和工作区；从 `main` 创建独立功能分支。优先通过已连接的 GitHub 工具**直接修改、提交到远程功能分支**，用户在本地 `git fetch` / `git pull` 后进行 Windows 构建与实机验收；不再默认要求用户下载脚本或手工应用补丁。修改、提交、PR、合并与清理由用户当轮授权；未经同意不合并或删除其他分支。

## 最终架构与职责

- **WPF / .NET 10 / Windows x64：** Windows 宿主、左侧异步逐层加载的书库树、系统对话框、应用生命周期和原生错误/退出界面。旧 WPF 正文、词汇编辑界面、工具栏、Toast、选区 Adorner 与重复的 C# 文本匹配器已删除；没有第二套阅读器。
- **唯一 WebView2：** Passage/Script 阅读、右侧词表与工具栏、文本匹配/高亮/定位、选区添加、独立滚动、消息通知及 Script 的 SVG 逐词着色。WebView2 不直接访问或改写用户 JSON，也不使用 HTML Audio。
- **C# 业务：** 文章/Script/词汇 JSON 解析和校验、音频资源路径与播放时钟、词汇新增/移动/删除/导入的事务提交、外部改动检测、原子保存、备份恢复、FIFO 通知、配置及日志。文章和媒体只读；仅当前词汇伴随 JSON 可由编辑操作改写。
- **数据与发布：** Framework-dependent、非 trimming、Windows x64 便携式单文件 EXE；发布到 `dist/ReadArticles/ReadArticles.exe`，用户设置和备份位于 EXE 同级 `userdata/`。设置路径 `userdata/config/settings.json`；左右面板宽度仅当前会话有效。WebView2 Evergreen Runtime 和 .NET 10 Windows Desktop Runtime 为运行依赖。
- **必须保留的边界：** 切换文章失败保留已提交状态；目录扫描不阻塞 UI，折叠/切库使旧请求失效；词汇写入必须保留未知字段、并发检测及备份；音频旧回调不能改变新的播放状态。MP3 存在时优先，解码失败不自动回退 WAV；句子录音只在请求时检测。WebView2 初始化、进程或渲染失败时显示原生错误及退出界面，不回退旧 WPF 阅读器。
- **Script：** 同目录 `<name>.script.json` 按需加载，必须符合 `filetype: "script"`、字符串 `title`、`paragraphs`、`text`、`timed_units` 的约定。未对齐单元不着色，且不改变 Passage 的 SID、词表或全篇音频来源。

## 下一轮工作入口

1. 阅读根目录 `AGENTS.md`、`docs/project-contract.md`、`docs/Testing.md`、`docs/Publishing.md`、`docs/skills/project-code-review/SKILL.md`；WebView2 迁移历史与技术取舍见 `docs/WebView2-Migration.md`。按需参照 WindowsAppPlaybook，贯彻最小必要设计，删除不必要复杂性，避免为精简而牺牲数据安全。
2. 核对 `git status --short`、`git log -1 --oneline`、远程分支状态。以最新实际源码为依据，不重放已合入的 V2 补丁或历史 stash；`feature/exercise` 不得随意改动或删除。
3. 新一轮整改先确认目标与验收边界，在独立分支提交最小改动及相关测试。Windows 验证入口：`dotnet build ReadArticles.Tests -c Release`、`.\tools\Test.ps1 -Suite Core`、`-Suite UI`、`-Suite Full`；发布使用 `.\tools\Publish.ps1`、`.\tools\Test-PublishedApp.ps1`，对**实际发布的 EXE**再次进行交互验收。涉及数据修改一律使用隔离文章库副本。
4. Windows 回归、GitHub CI、人工验收及最终 PR 差异审核通过后，取得明确授权方可合并到 `main`；确认合并后再清理临时分支和 stash。

## 相关文档

- [架构与模块](Application-Overview.md) · [文件格式及目录约定](Library-Layout.md) · [测试与人工验收](Testing.md) · [发布](Publishing.md)
- [WebView2 迁移历史](WebView2-Migration.md) · [项目代码审查 Skill](skills/project-code-review/SKILL.md)

**历史内容提醒：** 早期第六、七阶段 WPF 视觉测试、WebView2 试点开关和旧 PR 分支是历史材料；不要按旧交接中的 `--webview-pilot`、旧 WPF 阅读器或过期开发分支命令执行下一轮工作。
