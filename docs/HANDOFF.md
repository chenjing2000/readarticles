# ReadArticles — 开发交接 / Handoff（2026-11-18）

> **当前更新（2026-11-18）**：**第七阶段 UI / Full 回归已通过 Windows 验收。** 在 `021666d` 上 Release 测试构建成功、无警告；`Test.ps1 -Suite UI` **13/13 组通过**（window-only 15/15、controls-only 18/18）；`Test.ps1 -Suite Full` **6/6 组通过**，完整应用回归 **130/130 项通过**，源文件保护 **2/2 项通过**。旧 WPF Script 粉色词汇背景原有像素断言现已真正通过（`local=1590;global=1500;layer=1500`，开启绘图非空、关闭后为空）；最终修复是 `DrawingVisual` 保留式绘制，不再需要继续排查原 `OnRender`。第六阶段用户实机功能验收也已完成。发布及改名安装测试曾在较早提交通过，但**最新代码的便携式发布版 WebView2 功能实机验收尚未完成**；旧 WPF 回退界面与 Draft PR #16 均保持不变。查看 [`WebView2-Migration.md`](WebView2-Migration.md)。
>
> **新会话入口**：先读本文件、[`WebView2-Migration.md`](WebView2-Migration.md)、[`AGENTS.md`](../AGENTS.md) 及 [`project-contract.md`](project-contract.md)。目标仍是单个 WebView2 承载正文、词表、工具栏与通知，C#/WPF 保留左侧导航、音频与词汇文件的最终管理；当前已部分实现，不得误认为完成迁移。
>
> **当前工作目标**：先基于最新分支复验 `Publish.ps1` 和 `Test-PublishedApp.ps1`，然后以便携式 `dist\ReadArticles\ReadArticles.exe --webview-pilot` 实机检查文章/Script 切换、词汇编辑、音频、三槽通知、分栏重启恢复默认和 WebView2 失败回退。确认后制定逐文件删除旧 WPF 阅读视觉代码的审查清单；保留 WPF 左目录树及 C# 业务/测试，旧视图未获允许不得提前删除，PR 不合并。

## 1. 仓库、分支和实际状态

