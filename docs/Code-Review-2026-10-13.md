# ReadArticles 代码审查与局部简化（2026-10-13）

本次从已存在的 WPF 阅读器和调用方出发，检查职责、耦合、重复、过度设计、格式校验和文件归属。结论是现有结构已经较小，没有必要引入新的接口、容器、通用仓库或组件框架。本次保留公开接口、配置、JSON 格式、用户数据和统一测试／发布入口，仅修复已复现的问题并拆分一个职责过多的函数。

## 模块评估

| 模块 | 实际职责与评估 |
| --- | --- |
| App、MainWindow.xaml 和 MainWindow.xaml.cs | 启动、资源与窗口装配；两套媒体实例的生命周期在窗口内明确归属。保留 WPF 原生事件和绑定。 |
| MainWindow.Library / Vocabulary / Playback / Settings / Status | 同一窗口按功能拆分 partial 文件；目录选择、词汇操作、进度计时、设置保存和消息期限各自集中。无需把短事件处理器包装成额外服务。 |
| MainViewModel 及 Library / Vocabulary / Playback partial | 准备文章和词条显示状态，协调提交、通知及错误消息；先准备再提交，失败保留旧文章。短播放转发保持调用方接口。 |
| VocabularyEntryViewModel、RelayCommand | 每个词条的展示和按钮命令；直接委托足够，没有命令总线或泛型命令层。 |
| Models | 小型数据记录和词表 JSON 映射；record/with 表达快照替换，JsonExtensionData 保留未知元数据，属于必要语义。 |
| LibraryRepository、UserRepository | 目录发现与可选账户标签读取，分别处理；book.json 仅作标记，不误把账户／缓存／词表当正文。递归直接表达目录结构。 |
| ArticleRepository | 原 Load 同时负责正文解析校验、词表降级和音频告警；已将纯解析校验抽为同类私有 ReadParagraphs，Load 保持文件加载协调。 |
| VocabularyRepository、ReaderWorkspace、JsonFiles | 词表格式、外部修改检测、原子写入、成功后更新状态；层次各有职责，保留指纹复核和提交前回调，不为缩短代码移除数据保护。 |
| VocabularyMatcher | 按最早起点、同起点最长词组，输出不重叠匹配；英文边界规则显式，保留直观循环，未增加缓存或复杂匹配引擎。 |
| AudioResources、ReaderPlayback、PlaybackController | 路径选择、两个播放通道协调、单通道状态机分开；保留请求编号避免旧回调影响新目标。 |
| IAudioPlayer、WpfAudioPlayer | 唯一音频抽象分离原生解码与确定性状态测试；保留，未复制为多个无实际收益的接口。 |
| ParagraphReader、SelectionAddAdorner | 文本／SID 映射、选择、高亮和选择按钮定位；WPF TextPointer、Run 本地属性和 Adorner 的复杂度用于保留边界与滚动行为。局部函数共享构建上下文，未强行抽出一组参数众多的方法。 |
| ResourcePaths、ReaderSettings、UiIcons | 安全相对路径、配置归一化／恢复、已有 SVG 子集的本地绘制；职责独立。SVG 解析只支持仓库实际资源，不扩展为完整 SVG 框架。 |
| ReadArticles.Tests、tools、发布工作流 | 测试直接调用生产模块，小型辅助函数保留。测试数据准备、流程测试和发布防护有不同用途，不合并为一条不可单独验证的大脚本。所有 PowerShell 文件保持英文 ASCII。 |

## 必须修复：精确 SID 校验

格式约定 SID 为小写 s 加三位 ASCII 数字（例如 s001），不包含换行。原正则 `^s[0-9]{3}$` 中 `$` 可匹配字符串末尾换行之前的位置；整数解析又接受尾部空白。构造 `s001\n` 及相应音频声明时，真实 ArticleRepository 误接受该文章。

先新增内容回归，在临时正文副本修改 SID 与两个音频声明。修复前仅该新用例失败，错误为 `Invalid input was accepted`；随后将正则边界改为 `\A` 和 `\z`。修复后非法正文在提交前被拒绝，工作区仍引用先前文章。合法 s001–s999、重复／零 SID、next_sid 和音频声明规则保持原有语义；没有增加连续编号等新的约束。

