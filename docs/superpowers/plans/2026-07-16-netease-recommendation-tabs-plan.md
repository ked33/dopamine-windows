# Dopamine“网易云推荐”选项卡改造任务计划

日期：2026-07-16

目标仓库：`D:\D-Software\source\dopamine-windows`

参考文档：`D:\D-Download\第三方网易云\HyPlayer\DevelopDoc\网易云推荐选项卡改造任务计划.md`

## 1. 文档状态

- 状态：规划稿，尚未实施功能代码。
- 目标：将“音乐库”中现有的“每日推荐”一级选项卡改名为“网易云推荐”，并在其内容页内提供“每日推荐”“心动推荐”“私人 FM”三个子选项卡。
- 本文以 Dopamine 当前 WPF、Prism、MVVM、网易云服务适配层、临时在线队列和本地化机制为基线，不照搬 HyPlayer 的 UWP 页面结构。
- 本文定义产品行为、代码边界、分阶段任务、验证要求和回滚边界，作为后续实现的任务基线。
- 本任务不修改“音乐库”外层导航层级，不新增独立顶层播放器页面。

## 2. 结论

推荐采用以下低风险结构：

```text
音乐库一级选项卡：网易云推荐
  ├─ 每日推荐：复用现有成熟页面和 ViewModel
  ├─ 心动推荐：新增按“我喜欢的音乐”生成的智能推荐列表
  └─ 私人 FM：新增独立 FM 会话和按需补队列能力
```

关键决策：

1. 保留 `CollectionPage.DailyRecommendations = 6` 的数值和第一阶段的枚举成员名，只修改显示文案和导航目标，避免已有 `Settings.xml` 中的 `SelectedCollectionPage=6` 失效。
2. 新增“网易云推荐”父容器，现有 `CollectionDailyRecommendations` 降为第一个子选项卡内容，不重写已经稳定的每日推荐功能。
3. 心动推荐和私人 FM 分别使用独立 View/ViewModel；父 ViewModel 只管理子选项卡选择和懒加载，不承载具体 API、列表和播放逻辑。
4. 扩展现有 `INeteaseApiClient` 与 `INeteaseMusicService`，不允许 WPF 页面直接引用 `HyPlayer.NeteaseApi` 的 Contract/DTO。
5. 心动推荐在请求成功并获得有效结果后才替换播放队列；失败时不得破坏当前队列。
6. 私人 FM 必须由独立的会话/播放协调服务维护状态、补充下一批歌曲和处理退出，不能把连续播放算法写进页面事件。
7. 第一阶段私人 FM 只要求普通私人 FM。AI DJ 涉及“讲解音频 + 歌曲”的混合资源模型，列为后续扩展，不作为本任务首轮验收的强制条件。

## 3. 当前代码基线

### 3.1 现有导航

当前“每日推荐”位于音乐库顶部 `Pivot` 的最后一项：

```text
艺术家 | 风格 | 专辑 | 歌曲 | 播放列表 | 文件夹 | 每日推荐
```

真实入口：

- `Dopamine/Views/FullPlayer/Collection/CollectionMenu.xaml`
- `Dopamine/ViewModels/FullPlayer/Collection/CollectionMenuViewModel.cs`
- `Dopamine/ViewModels/FullPlayer/Collection/CollectionViewModel.cs`
- `Dopamine.Core/Enums/PageEnums.cs`

当前持久化链路：

```text
CollectionMenu Pivot.SelectedIndex
  ↕ EnumConverter
CollectionMenuViewModel.SelectedPage
  → FullPlayer.SelectedCollectionPage
  → IsCollectionPageChanged
  → CollectionViewModel
  → CollectionRegion
```

`CollectionPage.DailyRecommendations` 当前数值为 `6`。该值会写入 `Settings.xml`，因此不能通过删除、插入枚举项或改变数值来完成改名。

### 3.2 现有每日推荐页面

当前每日推荐已经是独立的 WPF `UserControl`：

- `Dopamine/Views/FullPlayer/Collection/CollectionDailyRecommendations.xaml`
- `Dopamine/Views/FullPlayer/Collection/CollectionDailyRecommendations.xaml.cs`
- `Dopamine/ViewModels/FullPlayer/Collection/CollectionDailyRecommendationsViewModel.cs`

现有能力包括：

- 登录、初次加载、刷新、空状态和错误状态；
- 推荐歌曲列表、搜索过滤和数量显示；
- 双击/Enter 播放、播放全部、播放下一首、加入当前在线队列；
- 网易云喜欢/取消喜欢；
- “不感兴趣”及本地推荐列表替换；
- 在线搜索右键菜单；
- 请求取消、加载代次和会话代次保护；
- 中国时间每日 06:00 自动刷新调度；
- 按账号和推荐日期隔离的 DPAPI 加密缓存；
- `PlaybackQueueContext.NeteaseDailyRecommendations` 临时在线队列；
- 每日推荐独立的随机播放设置。