- GitHub：`chenjing2000/readarticles`；用户 Windows 本地工作目录：`C:\Projects\ReadArticles`。
- 当前开发分支：`feature/webview2-reader`，Draft [PR #16](https://github.com/chenjing2000/readarticles/pull/16)，基于 `feature/script-reading-mode`；其 Draft [PR #15](https://github.com/chenjing2000/readarticles/pull/15) 仍未合并到 `main`。开始新任务先检查远程 HEAD、PR 状态和本地 `git status`。
- `main` 仍是原有 WPF 阅读方案；WebView2 工作区已加入 `feature/webview2-reader` 分支，**尚未合并到 `main`**。过去的 `feature/exercise` 为历史性功能分支，不属于当前 WebView2 迁移范围。
- 上一轮已在功能分支实现可按需切换的 Script 模式及 WPF 的多次渐变/高亮修复，旧版 WPF UI 回归及 Script 像素高亮问题后来已全部修复并通过验收（见本文件最新状态及迁移记录）。不得直接宣称 PR #15 已达到合并条件。
- 本文件是**交接说明和设计决定**，不是迁移代码或测试通过的证明；不隐含合并授权。

## 2. 用户确认的目标架构与边界

### 已确认的决定

1. **保留 C# / WPF / .NET 10 桌面宿主和左侧目录树**，包括逐层加载、文章选择、文件系统访问、设置、数据校验、音频播放、词汇持久化及现有安全恢复机制。
2. 在左侧文章库之外，使用**单个 WebView2**统一承载完整阅读工作区：**中央标题及 Passage/Script 正文、右侧词汇列表、两区底部工具栏、选区添加按钮、网页内浮动 Toast**。中央与右侧由 HTML/CSS 布局和分隔拖动管理，避免 WPF/WebView2 跨渲染引擎传递选区矩形。
3. **由 JavaScript 负责正文内的词汇匹配和定位**，输入为 C# 发送的正文与词汇数据；由 JavaScript 根据实际 DOM 文本查找并高亮，处理点击词汇后正文滚动定位、选区、选词按钮、普通正文和 Script 的交互。
4. **由 JavaScript 处理文本选择，点击“添加”时才把选区快照与会话标识发送给 C#**；C# 负责去重、权限、校验、保存和返回结果。网页不得直接写词汇文件。
5. **C# 是唯一的音频时钟和音频执行者**；网页只渲染当前播放时间并发送播放/暂停/Seek/词音/句音等意图。保留现有两条协调的音频通道，不改为 HTML Audio。
6. 浮动通知由 **HTML/CSS 绘制**，但 Info/Warning/Error 分类与 FIFO 队列由 C# 持有，WebView2 负责至多三张卡片的悬停与动画；重复事件仍逐条入队，错误必须手动关闭。WebView2 尚未初始化或崩溃时的关键错误须有原生 WPF 兜底。
7. **暂不引入 React、Node.js 或任何前端构建工具**；优先原生 HTML/CSS/JavaScript。优先验证普通 `WebView2`；仅在确实需要 WPF 控件覆盖网页等场景时再评估 `WebView2CompositionControl`。
8. 遵循第一性原理：迁移期允许旧 WPF 与新 WebView2 两套阅读界面短期共存以确保回退；新方案全面验收后，删除失去用途的 `ParagraphReader` / 旧右侧列表 / 旧 Toast **视觉实现**与重复匹配逻辑，不长期保留两套正式阅读器。

### 明确保留在 C# 的业务

- 打开、校验 Passage / Script / vocabulary JSON，导航、路径及故障隔离；正文及音频文件只读。
- 词汇新增、导入、删除、上移/下移的**最终提交**，外部文件修改检测、互斥写入、原子替换、验证备份和恢复。
- 文章音频以及单词 UK/US、句子 SID 发音；暂停、恢复、终止、Seek、播放器所有权。
- 持久化设置、用户数据、日志、通知业务状态；词汇保存失败不得导致网页假显示成功。

## 3. Script 语义、原型结果和当前已知缺陷

### 必须遵守的 Script 数据契约

- 普通 Passage 同目录可有同名 `<name>.script.json`，只在用户主动切换 Script 时加载；Script 不是新的书库节点，找不到或无效时保留原 Passage。
- 根节点要求 `"filetype": "script"` 和字符串 `"title"`（可为空）；`paragraphs` 为段落数组，每段是句子数组，句子有 `"text"` 和 `"timed_units"`。
- 单个时间单元 `["non-identical", startSeconds, endSeconds]`；一段文字**只依据两个时间端点**计算线性进度 `clamp((t-start)/(end-start),0,1)`。不增加中间时间点，不拆分 JSON 时间单元；其中连字符处自动换行，第一行先连续着色，之后才进入第二行。
- `timed_units` 对齐失败不拒绝整篇 Script，未对齐单元不着色；Script 与 Passage 正文不要求相同；空 title 不显示标题；Script 没有真实的 Passage SID 句子发音或 blank 占位符。
- Script 的词汇列表和全篇音频来源仍关联原 Passage；文章停止或自然结束时清除 Script 着色，Seek 后按当前时间重新着色。

参阅 [`Library-Layout.md`](Library-Layout.md) 和 [`Testing.md`](Testing.md)。

### 已完成的独立 Web 原型验证（不在仓库）

- 之前在本会话制作了独立 `prototype.html` / ZIP 原型，使用 Chromium 的 DOM `Range.getClientRects()` 和 CSS/SVG 可视进度，而非 React。
- **用户已在本机验证**：`non-identical` 的 `non-` 与 `identical` 在两行上连续着色、自动换行、词汇背景高亮都符合预期。
- 这个网页原型**尚未集成 WebView2/C#，也没有提交到 GitHub 仓库**。新会话若无法读取上次会话附件，应由用户重新提供原型，或依据本节与规范重建；不要假设本地或仓库已经有 `prototype.html`。
- 迁移到生产版时移除原型的模拟时钟、演示按钮、演示词汇输入框和诊断面板；只保留经过验证的渲染核心。

### 现有 WPF Script 分支验证状态（不要混淆）

- 上次用户本地在 `86649ac` 上运行 `--script-only` / UI / Full，跨行红色像素结果为 `first early=64, last early=0, last late=417`，表明该**测试案例**的跨行红色顺序已正确。
- 同轮仍报 `Script vocabulary highlight changed pixels: 0`；`ScriptChecks.cs` 中一项 UI 断言失败，`--script-only` 为 7 passed / 1 failed；UI / Full 因同一项失败。随后在 `93c0b0e` 提交了 Adorner 及窗口截图测试调整，但**没有获得这次提交的本地 UI/Full 通过记录**。
- GitHub Windows Core CI 对 `93c0b0e` 曾成功；**Core CI 不执行实际交互式 Windows UI 视觉验收**。不应把该成功等同于当前 WPF UI 全部通过。
- 后续主方向是 WebView2 替换正文与词汇渲染，勿继续对旧 WPF 着色器作无目标补丁；仍须保留针对跨行、词汇、选择、回归的真实测试要求。

## 4. 推荐职责划分和最小消息边界

### WebView2 内部完成（无需每次跨 C# 边界）

- 文章标题、段落、句子、Script 时间单元的 DOM 排版；CSS 布局、自动换行、阅读滚动与行内渐变。
- 词汇匹配、匹配范围定位、背景高亮、焦点切换、从词汇列表滚动到正文；选区及浮动“添加”按钮。
- 词汇列表的视觉布局、词条音标/释义、显示状态、内部上下文菜单；工作区左右分栏及其底部工具栏样式；浮动 Toast 绘制和动画。
- 同一篇文章播放期间**只更新进度和必要样式**，不反复发送整个正文或重建 DOM，以保护文字选择、滚动和视觉稳定性。

### C# → JavaScript（可按需合并初始化快照）

- `initialize/loadArticle`：文章会话标识、模式（Passage/Script）、标题、正文及 Script 时间单元、SID/blank 等合法的展示标记。
- `setVocabulary`：最新词条及音标/释义/UK-US 音频资格；数据来源为 C# 权威快照。
- `setPlayback`：文章播放时钟、播放/暂停/停止、Seek 后的位置、当前句子/词发音状态（需要时）。
- `setSettings`：字号、口音、显示/隐藏词汇高亮、可恢复的左右宽度等。
- `setNotifications`、`actionResult`：通知状态与变更操作成功/失败、必要的最新权威快照。

### JavaScript → C#（只传意图，不让页面直接执行业务）

- `addWord`：所选正文文本及其句子/段落归属、位置范围、**文档会话 ID**；显示“添加”按钮无需发消息。
- `moveWord/deleteWord/importVocabulary`：以稳定词条身份或当前受校验索引提交请求；C# 执行并在保存成功后回传权威词汇快照。
- `playArticle/stopArticle/seekArticle/playWord/playSegment`：明确音频动作及当前会话；C# 验证 SID、口音、音频资格并控制原有播放器。
- `switchReadingMode/setHighlight/setFontSize/setAccent/setPanelWidth`：需要保存或改变权威状态的设置/行为。
- `notificationDismiss` 等：通知交互事件交回 C# 状态管理。网页可自主管理局部动画和选区工具显示。

### 重要契约与安全要求

- **握手与版本**：WebView2 初始化完成后发送 `ready`，由 C# 推送快照；每篇文章有新的会话 ID，网页操作可带请求 ID，丢弃旧文章或旧加载轮次的过期事件。不要因页面刚加载而发送播放/写入动作。
- **不乐观提交用户数据**：词汇更改、文件导入或删除只有 C# 保存成功后才最终反映为已提交状态；失败保留原列表并显示错误。
- **词汇匹配语义兼容**：移植现有测试覆盖的最早匹配、同起点最长词组优先、直/弯撇号、连字符、数字/单词边界语义；不能直接以简单的 JS `\b` 正则替代既有行为。用 C# 现有用例建立对照后，最终删除失去用途的旧匹配实现。
- **选区兼容**：正文选区必须保留复制、选择起止位置、跨段限制；Passage 的 SID 和 blank 语义不能丢。Script 不得捏造可播放句子 SID；涉及 Script 选词归属与新增词汇时，优先复用现有保存通道并在新开发阶段落实验证规则。
- **网页安全边界**：资源为应用自带的可信静态 HTML/CSS/JS；文章及词汇文本以 text nodes / `textContent` 等安全方式渲染，禁止把文章内容作为 `innerHTML` 执行；限制导航和不必要的外部请求，消息动作采用白名单，C# 必须验证消息来源、类型、字段、权限及会话。
- **键盘和分栏**：网页焦点中的空格播放/暂停、左右方向键 Seek、Ctrl+C 复制等现有交互都应核对；左侧 WPF 分栏与网页内部正文/词汇分栏有独立、稳定的布局及设置保存逻辑。
- **Toast**：保留通知队列、newest-first、Info/Warning 自动过期、Error 不自动消失和悬停行为；WebView2 初始化/崩溃等不可由网页显示的错误，用 WPF 兜底。
- **便携部署**：保留 .NET 10 / Windows x64 framework-dependent 单文件及 `userdata/` 契约。WebView2 Evergreen Runtime 的可用性与缺失提示需要验证；WebView2 用户数据目录选址、更新迁移和清理策略在正式实施前核定（`userdata/cache/` 当前仅预留，不能假设已使用）。

## 5. 现有源码入口与预计改造范围

| 源码/文档 | 现状与新会话关注点 |
|---|---|
| `ReadArticles/MainWindow.xaml` | 左侧 WPF TreeView，中央 `PassageScroll` / `ArticleTitleText` / `ParagraphHost`，右侧 `VocabularyList`，两区底部按钮，中央 `NotificationHost`。后续在左侧树外使用**一个 WebView2 工作区**；不保留平行的两个 WebView2。 |
| `ReadArticles/MainWindow.Library.cs` | 打开/切换 Passage/Script、`ShowContent`、构建多个 `ParagraphReader`、恢复正文滚动。下一阶段转换为向 WebView2 提交文档快照与模式状态。 |
| `ReadArticles/MainWindow.Playback.cs` | C# 播放状态、键盘事件、Seek、进度计时器；Script 播放约 33 ms 刷新，其他情形约 100 ms。保留原音频执行者，改为给网页同步播放快照。 |
| `ReadArticles/MainWindow.Vocabulary.cs` | 词汇列表选择、排序/删除、导入、高亮 UI 的 WPF 事件。新的右侧视觉与定位改由网页，最终编辑命令交回现有 ViewModel。 |
| `ReadArticles/MainWindow.Settings.cs` | 字号、口音、窗口左右宽度与持久化；逐项迁移可见 UI，保留权威设置存储。 |
| `ReadArticles/Rendering/ParagraphReader.cs` | 当前 WPF 正文/选区/词汇/Script 渐变混合实现，历史修复复杂。仅作迁移行为基准；新界面验收后再删除旧渲染路径。 |
| `ReadArticles/Models/ScriptContent.cs`、`ReadArticles/Services/ArticleRepository.cs` | 已有 Script 数据结构及校验、按需读取与时间语义，保留。 |
| `ReadArticles/ViewModels/MainViewModel.Vocabulary.cs`、`ReadArticles/Services/VocabularyRepository.cs` | 词汇新增、导入、保存、备份、冲突检测与回滚；保留 C# 最终管理。 |
| `ReadArticles/Services/ReaderPlayback.cs` | 文章/句/词独立协调的音频服务；不改为浏览器音频。 |
| `ReadArticles.Tests/ScriptChecks.cs` 与其他测试 | 旧 WPF 结构断言需要按新架构更新，但**行为回归、像素测试和文件安全测试不能随渲染器删除而消失**。 |
| `docs/Library-Layout.md`、`docs/Testing.md`、`docs/project-contract.md`、`docs/Publishing.md` | 正式迁移时同步更新架构、WebView2 Runtime 要求、测试与发布约定；本次只更新 handoff。 |

## 6. 建议执行阶段和验收门槛

1. **设计/差异清单（新会话第一步）**：对当前分支复核源码和所有已有交互，给出逐文件“保留/修改/删除/新增”清单、风险、最小消息协议、WebView2 控件与布局/焦点方案。明确哪些逻辑需要保留现状、哪些重复逻辑可以删。得到用户确认后才改业务代码。
2. **单个 WebView2 工作区骨架**：嵌入可信静态页面，完成中央标题+正文、右侧词表、内部拆分、底部工具栏、浮动通知；优先复用浏览器原型，验证运行时启动、文章切换、异常与回退；保持旧路径可用以便回归。
3. **接通全部交互**：JS 词汇匹配与单词/词组定位、跨段选区、添加按钮、保存回执、删除排序、发音、Passage SID、Script 同步渐变、字幕停止/Seek、字号、快捷键、滚动、Toast。与现有行为和词汇匹配测试逐项对照。
4. **验证后清理**：通过全部自动化和真实 Windows UI/音频测试、发布目录与更新验收后，删除不再使用的旧 WPF 阅读/词汇/Toast 视觉实现，精简重复配置和测试；最终 PR 审核再考虑合并，**未经用户明确同意不合并**。

关键验收：`non-identical` 的连字符跨行必须连续着色；跨多行 timed unit 不新增时间点；词汇背景与红字并存；用户新增词语后正文/右侧一致且失败不误提交；选区与右键 SID、blank 行为兼容；实际播放/暂停/Seek/停止；自适应窗口与 DPI；分栏滚动互不干扰；WebView2 缺失/崩溃可明确报错；真实词汇数据及备份绝不被破坏。

## 7. 测试、发布与操作规程

从 `C:\Projects\ReadArticles` 的 Windows PowerShell 执行：

```powershell
git status --short
git switch feature/script-reading-mode
git pull --ff-only origin feature/script-reading-mode

dotnet build ReadArticles -c Release
dotnet run --project ReadArticles.Tests -c Release -- --script-only
.\tools\Test.ps1 -Suite Core
.\tools\Test.ps1 -Suite UI
.\tools\Test.ps1 -Suite Full
```

发布用 `.\tools\Publish.ps1`，再用 `.\tools\Test-PublishedApp.ps1`；以 [`Publishing.md`](Publishing.md) 为准。测试固定数据来自 `tests/TestData/`，有风险的测试用临时工作副本；勿将真实版权文章、`userdata/`、`dist/`、`artifacts/` 加入 Git。

- Core CI、UI、Full、真实音频听测、独立原型和发布后的 EXE 验证是**不同证据**，每一项分别报告。
- 优先最小必要方案，先删冗余实体再考虑新增；不追求没有依据的重构。
- 提交信息用英文并准确描述改动；需 PR 审核及用户验收；**没有明确指示不得合并到 `main`**。
- 本文件所述方案系已确认的总体方向；实施细节若涉及功能改变，先在新会话讨论并取得授权。

## 8. 新会话可直接采用的起始任务

> 请先阅读仓库 `chenjing2000/readarticles` 的 `docs/HANDOFF.md`、`AGENTS.md`、`docs/project-contract.md`，检查 `feature/script-reading-mode` / PR #15 的当前状态。我们已决定由**一个 WebView2**统一负责中央标题及正文、右侧词汇显示与匹配、选区添加 UI、两个区域的底部工具栏和网页浮动 Toast；C# 保留词汇数据最终保存、文件安全、设置及全部音频业务。不要引入 React、Node.js 或构建工具。**先检查现有源码，提出具体逐文件实施与测试迁移计划，暂不修改代码，待确认后实施。** 如原型文件不可访问，请说明，不要假设它已经提交到仓库。

