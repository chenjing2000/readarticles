# ReadArticles — 开发交接（2026-11-20）

> **新会话从这里开始。** 当前重点是尚未合并的 **PR #19：阅读设置弹窗、WebView2 图标与视觉交互改造**。不要误以为这些修改已经进入 `main`。

## 1. 当前 GitHub 基线与验收状态

- 仓库：[chenjing2000/readarticles](https://github.com/chenjing2000/readarticles)；本地项目通常为 `C:\Projects\ReadArticles`。
- **正在开发的功能分支：** `feature/reading-settings-dialog-icons`。
- **PR：** [#19](https://github.com/chenjing2000/readarticles/pull/19)，状态 **Open / Draft / 未合并**；本轮修改前远程 HEAD 为 `07e75ed656d33c75a1290d231f9bfadf9ae5113c`，当前最新提交以 GitHub 实际 HEAD 为准。
- **前一开发基线 CI：** [Windows CI #44693808771](https://github.com/chenjing2000/readarticles/actions/runs/44693808771) 针对 `07e75ed` **Success**：Build application、Core regression、Publish actual portable release、Verify portable release layout 均通过。新增英文字体及 `0.80 em` 选项的本轮提交仍需核对独立 CI。
- **Windows 实机验收：** 用户已确认上一轮 540 DIP 设置弹窗及滚动、五种选词背景、三档加号、选区自动消失与定位、设置保存/取消/恢复默认等五项实机验收全部符合预期。本轮新增字体与四档加号选项**尚待 Windows 实机验收**；不得未经用户批准合并。
- `main` 仍是稳定分支，包含于 2026-11-19 合并的 PR #18（三栏布局）。PR #18 已经用户 Windows 实机验收；**不要重做**它的三栏求解器。保留与本项目无关的 `feature/exercise` 分支，不随意清理。
- 这里只能确定 GitHub 远程状态，**不能推断本地工作区干净或本地分支已经同步**。先检查 `git status --short`、当前分支及远端最新提交。

## 2. PR #19 已实现并在功能分支提交的内容

### 设置弹窗与数据配置

- 原“阅读设置”名称统一为 **“设置”**；左上齿轮按钮只显示 SVG 图标。单个原生 WPF Fluent 设置弹窗，固定 **500 × 540 DIP**，不允许改变窗口大小；四个功能列宽 **72 / 140 / 72 / 140 DIP**，第二与第三功能列中间留 **20 DIP**（Grid 实际列宽 `72/140/20/72/140`）。各列控件**靠左对齐**，但下拉框显示文字和选项内部仍居中。标签/选项/底部按钮文字 13 DIP，单位标签 12 DIP，分组标题 15 DIP。底部“恢复默认 / 取消 / 保存”按既有草稿式提交行为。
- **英文字体列表按此顺序：** `Georgia / Microsoft YaHei UI / Nunito / Segoe UI / Noto Sans`，默认 **Georgia**；标题、正文、词头使用此列表。**中文释义字体**仍为 `Microsoft YaHei / SimSun / KaiTi`（微软雅黑/宋体/楷体）。WPF 通过 `Fonts.SystemFontFamilies` 检查 **Georgia、Nunito、Segoe UI、Noto Sans** 是否存在；缺少时弹出**非阻断浮动提示**（仅显示“未检测到字体 {name}。”），保留/恢复可用字体。不对中文字体或 Microsoft YaHei UI 进行存在性检查；不开发字体下载安装功能。
- **文字排版默认值：** 标题 18 px、行高 **1.7**（1.6/1.7/1.8）、段后 16 px；正文 15 px、行高 **1.7**（1.6/1.7/1.8）、段后 16 px、首行缩进 2 em；词头 16 px（15/16/17）、音标 14 px（13/14/15）；释义 15 px（14/15/16）、行高 **1.5**（1.5/1.6/1.7）。下拉框数值选项、数据归一化和 WebView2 fallback 已联动更新。
- 颜色设置是**八处五色圆球**（不是 HEX 文本输入框），色球每组占 140 DIP，默认在第一位。普通文字：`#283747/#344054/#3F4750/#315247/#513D4D`；词头：`#B7394D/#9D3E68/#7D50A1/#1F6795/#8B593B`；音标：`#0A7785/#256CA5/#38735A/#7657A0/#995431`；阅读背景：`#F8F8F6/#F0F3F7/#F7F1E6/#EFF5F0/#F1F0F7`。新增**“台词颜色”**与背景颜色同一行：`#B64550/#A33D30/#A45A2C/#856300/#53713C`，默认 `#B64550`，只用于 Script 已播放文字；Script 原 SVG clip 定时着色机制保持。正文“文字颜色”标签替换原“颜色”；新增选词背景 `#DDE8F2/#E5EDE0/#F3E9DC/#EBE5F2/#F2E5E7`，默认雾蓝 `#DDE8F2`，使用原生 CSS `::selection`，不改变前景色。
- 正文设置 **“选词加号”**（原“选项加号”），候选 `0.65/0.70/0.75/0.80 em`，默认 **0.65 em**，根据正文实际字体大小显示 SVG。点击区域仍 **24 × 24 CSS px**、可见图形放在点击区域左上角；相对选中文字末行右下角**水平 1 CSS px、垂直 0 CSS px**。定位改用唯一的 `requestAnimationFrame` 循环，**最多约 60 Hz** 检查；不再设毫秒刷新周期，页面不可见/选区失效/文章或模式切换时停止，滚出可视范围隐藏，滚回时可恢复。浏览器绘制节奏限制实际频率，不保证精确 60 Hz。
- 设置路径为 EXE 同级 `userdata/config/settings.json`；用户已确定**破坏性配置升级，不迁移旧格式**。新版本正常保存采用原子写入及最近一次有效备份；应用日志、备份、WebView2 用户数据仍归 `userdata/`。图书馆里的文章/Script/词汇 JSON 与音频保留在用户指定目录。账户与左下角 User 显示/专属代码已删除。
- 设置中**无音量滑块**；文章音频、单词/句子发音两套播放器都固定 `Volume = 1.0`（应用级 100%，不强制 Windows 系统音量）。音频设置仅保留英/美音、前进和后退秒数。

### 阅读 UI、按钮与播放边界

- 已采用 V5 SVG 图标，设置、播放/暂停、停止、词汇工具、Script/Passage 等按钮按约定使用无边框圆形样式；三栏底部保持 **48 DIP** 对齐。进度条左侧“文章音频”标签已删除。
- 正文词汇背景采用方案 B：`Range.getClientRects()` 测量文字，在独立不可交互图层绘制 **4 px 圆角**背景，不修改实际文本节点，保留文本选择和 Script 定时单元。
- **不要重新定义音频时间轴。** 用户明确撤销“停止后仍可拖动”提议：停止后进度条**不可拖动**、进度着色清除，暂停/播放期间可以 seek；切换文章成功后清零旧进度与通知，打开失败保留原文章状态。

## 3. 技术架构与不可破坏的边界

- C# / WPF / .NET 10 Windows x64；唯一 WebView2 承载 Passage/Script、词表、选区与通知，WPF 保留左栏树和原生窗口；音频由独立的 C# 文章播放器与词/句发音播放器管理，不引入 HTML Audio 或第二套 WPF 阅读器。
- C# 是三栏宽度的唯一求解者：左栏默认 250 DIP、右栏默认 300 DIP（均允许 200–400），中栏至少 200 DIP，两分隔条各 3 DIP；三栏宽度**仅会话有效**，不写入设置 JSON。具体算法见 [Application-Overview.md](Application-Overview.md) 与 PR #18 历史。
- 失败文章切换不破坏当前阅读状态；目录扫描不阻塞 WPF；过期异步操作不能写回新文章；词汇编辑需保留未知 JSON 字段、并发检测、原子写入与备份；Script/Passage 源文件及 MP3/WAV 只读。MP3 存在时优先，不因解码失败自动换 WAV。
- 贯彻第一性原理与奥卡姆剃刀：优先最小改动和删除多余逻辑，不增加不必要功能、兼容层或文件；用户明确验收前**不合并 PR #19，不修改无关分支**。

## 4. 下一会话最重要的工作

1. **先读取此文档，再核对 PR #19、`feature/reading-settings-dialog-icons` HEAD、最新 CI、源码与本地状态**；不要把此前的 `6feefda` 功能提交误认成文档提交后的 HEAD。入口文件：`ReadArticles/SettingsDialog.cs`、`Infrastructure/ReaderSettings.cs`、`MainWindow.Settings.cs`、`MainWindow.WebView.cs`、`web/reader.js`、`reader.css`，相关测试位于 `ReadArticles.Tests/`。
2. **优先处理本轮 Windows 实机验收**：核对 Nunito 与 Noto Sans 字体选项、缺失字体提示与回退，以及选词加号新增 `0.80 em` 后的尺寸、定位和点击；上一轮设置、选区背景与保存等五项验收已通过。
3. 本机可执行（先确认本地未提交修改）：

   ```powershell
   cd C:\Projects\ReadArticles
   git status --short
   git fetch origin
   git switch feature/reading-settings-dialog-icons
   git pull --ff-only origin feature/reading-settings-dialog-icons

   .\tools\Test.ps1 -Suite UI
   .\tools\Test.ps1 -Suite Full
   # 确保 ReadArticles.exe 已退出，避免发布文件被锁定
   .\tools\Publish.ps1
   .\tools\Test-PublishedApp.ps1
   ```

4. 有新修改时先列出影响与风险，保持单一小功能分支、配套测试和 CI，提交到 PR #19；**用户实际 Windows 验收通过并明确授权后**才能合并 `main`。合并后另行验证 `main` CI 和决定是否删除功能分支。

## 参考文档与历史

- [Configuration.md](Configuration.md)（最新 UI 和配置参数）、[Testing.md](Testing.md)、[Publishing.md](Publishing.md)、[project-contract.md](project-contract.md)、根目录 `AGENTS.md`、[project-code-review/SKILL.md](skills/project-code-review/SKILL.md)。
- 2026-11-19 的 PR #18 三栏布局基线已验收并合并，曾对应 [CI #44445487538](https://github.com/chenjing2000/readarticles/actions/runs/44445487538)。PR #17 为更早的输入与词汇控制收尾。老 WPF 阅读器、旧迁移分支、旧账户模型均属历史，不要重新启用。