因此，本任务不应重新实现每日推荐缓存、登录状态、在线歌曲映射或播放入口。正确做法是把现有控件放入新的父页面第一个子选项卡，并只处理其父子生命周期兼容。

### 3.3 现有网易云服务边界

当前服务边界已经形成：

```text
WPF ViewModel
  → INeteaseMusicService
  → INeteaseApiClient
  → HyPlayer.NeteaseApi 0.1.2
```

当前 `INeteaseMusicService` 只提供：

- 每日推荐；
- 喜欢状态查询和修改；
- 每日推荐“不感兴趣”；
- 官方播放 URL 解析；
- 歌词；
- 会话缓存清理。

当前 `INeteaseApiClient` 只提供：

- 二维码登录和账号状态；
- 每日推荐；
- 喜欢歌曲 ID；
- 喜欢/取消喜欢；
- 每日推荐“不感兴趣”；
- 歌曲 URL 和歌词。

当前尚未接入：

- 用户“我喜欢的音乐”歌单 ID 查询；
- `PlaymodeIntelligenceListApi` 心动推荐；
- `PersonalFmApi` 私人 FM；
- 私人 FM 不喜欢/垃圾桶操作；
- AI DJ 混合资源。

### 3.4 现有播放边界

`IPlaybackService.PlayTransientQueueAsync()` 已支持用在线 `TrackViewModel` 建立不持久化的临时队列。当前队列上下文只有：

```text
Default
NeteaseDailyRecommendations
```

固定列表型的心动推荐可以复用临时在线队列，但需要独立上下文。私人 FM 则不是一次性固定列表：它必须在队列接近末尾或播放结束时请求下一批内容，并防止重复请求、旧响应回写和退出后继续补队列。

### 3.5 本地化和工程约束

- 本地化文件位于 `Dopamine/Languages/*.xml`。
- 英文 `EN.xml` 是回退基线，简体中文为 `ZH-CN.xml`。
- 当前资源键 `Netease_Daily_Recommendations` 的显示值是“每日推荐”。
- 项目使用旧式 `.csproj`，新增 `.cs`、`.xaml` 和 `Page` 必须显式登记。
- Prism 导航目标需要在 `Dopamine/App.xaml.cs` 注册。
- 本机通常没有完整 .NET Framework 4.8 构建环境；实现阶段应以静态检查和 GitHub Actions `Build Portable` 作为构建证据，并单独进行真实运行态验收。

## 4. 产品与交互目标

### 4.1 音乐库一级选项卡

将当前显示文案：

```text
每日推荐
```

改为：

```text
网易云推荐
```

改造后音乐库顶部顺序保持：

```text
艺术家 | 风格 | 专辑 | 歌曲 | 播放列表 | 文件夹 | 网易云推荐
```

不改变它在 `Pivot` 中的索引，不移动其他选项卡，不改变 `SelectedCollectionPage=6` 的含义。

### 4.2 页面内子选项卡

第一阶段固定顺序：

| 子选项卡 | 主要用途 | 登录要求 | 切换时是否自动播放 |
| --- | --- | ---: | ---: |
| 每日推荐 | 展示当前账号的每日推荐歌曲 | 是 | 否 |
| 心动推荐 | 根据“我喜欢的音乐”生成智能推荐列表 | 是 | 否 |
| 私人 FM | 启动或继续网易云私人 FM | 是 | 否 |

统一行为：

- 首次进入“网易云推荐”默认打开“每日推荐”。
- 切换到子选项卡只加载该子页需要的状态，不同时请求三个功能。
- 切换子选项卡不得自动清空或替换当前播放队列。
- 心动推荐必须点击“生成心动推荐”后才请求并生成结果。
- 私人 FM 必须点击“开始私人 FM”或“继续私人 FM”后才改变播放状态。
- 离开“网易云推荐”页面不自动停止当前音乐或私人 FM。

### 4.3 子选项卡控件

建议继续使用项目已有的 `Digimezzo.Foundation.WPF.Controls.Pivot`：

- 与外层音乐库菜单风格一致；
- 无需引入新的 TabControl/第三方依赖；
- 支持现有主题资源和键盘操作；
- 三个固定同级页面适合使用 Pivot。

父页内应使用独立的子选项卡样式，避免直接复用外层 `MenuPivot` 后出现字号、边距或选中指示器层级冲突。若现有样式无法区分，应新增小范围的 `NeteaseRecommendationPivot` 样式，而不是修改全局 `MenuPivot` 影响其他页面。