同时修正文档 Library-Layout 的过期说明：文章与发音实际使用两个播放器，底部停止按钮只停止文章。此项只更新说明，不修改播放行为。

## 推荐并已完成：职责与忽略规则

- ArticleRepository 在同一个具体类中提取 ReadParagraphs，集中正文结构校验；Load 集中词表读取、降级告警、缺失录音统计和快照构造。没有增加解析接口、配置项或新类。
- .gitignore 删除全局图片／ZIP 排除和被整个 reference 目录排除覆盖的重复规则。个人目录、媒体、构建、发布和测试输出继续排除；UI 图片、将来的说明插图和合法源测试资源不会被全局后缀规则隐藏。
- Publish／Test-Publish 删除 legacyDirectory 恒为 null 的旧目录迁移分支；Test-PublishedApp 直接验证启动创建配置，删除恒为 true 的开关及恒为 null 的集成项目分支。Test-Workflow 删除实际调用链从未使用的 node 替身。保留当前 CLI、EXE 锁检查、原子替换、用户配置／其他文件保护、失败恢复与安全临时目录清理。
- 文档索引和模块概述反映本次实际修改。Testing 不再固化易随新增回归失效的总用例数；历史 Health Check 保留当时的验证记录。

## 可选且未实施

partial 文件合并、统一所有单行／多行语法、增加仓库接口或缓存、封装所有重复遍历均无当前正确性收益。测试内与生产代码各自遍历导航叶节点的少量重复可接受，不让测试反过来依赖生产遍历结果计算期望值。公开的短转发和测试使用接口保留。

## 文件归属与清理

已检查 Git 跟踪文件及调用引用，未发现应删除的废弃源码或重复运行入口；docs/skills 是外部内容制作参考，仍被文档链接使用。未删除 bin、obj、dist、个人资源或用户源文章。

仓库 artifacts 中的截图，以及上级 E:/Projects/artifacts 的 content-tests、controls-tests、editing-tests、test-media、progress-options.png、reader-startup-footer.png，来自相对工作目录的测试写入，生产代码不读取它们。当前 Test.ps1 会切换到仓库根目录；这些旧生成产物可在确认没有测试运行时清理，由主任务统一处理。

## 验证证据与限制

- 新回归在修复前使用既有 Prepare-ArticleFixture 的真实 MP3 临时副本运行：内容类别 13 通过、1 失败，仅新 SID 用例失败。
- 单行修复后：内容类别 14 通过、0 失败；提取 ReadParagraphs 后再次运行：14 通过、0 失败。包含固定正文／词表读取、可选坏词表降级、元数据原子往返、外部编辑拒绝和状态通知。
- 发布模板简化前后，既有 Test-Publish 均为 5 通过、0 失败；Test-Workflow 的成功汇总、原生命令失败拒绝、无外部数据显式跳过三项均通过。Test-PublishedApp 的窗口启动和实际 EXE 检查仍由主任务串行验收，本子任务没有运行它。
- 直接使用无 MP3 的 Git fixture 首次运行另有两项资源相关失败：`segment audio uses audio_segments with exact SID/accent` 和 `normal operations stay quiet while warnings and errors remain visible`。补齐临时录音副本后这两项均通过，未改变测试期望或生产逻辑。
- 原始 D:/LibEN/The Economist/2026-09-26/When America walks away 仅由既有脚本读取／复制，写操作都在唯一临时目录；临时目录和环境变量已清理／恢复。
- 本审查子任务没有打开窗口、播放音频或运行原生输入测试；完整 UI／Full 和发布验收由主任务串行完成，结果以其最终记录为准。

## 统一收尾验证

75 fixed regression checks and 2 current-source checks passed using the supplied article recordings. The invalid SID regression is included. 发布 EXE 自动验收也通过；本子任务阶段的未测项由主任务补齐。最终证据见 [Health Check](Health-Check-2026-10-13.md)。

统一清理已完成：四个仓库的编译缓存与已确认的测试输出共 29 个目录、约 100 MB；保留 dist、配置、个人素材、原始资源和历史资料。后续构建与测试会重新生成所需缓存。
