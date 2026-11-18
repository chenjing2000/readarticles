# ReadArticles — 开发交接（2026-11-18）

## 当前状态

- **当前正式基线：** `main`，squash 合并提交 `2c68cae`。PR #16 已合并，PR #15 已关闭且未单独合并；旧 WebView2/Script 功能分支不再作为开发入口。每次工作先核对当前 HEAD 和 `git status`。
- **已交付架构：** 旧 WPF 正文、词表、工具栏、Toast、`ParagraphReader`、选区 Adorner 与重复的 C# 词汇匹配器已删除。单个 WebView2 是唯一阅读界面；WPF 保留窗口、左侧逐层加载的目录树与原生失败/退出界面。
- **Windows 已完成验收：** Release 构建通过；WebView2 专项 4/4，Core 10/10 组，UI 13/13 组，Full 6/6 组（应用回归 91/91，源文件保护 2/2），`Test-PublishedApp.ps1` 4/4；用户确认 2d 实机功能验收通过。
- **验收证据：** 合并前的 WebView2 [Run #142](https://github.com/chenjing2000/readarticles/actions/runs/44330081273) 和 Windows Core [Run #143](https://github.com/chenjing2000/readarticles/actions/runs/44331283755) 已通过；此前的本地 Full 91/91 与实机验收也已完成。合并后新整改的构建和 Windows 实机测试必须重新执行，不能沿用旧结果。最终发布版的完整 WebView2 交互仍需单独检查。
- **授权：** 本说明不构成修改、提交、推送或合并授权。新整改应在从 `main` 创建的独立分支上开展，并分别取得后续 Git 操作授权。

## 最终架构与职责

- **WPF / .NET 10：** Windows 宿主、左侧书库导航、目录展开/折叠/切库、对话框、生命周期、不可用时的原生错误及退出界面。
- **单 WebView2：** Passage 和 Script 正文、右侧词表、阅读和词汇工具栏、词汇 DOM 匹配/高亮/定位、选区添加浮层、独立滚动、网页通知及 Script SVG 逐行渐进着色。不使用 React、Node.js、HTML Audio 或第二个 WebView2。
- **C# 业务：** Passage/Script/词汇 JSON 解析及校验、音频资源匹配与播放时钟、词汇新增/排序/删除/导入后的最终事务提交、并发变更检测、原子替换、备份与恢复、通知 FIFO、设置与日志。网页不能直接修改用户文件。
- **失败原则：** WebView2 初始化、通信或渲染失败后显示原生错误/退出覆盖层，不再切回旧 WPF 阅读器；错误路径不得继续播放或假定保存成功。
- **Script：** 同目录 `<name>.script.json` 按需加载，要求 `filetype: script`、字符串 `title`、`paragraphs` / `text` / `timed_units`。单个单元仅有开始和结束时间；缺失配对或非正时长非法。对齐失败的单元不着色，且不改变 Passage 的 SID、词汇或全篇音频来源。

## 继续工作的入口

1. 先阅读 `AGENTS.md`、`docs/project-contract.md`、`docs/WebView2-Migration.md`、`docs/Testing.md` 和项目代码审查 skill；遵循 WindowsAppPlaybook。
2. 在 `C:\Projects\ReadArticles` 检查 `git status --short` 和当前分支，按需 `git fetch` 核对远程 HEAD。不要假定存在未提交的 2d 补丁；修改前以实际源码为准。
3. 2d 的最小整改及回归测试已完成。后续若修改代码，按影响范围运行 Release 与 Core/UI/Full；发布时执行 `tools/Publish.ps1`、`tools/Test-PublishedApp.ps1` 并对最终 EXE 单独检查浏览器交互。测试使用工作副本，勿对真实词汇库进行破坏性测试。
4. 后续新功能或整改以 `main` 为基线建立独立分支，完成变更核对、针对性回归、Windows 实机与发布验收后再审查是否合并。旧 PR #15/#16 只是历史记录。

## 相关文档

- [迁移分阶段历史、设计决策和实测记录](WebView2-Migration.md)
- [现行架构与数据流](Application-Overview.md)
- [当前测试入口和边界](Testing.md)
- [文章、Script、词汇和音频约定](Library-Layout.md)
- [发布与便携部署](Publishing.md)

**历史说明：** 早期第六、七阶段 WPF 像素与视觉测试及试点开关已结束；不得使用旧交接文本中的 `--webview-pilot` 操作说明、旧 WPF 备用阅读器或旧分支命令来指导当前开发。