## 5. 目标架构

### 5.1 父容器

建议新增：

```text
Dopamine/Views/FullPlayer/Collection/CollectionNeteaseRecommendations.xaml
Dopamine/Views/FullPlayer/Collection/CollectionNeteaseRecommendations.xaml.cs
Dopamine/ViewModels/FullPlayer/Collection/CollectionNeteaseRecommendationsViewModel.cs
```

职责仅包括：

- 管理当前子选项卡；
- 首次进入时默认选中每日推荐；
- 管理子页可见性和懒加载触发；
- 可选地在同一应用会话内记住上次子选项卡；
- 将登录状态变化传递给尚未实例化的子页壳状态。

父 ViewModel 不负责：

- 直接调用网易云 API；
- 构建歌曲模型；
- 替换或追加播放队列；
- 维护私人 FM 连续播放状态；
- 复制每日推荐 ViewModel 的命令。

### 5.2 子视图

保留并复用：

```text
CollectionDailyRecommendations.xaml
CollectionDailyRecommendationsViewModel.cs
```

建议新增：

```text
CollectionIntelligenceRecommendations.xaml
CollectionIntelligenceRecommendations.xaml.cs
CollectionIntelligenceRecommendationsViewModel.cs

CollectionPersonalFm.xaml
CollectionPersonalFm.xaml.cs
CollectionPersonalFmViewModel.cs
```

三个子视图均保持独立的 loading、空状态、错误、取消和命令状态，不让一个子页的失败遮蔽整个“网易云推荐”页面。

### 5.3 服务扩展

优先扩展现有接口，而不是再建立与其平行的第二套网易云客户端：

```text
INeteaseApiClient
INeteaseMusicService
NeteaseApiClient
NeteaseMusicService
```

建议新增业务方法，实际命名可按仓库风格调整：

```csharp
Task<NeteaseResult<NeteaseLikedLibrary>> GetLikedLibraryAsync(...);
Task<NeteaseResult<IReadOnlyList<NeteaseIntelligenceRecommendation>>> GetIntelligenceRecommendationsAsync(...);
Task<NeteaseResult<IReadOnlyList<NeteasePersonalFmItem>>> GetPersonalFmAsync(...);
Task<NeteaseResult<bool>> DislikePersonalFmSongAsync(...);
```

业务模型必须位于 `Dopamine.Services.Online.Netease`，不得把 `PlaymodeIntelligenceListResponse`、`PersonalFmResponse` 等包类型暴露给 UI。

### 5.4 私人 FM 会话协调器

建议新增独立服务：

```text
INeteasePersonalFmService
NeteasePersonalFmService
```

它负责：

- 当前 FM 会话是否处于活动状态；
- 开始、继续和退出私人 FM；
- 首批加载和接近队尾时补充下一批；
- single-flight，防止重复补队列；
- 会话代次与取消；
- 跳过和“不喜欢”；
- 登录失效时结束会话；
- 页面离开后仍允许播放服务完成当前 FM 播放；
- 用户切换到普通队列后停止自动补充 FM；
- 对 UI 发布只读状态变化。

该服务可以协调 `INeteaseMusicService` 与 `IPlaybackService`，但不应直接引用 WPF 控件或页面。

## 6. 每日推荐子选项卡计划

### 6.1 复用范围

完整保留现有能力：

- 列表布局、搜索和虚拟化；
- 播放全部和从选中项播放；
- 在线队列操作；
- 喜欢/取消喜欢；
- 不感兴趣；
- 06:00 刷新；
- DPAPI 加密缓存；
- 登录、空、错误和重试状态；
- 请求与会话代次保护。

### 6.2 必要适配

- 把现有每日推荐控件放入父容器第一个 PivotItem。
- 检查父子 `Loaded`/`Unloaded` 是否会在切换子页时重复订阅事件或重建刷新计时器。
- 若 Pivot 仅隐藏而不卸载内容，确保隐藏页不会持续进行无意义 UI 更新。
- 若 Pivot 会卸载内容，确保每日推荐缓存仍能立即恢复，不因来回切换重复发起网络请求。
- 每日推荐页面标题和数量文案仍使用“每日推荐”，只有外层入口改为“网易云推荐”。
- 不把现有 `PlaybackQueueContext.NeteaseDailyRecommendations` 改成泛化名称，避免无必要地改动已稳定的随机播放行为。

## 7. 心动推荐子选项卡计划

### 7.1 第一阶段产品语义

固定基于当前登录账号的“我喜欢的音乐”歌单生成心动推荐。

建议文案：

