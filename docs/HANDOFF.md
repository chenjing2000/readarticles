# ReadArticles — 开发交接（2026-11-18）

## 当前状态

- **开发分支：** `feature/webview2-reader`，基于 `feature/script-reading-mode`；Draft PR #16 仍未合并，PR #15 同样未合并。请在操作前核对分支、远程 HEAD 和本地 `git status`。
- **阶段八批次 2d：** 旧 WPF 正文、词表、工具栏、Toast、`ParagraphReader`、选区 Adorner 和重复的 C# 词汇匹配器已从本地工作区清理。单个 WebView2 是唯一阅读界面；WPF 保留窗口、左侧逐层加载的目录树与原生失败/退出界面。
- **Windows 已完成验收：** Release 构建通过；WebView2 专项 4/4，Core 10/10 组，UI 13/13 组，Full 6/6 组（应用回归 90/90），`Test-PublishedApp.ps1` 4/4；用户已确认 2d 实机功能验收通过。
- **本地 Git 状态：** 上述结果针对尚未提交的本地工作区。不能把远程 PR #16 的旧 HEAD 或 GitHub CI 结果视为 2d 的远程验收。本轮提交前修正完成后还须重跑相应测试。
- **授权：** 本说明不授权提交、推送或合并。尤其不得自动合并 PR #15、#16 或修改 `main`。

## 最终架构与职责

- **WPF / .NET 10：** Windows 宿主、左侧书库导航、目录展开/折叠/切库、对话框、生命周期、不可用时的原生错误及退出界面。
- **单 WebView2：** Passage 和 Script 正文、右侧词表、阅读和词汇工具栏、词汇 DOM 匹配/高亮/定位、选区添加浮层、独立滚动、网页通知及 Script SVG 逐行渐进着色。不使用 React、Node.js、HTML Audio 或第二个 WebView2。
- **C# 业务：** Passage/Script/词汇 JSON 解析及校验、音频资源匹配与播放时钟、词汇新增/排序/删除/导入后的最终事务提交、并发变更检测、原子替换、备份与恢复、通知 FIFO、设置与日志。网页不能直接修改用户文件。
- **失败原则：** WebView2 初始化、通信或渲染失败后显示原生错误/退出覆盖层，不再切回旧 WPF 阅读器；错误路径不得继续播放或假定保存成功。
- **Script：** 同目录 `<name>.script.json` 按需加载，要求 `filetype: script`、字符串 `title`、`paragraphs` / `text` / `timed_units`。单个单元仅有开始和结束时间；缺失配对或非正时长非法。对齐失败的单元不着色，且不改变 Passage 的 SID、词汇或全篇音频来源。

## 继续工作的入口

1. 先阅读 `AGENTS.md`、`docs/project-contract.md`、`docs/WebView2-Migration.md`、`docs/Testing.md` 和项目代码审查 skill；遵循 WindowsAppPlaybook。
2. 在 `C:\Projects\ReadArticles` 检查 `git status --short`、`git diff --check`，确认未提交补丁和当前分支。修改前先检查真实源码，勿根据远程未更新的文件覆盖本地改动。
3. 提交前做最小整改、Release 构建和 Core/UI/Full 回归；对发布产物使用 `tools/Publish.ps1`、`tools/Test-PublishedApp.ps1`，独立验证浏览器启动及实机关键交互。测试用工作副本，绝不对真实词汇库做破坏性测试。
4. 审核完整 `git diff` 后才决定是否提交。候选英文信息：`refactor: remove legacy WPF reader and complete WebView2 migration`。提交、推送、PR 合并各需独立明确授权。

## 相关文档

- [迁移分阶段历史、设计决策和实测记录](WebView2-Migration.md)
- [现行架构与数据流](Application-Overview.md)
- [当前测试入口和边界](Testing.md)
- [文章、Script、词汇和音频约定](Library-Layout.md)
- [发布与便携部署](Publishing.md)

**历史说明：** 早期第六、七阶段 WPF 像素与视觉测试及试点开关已结束；不得使用旧交接文本中的 `--webview-pilot` 操作说明、旧 WPF 备用阅读器或旧分支命令来指导当前开发。
