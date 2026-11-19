# ReadArticles — 开发交接（2026-11-19）

## 当前稳定基线

- **仓库与正式分支：** [chenjing2000/readarticles](https://github.com/chenjing2000/readarticles) 的稳定开发基线是 `main`。本轮三栏布局 PR [#18](https://github.com/chenjing2000/readarticles/pull/18) 已于 2026-11-19 **squash merge**，功能合并提交为 `71b5cd188ae344ef013d3e8a8cd7d349656dfbae`；此 HANDOFF 的后续文档提交位于其上。先前 PR [#17](https://github.com/chenjing2000/readarticles/pull/17) 已合并，#16 已合并，#15 已关闭；不要从旧迁移分支继续开发。
- **本轮交付：** 三栏宽度和两条分隔条行为已经按用户确认的规则统一：左侧 WPF 书库栏、WebView2 文章中栏和词汇右栏双向联动。清除了旧 WPF `GridSplitter` 的自动宽度竞争、CSS 动态右栏压缩和 JavaScript 抢先修改最终右栏宽度。C# 是唯一三栏宽度求解者，浏览器应用 C# 返回的权威右栏尺寸。
- **PR #18 合并前验证：** 功能分支最终提交 `22341c5264a8a8b7605233c9a91833b1894c5c17` 的 [GitHub Windows CI #44445487538](https://github.com/chenjing2000/readarticles/actions/runs/44445487538) 为 **Success**。用户提供的 Windows Release 构建成功；Core **10/10** 组、UI **13/13** 组、Full **6/6** 组（应用回归 **96/96**、源文件保护 **2/2**）。此前 `left splitter keyboard width survives window resize` 失败项最终已 **PASS**；用户另外确认左右分隔条视觉跳动已消除，**本轮实机交互验收通过**。这些结论针对 PR #18 最终功能提交，不能替代未来改动的测试。合并后 `main` 与此文档提交的 CI 状态应另行核对，不要沿用 PR CI 作为其结果。
- **前一阶段记录：** PR #17（合并提交 `3fc6e217c94313535579788fb179160b5f17f451`）完成键盘焦点、WebView2 词汇 Enter/Space、设置无变化跳过写入及备份轮换、遗留 WPF 命令清理和相关回归；此前用户完成发布 EXE **20/20** 项实机验收。见历史 PR 和 [测试文档](Testing.md)，不重演已合入的旧补丁。
- **Git/工作区边界：** PR #18 已合并；其原功能分支 `fix/three-pane-width-constraints` 未在本次操作中删除。远程 `feature/exercise` 属于独立工作，**必须保留**。当前只可确认 GitHub 远程状态，不能据此断言用户本地 `main` 已同步或 `git status` 为空。下一轮请先 `git fetch origin`，检查本地未提交更改，再更新 `main` 和建立新功能分支；旧版 postmigration 分支及 V2 stash 的清理属于历史记录。
- **后续操作约定：** 优先通过已连接的 GitHub 工具直接修改并提交到获准功能分支，再由用户在 Windows 本地构建、测试和验收。修改、推送、PR、合并及分支清理分别遵循当轮授权；不要未经同意删除其他分支。

## 最终架构与职责

- **WPF / .NET 10 / Windows x64：** Windows 宿主、左侧异步逐层加载的书库树、系统对话框、应用生命周期和原生错误/退出界面。旧 WPF 正文、词汇编辑界面、工具栏、Toast、选区 Adorner 与重复的 C# 文本匹配器已删除；没有第二套阅读器。
- **唯一 WebView2：** Passage/Script 阅读、右侧词表与工具栏、文本匹配/高亮/定位、选区添加、独立滚动、消息通知及 Script 的 SVG 逐词着色。WebView2 不直接访问或改写用户 JSON，也不使用 HTML Audio。
- **C# 业务：** 文章/Script/词汇 JSON 解析和校验、音频资源路径与播放时钟、词汇新增/移动/删除/导入的事务提交、外部改动检测、原子保存、备份恢复、FIFO 通知、配置及日志。文章和媒体只读；仅当前词汇伴随 JSON 可由编辑操作改写。
- **数据与发布：** Framework-dependent、非 trimming、Windows x64 便携式单文件 EXE；发布到 `dist/ReadArticles/ReadArticles.exe`，用户设置和备份位于 EXE 同级 `userdata/`。设置路径 `userdata/config/settings.json`；左右面板宽度仅当前会话有效。WebView2 Evergreen Runtime 和 .NET 10 Windows Desktop Runtime 为运行依赖。
- **必须保留的边界：** 切换文章失败保留已提交状态；目录扫描不阻塞 UI，折叠/切库使旧请求失效；词汇写入必须保留未知字段、并发检测及备份；音频旧回调不能改变新的播放状态。MP3 存在时优先，解码失败不自动回退 WAV；句子录音只在请求时检测。WebView2 初始化、进程或渲染失败时显示原生错误及退出界面，不回退旧 WPF 阅读器。
- **Script：** 同目录 `<name>.script.json` 按需加载，必须符合 `filetype: "script"`、字符串 `title`、`paragraphs`、`text`、`timed_units` 的约定。未对齐单元不着色，且不改变 Passage 的 SID、词表或全篇音频来源。


## 三栏布局约定（PR #18）

- **尺寸：** 左侧图书馆默认 **250 DIP**、范围 **200–400 DIP**；文章中栏 **至少 200 DIP**、宽度自适应；词汇右栏默认 **300 DIP**、范围 **200–400 DIP**。两条分隔条各 **3 DIP**，所以 WPF 中 WebView2 宿主必须至少有 **403 DIP**（文章 200 + 右分隔条 3 + 词汇 200）。窗口启动居中，高度约为工作区的 70%，宽度默认约为工作区的 70%，有空间时不少于 **796 DIP**、不能超过工作区宽度。
- **精确的双向规则：** 拖动左分隔条，先压缩/扩大**中栏**；中栏压至 200 DIP 后才压缩**右栏**。拖动右分隔条同理，优先改变**中栏**，再压缩**左栏**。反向拖动先恢复中栏、另一侧外栏维持**当前值**，不强制重置到 250/300。三栏均达到 200 DIP 时，不可继续向压缩方向拖动。窗口本身变窄时同样遵循最小值并调整到可行布局。
- **唯一控制入口：** `ReadArticles/MainWindow.Settings.cs` 的 `AvailablePaneWidth`、`SetPaneWidths`、`ResizeLibraryPane`、`ResizeVocabularyPane` 及 `WorkspaceResized` 负责实际宽度计算和 WPF 左栏更新。左分隔条是 WPF `Thumb`，其鼠标和键盘事件交给相同求解器。右分隔条位于唯一 WebView2 页面里，通过 `dragPanelWidth`、`setPanelWidth` 与 `cancelPanelDrag` 向 C# 提交意图，C# 用 `setPaneWidth` 给浏览器返回确认值；浏览器不再先行设置最终宽度。JS 右侧拖动使用屏幕坐标，避免 WPF 宿主移动产生坐标反馈。
- **浏览器和状态边界：** `ReadArticles/web/reader.css` 仅根据 C# 确认的 `--word-width` 渲染词汇栏，**不要重新引入**独立的 `clamp(... calc(100% - 203px))` 或新的浏览器宽度求解器；CSS 保留最小文章宽度等安全约束。`ReaderSettings.LeftWidth/RightWidth` 均标记 `[JsonIgnore]`，仅会话有效，重启恢复默认值；不可让面板拖动写入 `userdata/config/settings.json` 或触发无意义的备份轮换。
- **必测路径：** 两条分隔条向两个方向慢速/快速拖动、文章到 200 DIP 后另一侧才压缩、反向拖动不回弹、三栏最小值停止、改变窗口宽度、左右键键盘操作、浏览器实际 DOM 宽度与 C# 一致。现有覆盖在 `ReaderControlsChecks.cs`、`WebViewPilotChecks.cs`、`WebViewSettingsChecks.cs` 和目录树测试里；视觉无跳动仍需实机检查。

## 下一轮工作入口

1. 阅读根目录 `AGENTS.md`、`docs/project-contract.md`、`docs/Testing.md`、`docs/Publishing.md`、`docs/skills/project-code-review/SKILL.md`；WebView2 迁移历史与技术取舍见 `docs/WebView2-Migration.md`。按需参照 WindowsAppPlaybook，贯彻最小必要设计，删除不必要复杂性，避免为精简而牺牲数据安全。
2. 核对 `git status --short`、`git log -1 --oneline`、远程分支状态；本地如无阻塞的未提交改动，可用 `git switch main` 与 `git pull --ff-only origin main` 同步。以 PR #18 合并后的实际源码为依据，不重放旧的三栏宽度补丁、V2 补丁或历史 stash；`feature/exercise` 不得随意改动或删除。
3. 新一轮整改先确认目标与验收边界，在独立分支提交最小改动及相关测试。Windows 验证入口：`dotnet build ReadArticles.Tests -c Release`、`.\tools\Test.ps1 -Suite Core`、`-Suite UI`、`-Suite Full`；发布使用 `.\tools\Publish.ps1`、`.\tools\Test-PublishedApp.ps1`，对**实际发布的 EXE**再次进行交互验收。涉及数据修改一律使用隔离文章库副本。
4. Windows 回归、GitHub CI、人工验收及最终 PR 差异审核通过后，取得明确授权方可合并到 `main`；合并后核对 `main` 新一轮 CI，再按独立授权清理临时分支或 stash。

## 相关文档

- [架构与模块](Application-Overview.md) · [文件格式及目录约定](Library-Layout.md) · [测试与人工验收](Testing.md) · [发布](Publishing.md)
- [WebView2 迁移历史](WebView2-Migration.md) · [项目代码审查 Skill](skills/project-code-review/SKILL.md)

**历史内容提醒：** 早期第六、七阶段 WPF 视觉测试、WebView2 试点开关和旧 PR 分支是历史材料；不要按旧交接中的 `--webview-pilot`、旧 WPF 阅读器或过期开发分支命令执行下一轮工作。