```text
心动推荐
根据“我喜欢的音乐”生成智能推荐列表
生成心动推荐
重新生成
播放全部
```

第一阶段不提供任意歌单选择器，避免 UI 暗示支持尚未验证的来源语义。

### 7.2 调用前置条件

必须依次检查：

1. 会话状态为 `SignedIn`；
2. 当前账号 ID 有效；
3. 能确定“我喜欢的音乐”歌单 ID；
4. 喜欢歌曲集合不为空；
5. 有可用的网易云歌曲作为种子；
6. 当前播放项若参与 `startMusicId`，必须是 `TrackSourceKind.Netease` 且有有效 `RemoteId`；
7. 请求未在进行中。

当前播放的是本地歌曲时，不得把本地数据库 ID、路径或其他字段传给网易云接口。

### 7.3 参数边界

第一阶段建议保持 HyPlayer 已验证的接口语义，但在实现前必须针对当前 `HyPlayer.NeteaseApi 0.1.2` Contract 再核对：

| 参数 | 建议来源 |
| --- | --- |
| `playlistId` | 当前账号“我喜欢的音乐”歌单 ID |
| `songId` | 从喜欢歌曲中选择的有效种子歌曲 ID |
| `startMusicId` | 当前播放的网易云歌曲 ID；不可用时回退到种子歌曲 |
| `count` | 喜欢歌曲数量，并允许服务端返回更少结果 |
| `type` | 保持接口已验证的 `fromPlayAll` 语义 |

不要在 UI 层通过“第一个歌单”猜测喜欢歌单。服务层应显式返回喜欢歌单 ID和喜欢歌曲 ID。

### 7.4 列表和播放行为

- 请求成功后先过滤缺少歌曲信息或无有效 ID 的条目。
- 结果有效时更新页面列表，但不因“查看结果”自动播放。
- 点击“播放全部”时才调用 `PlayTransientQueueAsync()`。
- 新增 `PlaybackQueueContext.NeteaseIntelligenceRecommendations`。
- 心动推荐队列可以使用独立的随机播放设置；若第一阶段不提供独立设置，则保持服务端返回顺序且默认关闭随机。
- 请求或映射失败时保留旧心动推荐结果和当前播放队列。
- 连续点击“生成/重新生成”使用 single-flight 或取消旧请求，禁止结果交叉覆盖。
- 页面离开或账号切换后，旧请求不得回写页面。

### 7.5 状态和错误文案

至少区分：

- 未登录；
- 没有“我喜欢的音乐”歌单；
- 喜欢歌曲为空；
- 网络或接口错误；
- 返回结果为空；
- 请求已取消；
- 当前登录已过期。

不能把所有失败统一显示成“暂无推荐内容”。

## 8. 私人 FM 子选项卡计划

### 8.1 第一阶段页面职责

页面只负责展示状态和发送播放意图：

- 显示是否已登录；
- 显示“开始私人 FM”或“继续私人 FM”；
- 显示当前 FM 歌曲和后续已缓冲项目；
- 跳过当前歌曲；
- 标记“不喜欢”；
- 退出私人 FM；
- 显示加载、错误和重试状态。

页面不直接订阅底层播放器的 track-ended 事件并自行请求 API；这些行为属于 `INeteasePersonalFmService`。

### 8.2 队列行为

建议新增：

```text
PlaybackQueueContext.NeteasePersonalFm
```

行为必须明确：

1. 点击“开始私人 FM”后才请求首批内容。
2. 首批内容请求成功后再替换为私人 FM 临时队列。
3. 请求失败时保留当前队列。
4. 队列剩余数量低于阈值时异步请求下一批。
5. 同一时刻只能有一个补队列请求。
6. 接口重复返回相同歌曲时按远程歌曲 ID 去重，但不能因过度去重造成队列永久为空。
7. 用户切换到本地、每日推荐或心动推荐队列后，旧 FM 会话不得继续追加。
8. 离开页面但仍在 FM 上下文时，可以继续自动补充。
9. 退出 FM 后取消未完成请求，清除 FM 会话状态，但不要求强制停止播放器；具体采用停止还是保留当前曲目须在实施前确认。

### 8.3 跳过和不喜欢

- “跳过”只执行下一首，不调用不喜欢接口。
- “不喜欢”调用私人 FM 对应垃圾桶/不喜欢 API；成功后跳过当前项。
- 不喜欢请求失败时应提示错误，不应伪装成功。
- 不喜欢和补队列请求必须共享会话代次保护。
- 日志只能记录接口名称、错误类别和数量，不能记录 Cookie、完整播放 URL 或响应正文。

### 8.4 AI DJ 边界

AI DJ 不纳入第一阶段强制验收，原因是其返回资源可能包含讲解音频和歌曲，当前 Dopamine 在线 `TrackViewModel`、歌词、喜欢、右键菜单和歌曲详情行为均假设在线项是歌曲。

后续启用 AI DJ 前至少需要：

- 为在线项增加明确的歌曲/讲解音频类型；
- 禁用讲解音频不适用的喜欢、歌词、专辑和搜索操作；
- 明确讲解标题、封面和播放进度显示；
- 验证临时音频 URL 的过期与缓存；
- 明确普通私人 FM 与 AI DJ 切换时是否替换队列。

## 9. 加载、取消和生命周期

### 9.1 懒加载

- 父页首次显示时只激活每日推荐。
- 心动推荐首次切换时只加载前置状态，不自动生成结果。
- 私人 FM 首次切换时只读取会话状态，不自动请求或播放。
- 已加载子页再次切换回来时优先保留当前列表和操作状态。

### 9.2 请求隔离

每个子功能维护独立的：

- `CancellationTokenSource`；
- 请求代次；
- loading/refreshing 标志；
- 错误消息；
- single-flight 门禁。

账号切换、登出或会话过期时：

- 清空心动推荐和私人 FM 的账号相关状态；
- 取消旧账号请求；
- 结束旧账号私人 FM 会话；
- 不复用上一账号的数据；
- 每日推荐继续沿用现有账号隔离缓存逻辑。

## 10. 导航、状态和兼容性

### 10.1 保留持久化值

第一阶段推荐保持：

```csharp
DailyRecommendations = 6
```

但将 `CollectionViewModel` 的导航目标从：

```text
CollectionDailyRecommendations
```

改为：

```text
CollectionNeteaseRecommendations
```

这样旧配置中的 `SelectedCollectionPage=6` 会直接打开新父页，无需设置迁移。

如后续希望把枚举成员重命名为 `NeteaseRecommendations`，应单独提交，并明确保持数值 `6`。该重命名只改善代码语义，不是本功能的必要条件。

### 10.2 子选项卡状态

第一阶段建议：

- 首次启动默认每日推荐；
- 同一父页面实例内记住当前子页；
- 不立即增加持久化设置，避免旧设置迁移和设置文件膨胀；
- 若用户明确要求跨重启记忆，再增加 `Netease.SelectedRecommendationTab`，并对越界值回退到每日推荐。

### 10.3 搜索框语义

音乐库顶部现有全局搜索框在父页仍可见：

- 每日推荐继续响应搜索；
- 心动推荐列表也应响应歌曲名、歌手和专辑过滤；
- 私人 FM 页面若不提供列表过滤，应在该子页明确禁用或忽略搜索，并避免让用户误以为搜索会改变 FM 内容；
- 不允许全局搜索文字触发新的网易云网络请求。

## 11. 本地化、主题和可访问性

### 11.1 建议资源键

至少增加或调整：

```text
Netease_Recommendations                 网易云推荐
Netease_Daily_Recommendations           每日推荐
Netease_Intelligence_Recommendations    心动推荐
Netease_Personal_Fm                     私人 FM
Netease_Generate_Intelligence           生成心动推荐
Netease_Regenerate_Intelligence         重新生成
Netease_Based_On_Liked_Music            根据“我喜欢的音乐”生成
Netease_Start_Personal_Fm               开始私人 FM
Netease_Continue_Personal_Fm            继续私人 FM
Netease_Exit_Personal_Fm                退出私人 FM
Netease_Dislike                         不喜欢
Netease_Liked_Music_Empty               请先喜欢一些歌曲
```

实施规则：

- `CollectionMenu.xaml` 改用新的 `Language_Netease_Recommendations`。
- 保留 `Language_Netease_Daily_Recommendations` 给每日推荐子选项卡使用。
- 先补齐 `EN.xml`，再补齐 `ZH-CN.xml`；其他语言使用项目现有英文回退机制。
- 不在 XAML、ViewModel 和服务层重复硬编码可见文案。

### 11.2 UI 和可访问性

- 外层和内层 Pivot 的选中状态必须层级清楚。
- 窄窗口与 100%/150% 缩放下，三个子选项卡不能被顶部搜索框遮挡。
- 选中态不能只依赖颜色。
- 键盘可切换子页并触发主要按钮。
- loading、空状态、登录提示和错误必须有文本。
- 焦点从子选项卡进入列表或主按钮时顺序稳定。
- 亮色、暗色和用户颜色方案下保持可读性。

## 12. API、隐私和许可证门禁

### 12.1 API Contract 门禁

参考 HyPlayer 源码中存在：

- `PlaymodeIntelligenceListApi`；
- `PersonalFmApi`；
- `PersonalFmTrashApi` 或等价不喜欢接口；
- `AiDjContentRcmdInfoApi`。

但 Dopamine 实施前必须确认当前锁定的 `HyPlayer.NeteaseApi 0.1.2` 实际 NuGet 资产包含所需 Contract 和 DTO。不能仅根据 HyPlayer 当前工作树推断已发布包一定包含相同 API。

如果包中缺失：

1. 优先评估升级到包含所需 Contract 的已发布兼容版本；
2. 重新执行 net48、binding redirect、Portable 打包和许可证门禁；
3. 只有许可证明确且 NuGet 方案不可用时，才评估最小源码适配；
4. 不为此引入 Node/Fastify 或远程中转服务。

### 12.2 隐私边界

- 不记录 Cookie、`MUSIC_U`、刷新令牌或完整请求头。
- 不记录完整二维码 URL、设备标识和响应正文。
- 不持久化心动推荐算法参数或私人 FM 临时播放 URL。
- 私人 FM 和心动推荐缓存若后续增加，必须按账号隔离并采用与现有每日推荐相同等级的 DPAPI 保护。
- 默认不持久化私人 FM 队列；重启后由用户重新开始 FM。

## 13. 分阶段实施任务

### 阶段 0：基线和 Contract 验证

- [ ] 记录当前分支、远端和工作树状态，保护与本任务无关的用户文件。
- [ ] 确认现有每日推荐、登录、播放 URL、歌词、喜欢和不感兴趣基线。
- [ ] 确认 `HyPlayer.NeteaseApi 0.1.2` 是否包含心动推荐、私人 FM 和私人 FM 不喜欢 Contract。
- [ ] 确认喜欢歌单 ID 的可靠获取方式，不使用“第一个歌单”猜测。
- [ ] 确认私人 FM 退出时的产品语义：停止播放，还是只停止自动补队列。
- [ ] 确认第一阶段不包含 AI DJ。

### 阶段 1：建立父页面和导航兼容

- [ ] 新增 `CollectionNeteaseRecommendations` View/ViewModel。
- [ ] 在父页建立三个子选项卡壳，默认选中每日推荐。
- [ ] 将外层菜单文案改为“网易云推荐”。
- [ ] 保留 `CollectionPage.DailyRecommendations = 6`。
- [ ] 将枚举值 `6` 的导航目标改为新父页面。
- [ ] 在 `App.xaml.cs` 注册父页面。
- [ ] 在 `Dopamine.csproj` 显式登记新增 XAML 和代码文件。
- [ ] 验证旧 `SelectedCollectionPage=6` 能打开新父页面。

### 阶段 2：嵌入现有每日推荐

- [ ] 将现有每日推荐控件放入第一个子选项卡。
- [ ] 验证加载、刷新、搜索、播放、喜欢、不感兴趣和右键菜单未回归。
- [ ] 验证 06:00 定时刷新在父子生命周期下不会重复注册。
- [ ] 验证快速切换子页不会重复请求或丢失现有列表。
- [ ] 保持原缓存、队列上下文和随机播放设置不变。

### 阶段 3：扩展心动推荐服务

- [ ] 新增喜欢歌单/喜欢歌曲业务模型和查询方法。
- [ ] 在 `INeteaseApiClient` 适配心动推荐 Contract。
- [ ] 在 `INeteaseMusicService` 封装登录、参数、错误映射和会话代次。
- [ ] 增加本地歌曲、空喜欢列表和无喜欢歌单保护。
- [ ] 新增 `NeteaseIntelligenceRecommendation` 业务模型。
- [ ] 新增心动推荐队列上下文。
- [ ] 请求成功后再更新列表，播放按钮点击后才替换队列。

### 阶段 4：实现心动推荐 UI

- [ ] 新增心动推荐 View/ViewModel。
- [ ] 增加“生成”“重新生成”“播放全部”和单曲播放。
- [ ] 复用每日推荐的在线 Track 映射、列表列和搜索行为，避免复制映射代码。
- [ ] 增加登录、空喜欢列表、空结果、错误、重试和取消状态。
- [ ] 防止连续点击和旧请求回写。
- [ ] 失败时保留旧结果和当前播放队列。

### 阶段 5：实现私人 FM 服务

- [ ] 在 API 客户端适配私人 FM 和不喜欢接口。
- [ ] 新增私人 FM 业务模型。
- [ ] 新增 `INeteasePersonalFmService` 和实现。
- [ ] 新增 `NeteasePersonalFm` 队列上下文。
- [ ] 实现开始、继续、补队列、跳过、不喜欢、退出和会话取消。
- [ ] 用户离开页面但仍处于 FM 队列时继续补充。
- [ ] 用户切换到其他队列或登出后停止补充。
- [ ] 防止重复返回、重复请求和无限空结果循环。

### 阶段 6：实现私人 FM UI

- [ ] 新增私人 FM View/ViewModel。
- [ ] 显示开始/继续、跳过、不喜欢、退出和当前状态。
- [ ] 订阅协调服务的只读状态，不在页面中实现连续播放算法。
- [ ] 增加登录、加载、错误和重试状态。
- [ ] 验证页面切换、窗口隐藏、托盘播放和恢复主窗口时状态一致。

### 阶段 7：本地化、主题和可访问性

- [ ] 补齐英语和简体中文资源键。
- [ ] 检查其他语言回退。
- [ ] 完成内层 Pivot 样式。
- [ ] 验证窄窗口、缩放、亮暗主题和颜色方案。
- [ ] 验证键盘、焦点和可读状态文本。

### 阶段 8：构建和运行态验收

- [ ] 执行 XML/XAML、旧式 `.csproj` 登记、编码、行尾和 `git diff --check` 静态检查。
- [ ] 推送后确认 GitHub Actions `Build Portable` 成功。
- [ ] 下载真实 Portable 产物，确认新增依赖被打包。
- [ ] 在真实账号环境完成每日推荐、心动推荐和私人 FM 手动验收。
- [ ] 明确区分静态检查、云构建和真实运行态证据。

## 14. 建议文件变更范围

### 14.1 新增

```text
Dopamine/Views/FullPlayer/Collection/CollectionNeteaseRecommendations.xaml
Dopamine/Views/FullPlayer/Collection/CollectionNeteaseRecommendations.xaml.cs
Dopamine/ViewModels/FullPlayer/Collection/CollectionNeteaseRecommendationsViewModel.cs

Dopamine/Views/FullPlayer/Collection/CollectionIntelligenceRecommendations.xaml
Dopamine/Views/FullPlayer/Collection/CollectionIntelligenceRecommendations.xaml.cs
Dopamine/ViewModels/FullPlayer/Collection/CollectionIntelligenceRecommendationsViewModel.cs

Dopamine/Views/FullPlayer/Collection/CollectionPersonalFm.xaml
Dopamine/Views/FullPlayer/Collection/CollectionPersonalFm.xaml.cs
Dopamine/ViewModels/FullPlayer/Collection/CollectionPersonalFmViewModel.cs

Dopamine.Services/Online/Netease/INeteasePersonalFmService.cs
Dopamine.Services/Online/Netease/NeteasePersonalFmService.cs
```

必要时新增专用业务模型文件；若继续沿用 `NeteaseModels.cs`，应控制文件体积和职责。

### 14.2 修改

```text
Dopamine.Core/Enums/PageEnums.cs（原则上保留值 6，仅在后续可选重命名）
Dopamine/Views/FullPlayer/Collection/CollectionMenu.xaml
Dopamine/ViewModels/FullPlayer/Collection/CollectionViewModel.cs
Dopamine/App.xaml.cs
Dopamine/Dopamine.csproj

Dopamine.Services/Online/Netease/INeteaseApiClient.cs
Dopamine.Services/Online/Netease/NeteaseApiClient.cs
Dopamine.Services/Online/Netease/INeteaseMusicService.cs
Dopamine.Services/Online/Netease/NeteaseMusicService.cs
Dopamine.Services/Online/Netease/NeteaseModels.cs 或拆分后的模型文件
Dopamine.Services/Playback/PlaybackQueueContext.cs
Dopamine.Services/Playback/IPlaybackService.cs（仅在 FM 协调确有需要时）
Dopamine.Services/Playback/PlaybackService.cs
Dopamine.Services/Dopamine.Services.csproj

Dopamine/Languages/EN.xml
Dopamine/Languages/ZH-CN.xml
```

### 14.3 不应修改或应避免扩大范围

- 不改本地音乐数据库表和迁移版本。
- 不把在线歌曲写入持久化 `QueuedTrack`。
- 不改网易云 Cookie 的持久化安全模型。
- 不重写现有每日推荐缓存。
- 不顺带迁移网易云搜索、歌单、评论、云盘或下载功能。
- 不在第一阶段引入 AI DJ 混合资源。
- 不为私人 FM 引入 Node/Fastify 或远程中转服务。

## 15. 验收标准

### 15.1 导航和 UI

- [ ] 音乐库顶部显示“网易云推荐”，位置仍在“文件夹”右侧。
- [ ] 原配置 `SelectedCollectionPage=6` 能正常打开新页面。
- [ ] 页面内按顺序显示“每日推荐”“心动推荐”“私人 FM”。
- [ ] 首次进入默认选中“每日推荐”。
- [ ] 快速切换子页无空白、重叠、重复实例或明显卡顿。
- [ ] 窄窗口、150% 缩放、亮色和暗色主题下布局可用。

### 15.2 每日推荐

- [ ] 原有加载、刷新、缓存、搜索、播放和右键菜单不回归。
- [ ] 喜欢、取消喜欢和不感兴趣不回归。
- [ ] 06:00 刷新调度不重复注册。
- [ ] 子页切换不会清空已有推荐列表或当前队列。

### 15.3 心动推荐

- [ ] 明确显示基于“我喜欢的音乐”。
- [ ] 未登录、无喜欢歌单和喜欢歌曲为空时显示可理解提示。
- [ ] 当前播放本地歌曲时不会把本地 ID 传给网易云。
- [ ] 生成结果为空、网络失败或登录过期时不清空当前队列。
- [ ] 连续点击不会产生并发结果覆盖。
- [ ] 播放全部使用独立在线临时队列，从有效首曲开始。

### 15.4 私人 FM

- [ ] 切换到子页不会自动播放或替换队列。
- [ ] 点击开始后，首批成功才进入 FM 队列。
- [ ] 接近队尾或播放结束时能补充下一批。
- [ ] 快速切页、隐藏窗口或进入托盘不导致重复补队列。
- [ ] 用户切换到其他队列、登出或退出 FM 后不再追加旧 FM 内容。
- [ ] 跳过不等于不喜欢；不喜欢成功后按既定语义跳过。
- [ ] 私人 FM 请求失败不破坏此前有效播放队列。

### 15.5 安全和回归

- [ ] 日志不包含 Cookie、令牌、完整播放 URL、设备标识或响应正文。
- [ ] 本地音乐播放、队列持久化、随机顺序、歌词、托盘和后台播放不回归。
- [ ] 每日推荐和本地队列的现有随机播放设置不被错误复用到私人 FM。
- [ ] Portable 包含所有新增或升级依赖。

## 16. 验证边界

实现后至少执行：

```powershell
git diff --check
git status --short
```

并进行：

- XML/XAML 可解析性检查；
- `.csproj` 新文件登记检查；
- 资源键在 `EN.xml` 和 `ZH-CN.xml` 中的完整性检查；
- 敏感日志文本检查；
- GitHub Actions `Build Portable`；
- 下载 Portable 产物后的真实登录和播放验收。

必须明确：

- `git diff --check`、XML 解析和静态回读不是编译成功。
- GitHub Actions 构建成功只证明编译和打包成功。
- 网易云接口、心动推荐算法参数、私人 FM 连续补队列和真实 UI 行为必须由运行态手动验证。

## 17. 提交和回滚策略

建议按以下边界拆分提交：

1. 父页面、外层改名和持久化导航兼容；
2. 每日推荐嵌入与生命周期适配；
3. 心动推荐 API/服务和模型；
4. 心动推荐 UI 与队列上下文；
5. 私人 FM API 和会话协调服务；
6. 私人 FM UI；
7. 本地化、主题和可访问性；
8. 清理与文档。

回滚原则：

- 心动推荐失败时可以回滚第 3、4 组，保留“网易云推荐”父页和每日推荐。
- 私人 FM 出现播放回归时可以回滚第 5、6 组，不影响每日推荐和心动推荐。
- 不在同一提交中同时改变导航、升级 API 包、修改播放内核和实现 FM 补队列。
- 不在功能尚未运行态验证前删除现有每日推荐实现或泛化其队列上下文。

## 18. 最终任务目标摘要

```text
外层入口：每日推荐 → 网易云推荐
持久化兼容：继续使用 CollectionPage 数值 6
父页面：新增 CollectionNeteaseRecommendations
子选项卡：每日推荐 | 心动推荐 | 私人 FM
每日推荐：完整复用现有成熟实现
心动推荐：基于“我喜欢的音乐”，显式生成，成功后才允许替换队列
私人 FM：显式开始，由独立会话服务负责连续补队列、退出和不喜欢
AI DJ：后续扩展，不纳入首轮强制验收
服务边界：UI → INeteaseMusicService/INeteasePersonalFmService → INeteaseApiClient
加载边界：子页懒加载，独立取消、错误和请求代次
验证边界：静态检查 + GitHub Actions Portable + 真实账号运行态验收
```

该方案能实现用户要求的导航重组，同时最大限度复用 Dopamine 已完成的每日推荐、账号隔离缓存、在线播放和歌词链路，并把风险最高的私人 FM 连续播放逻辑限制在独立服务中，避免破坏现有本地播放与每日推荐功能。
