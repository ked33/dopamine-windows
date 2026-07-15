# HyPlayer 网易云核心链路移植实施计划

来源设计：

- `docs/superpowers/specs/2026-07-14-hyplayer-netease-migration-design.md`

目标链路：

```text
设置 → 在线：二维码登录或 Cookie 登录
→ 音乐库 → 文件夹右侧的“每日推荐”
→ 官方播放 URL
→ Dopamine 播放
→ 网易云歌词
```

## 1. 实施规则

- 只迁移 HyPlayer 的低层网易云 API 能力，不迁移 UWP 页面、播放器或高层 Provider。
- 第一阶段不需要 Fastify、Node、远程后端或 `UnblockNeteaseMusic` 进程。
- `UnblockNeteaseMusic` 仅通过预留 fallback provider 接口在后续阶段实现。
- 每个提交保持可构建、可独立回滚，不把依赖、登录 UI、播放内核和歌词塞进一个提交。
- 不初始化或修改本地 HyPlayer 子模块；实现以固定提交和锁定 NuGet 包为依据。
- 不提交真实 Cookie、账号 ID、昵称、推荐响应、播放 URL 或二维码 unikey。
- 不把 Cookie 写入 `Settings.xml`，不为了调试临时打印后再“计划删除”。
- 旧式 `.csproj` 的每个新增 `.cs`、`.xaml`、Page 和 Resource 必须显式登记。
- 英语语言文件必须先有所有新 key；简体中文提供完整翻译，其他语言走现有英语回退。
- 本机若没有完整 .NET Framework 4.8/Visual Studio 工具链，只做静态检查，并以 GitHub Actions `Build Portable` 和干净 Windows 运行验证作为构建/运行证据。
- 不把 `git diff --check`、XML 解析或静态回读描述为编译成功。

## 2. 阶段依赖

```text
兼容性门禁
  ↓
API 适配器与内部模型
  ↓
DPAPI 会话与登录状态
  ↓
设置 → 在线登录 UI
  ↓
在线 Track 身份与临时队列
  ↓
播放源解析与 CSCore 输入
  ↓
每日推荐选项卡
  ↓
歌词接入
  ↓
文件型消费者分流
  ↓
安全、错误、本地化、CI 与手动回归
```

不得提前展示“每日推荐”播放按钮，除非临时队列和播放源解析已经可用；避免把半成品入口交给用户。

## 3. 开工前基线

开始任何代码提交前执行并记录：

```powershell
git status --short --branch
git log -1 --oneline
git submodule status "D:\D-Download\第三方网易云\HyPlayer"
```

检查项：

1. Dopamine 工作树没有与本任务重叠的用户改动。
2. 分支和远端状态明确。
3. HyPlayer API 固定参考提交仍为 `d28099727252674823794e33e199cdf0265bf402`。
4. NuGet 源可访问，GitHub Actions `Build Portable` 当前基线为绿色。
5. 记录一个不含账号数据的本地播放回归基线：本地 MP3 播放、Seek、下一首和歌词。
6. 确认不需要修改数据库版本；第一阶段没有 `DbMigrator` 任务。

停止条件：

- 工作树有无法安全区分的同文件用户改动。
- 当前分支已有失败的 Portable 基线且原因未知。
- 固定 API 包或源码许可证不能确认。

## 4. Commit 1：第三方依赖、许可证与 Portable 兼容性门禁

建议提交信息：

```text
Validate Netease API dependencies on .NET Framework
```

### 4.1 目的

先证明 `HyPlayer.NeteaseApi 0.1.2` 和二维码库能在 Dopamine 的 net48、`packages.config`、测试工程和 Portable 产物中工作。此提交不发网络请求、不做 UI。

### 4.2 文件

预计修改：

- `Dopamine.Services/packages.config`
- `Dopamine.Services/Dopamine.Services.csproj`
- `Dopamine/packages.config`
- `Dopamine/Dopamine.csproj`
- `Dopamine.Tests/packages.config`
- `Dopamine.Tests/Dopamine.Tests.csproj`
- `Dopamine/App.config`
- `Dopamine.Services/app.config`
- `Dopamine.Tests/App.config`
- `Dopamine.Packager/PackagerConfiguration.xml`
- 第三方许可证清单；仓库没有现成清单时新增 `THIRD-PARTY-NOTICES.md`。

可新增：

- `Dopamine.Tests/NeteaseDependencySmokeTests.cs`

### 4.3 任务

1. 从 NuGet 注册信息再次确认并锁定：
   - `HyPlayer.NeteaseApi 0.1.2`
   - 选中的 `QRCoder` net48/netstandard2.0 兼容版本
   - 所有传递依赖的精确版本。
2. 在固定 HyPlayer API 提交中确认许可证和版权归属。
3. 如果许可证缺失或不兼容，停止；不要用“Dopamine 本身是 GPL”替代依赖许可证确认。
4. 只把 `HyPlayer.NeteaseApi` 放进 `Dopamine.Services`，不要引用高层 `HyPlayer.NeteaseProvider`。
5. QRCoder 放在实际生成二维码的项目；若二维码 PNG 在 Services 生成，则依赖归 Services，否则归 Dopamine UI 项目。
6. 让 NuGet 生成/更新旧式 `.csproj` 引用和 `packages.config`，然后手工审查每个 HintPath 和 `Private`。
7. 统一以下可能冲突的 BCL 包：
   - `System.Text.Json`
   - `System.Text.Encodings.Web`
   - `System.Memory`
   - `System.Buffers`
   - `System.Runtime.CompilerServices.Unsafe`
   - `System.Threading.Tasks.Extensions`
   - `Microsoft.Bcl.AsyncInterfaces`
   - `System.IO.Pipelines`
   - NuGet 实际解析出的其他依赖。
8. 更新主程序和测试配置的 binding redirect；以实际程序集版本为准，不猜版本。
9. 在 `PackagerConfiguration.xml` 的 Portable 文件列表中显式加入 API、QRCoder 和全部运行时依赖。
10. 添加不联网的 smoke test：只验证能加载 API 程序集、构造 Handler/Option 和解析一个本地 DTO，不触发真实网易云请求。
11. 在 CI artifact 中逐项检查新增 DLL，而不是只看 MSBuild 退出码。
12. 在干净 Windows 环境启动一次 Portable，打开现有音乐库并播放本地歌曲。

### 4.4 门禁判定

通过条件：

- restore 和 Release/Portable 构建成功。
- 测试进程和主程序没有程序集绑定异常。
- Portable 中依赖完整。
- 本地歌曲播放没有因 BCL 包升级回归。
- 许可证记录完整。

失败回退：

- 先回滚整个依赖提交。
- 再评估“最小源码抽取 + Newtonsoft.Json”方案。
- 源码抽取仍受许可证门禁约束；许可证不明确时停止。

## 5. Commit 2：Dopamine API 适配器和内部 DTO

建议提交信息：

```text
Add isolated Netease API adapter
```

### 5.1 目的

建立唯一引用 HyPlayer Contract 的适配层，并给登录、推荐、播放 URL 和歌词提供稳定的 Dopamine 模型。

### 5.2 新增文件

- `Dopamine.Services/Online/Netease/INeteaseApiClient.cs`
- `Dopamine.Services/Online/Netease/NeteaseApiClient.cs`
- `Dopamine.Services/Online/Netease/NeteaseModels.cs`
- `Dopamine.Services/Online/Netease/NeteaseError.cs`
- `Dopamine.Services/Online/Netease/INeteaseMusicService.cs`
- `Dopamine.Services/Online/Netease/NeteaseMusicService.cs`
- `Dopamine.Tests/NeteaseApiMappingTests.cs`

修改：

- `Dopamine.Services/Dopamine.Services.csproj`
- `Dopamine.Tests/Dopamine.Tests.csproj`
- `Dopamine/App.xaml.cs`

### 5.3 任务

1. `NeteaseApiClient` 封装单例 `NeteaseCloudMusicApiHandler`。
2. 按 HyPlayer 当前入口核对 Handler 构造：复用一个长期存活的 `HttpClient`，不要每次请求创建/释放客户端；默认 AdditionalParameters 保持库默认值，只有固定提交证明必需时才增加参数。
3. 只实现以下调用：
   - Web WEAPI 二维码 key Contract
   - Web WEAPI 二维码轮询 Contract
   - Web WEAPI 账号状态 Contract
   - `RecommendSongsApi`
   - `SongUrlApi`
   - `LyricApi`
4. 不添加未使用的搜索、歌单、评论和 FM Contract。
5. 所有公开返回值转换为 Dopamine DTO；UI 和 Playback 项目不得 import `HyPlayer.NeteaseApi`。
6. 对每个响应处理：
   - `IsError`
   - Error 为空
   - Value 为空
   - 集合为空
   - 首项为空
   - 未知 code
   - DTO 字段缺失。
7. 定义结构化错误码，不把第三方异常正文直接变成 UI 文案。
8. 提供 Cookie 的 replace/snapshot/clear 操作；snapshot 必须复制，不能把内部可变字典直接暴露。
9. `NeteaseMusicService` 实现：
   - 每日推荐按会话 generation + 中国时间 06:00 推荐日缓存，并使用 DPAPI CurrentUser 跨重启保存当日有序快照。
   - 播放 URL 按 songId + `standard` + generation 缓存 15 分钟。
   - 歌词按 songId 缓存 24 小时。
   - 登录变化清缓存。
10. URL 结果映射 `code`、空 URL、`FreeTrialInfo`、类型、码率和大小。
11. 日志只记录方法、歌曲 ID、错误分类和响应 code；禁止记录 Cookie、unikey、完整 URL 和响应 JSON。
12. 添加 fake Contract/fixture 测试，不联网。

### 5.4 验证

- 每种 API 的成功、空值和错误映射单测。
- 同一歌曲并发 URL 请求只有一个底层任务。
- 失败任务会从 single-flight 字典移除。
- 强制刷新会跳过 15 分钟 URL 缓存。
- 切换 session generation 后旧缓存不可见。
- 代码搜索确认 HyPlayer 命名空间只出现在适配器及适配器测试中。

### 5.5 登录风控兼容补充（2026-07-15）

参考 `D:\D-Download\第三方网易云\NeriPlayer` 的登录实现，按以下顺序修正当前 `802` 后返回 `8821` 和网页 Cookie 被误判失效的问题：

1. 新增 Dopamine 自有的 WEAPI Contract，继续使用固定的 `HyPlayer.NeteaseApi 0.1.2` 加密实现，不引入 WebView2；在第一次请求前为包内源码生成 JSON 类型表组合反射元数据回退，使自定义 DTO 可序列化。
2. 二维码 key/轮询统一使用 `type=1` 和 `noCheckToken=true`；每次创建会话生成 `sDeviceId`、`NMTID` 和 `chainId`。
3. ViewModel 只渲染会话返回的 `/st/platform/scanlogin` 二维码内容，不再自行拼接旧的 `/login?codekey=` 地址。
4. Web 登录请求补齐桌面 UA、`Origin`、`x-os: web`、`x-channelsource` 和 `nm-gcore-status`；轮询额外增加 `x-loginmethod: QrCode` 和 `x-login-chain-id`。`ydDeviceToken` 保留在会话模型中，当前无安全来源时传空值。
5. `803` 后捕获 `x-refresh-token`，仅在响应没有 `MUSIC_U` 时作为回退写入候选 Cookie；随后必须通过账号状态接口再次验证。
6. 手工 Cookie 登录使用相同的 PC Web WEAPI 账号状态 Contract，并补齐 `os=pc`、`appver=8.10.35`，但不覆盖用户提供的值。
7. 自建 `HttpClientHandler` 并设置 `UseCookies=false`，避免系统 Cookie 容器和 Handler 的受控 Cookie 字典产生双重来源。
8. `8821` 映射成独立风控错误，不能落入网络错误；新增中英文提示。
9. 单元测试覆盖二维码 URL、chainId、设备标识形状、Cookie 默认值以及 `8821` 不属于正常二维码状态。
10. 日志只允许记录 Cookie 数量、响应码、`profile/account` 是否存在和错误分类；严禁记录 Cookie 键名或值、设备标识、二维码 key/URL 和响应正文。

回滚：删除适配层和注册，不影响现有 Dopamine 功能。

## 6. Commit 3：Cookie 解析、DPAPI 会话和登录状态机

建议提交信息：

```text
Persist Netease sessions with Windows DPAPI
```

### 6.1 新增文件

- `Dopamine.Services/Online/Netease/NeteaseCookieHeaderParser.cs`
- `Dopamine.Services/Online/Netease/INeteaseSessionStore.cs`
- `Dopamine.Services/Online/Netease/DpapiNeteaseSessionStore.cs`
- `Dopamine.Services/Online/Netease/INeteaseSessionService.cs`
- `Dopamine.Services/Online/Netease/NeteaseSessionService.cs`
- `Dopamine.Tests/NeteaseCookieHeaderParserTests.cs`
- `Dopamine.Tests/NeteaseSessionStoreTests.cs`
- `Dopamine.Tests/NeteaseSessionServiceTests.cs`

修改：

- `Dopamine.Services/Dopamine.Services.csproj`
- `Dopamine.Tests/Dopamine.Tests.csproj`
- `Dopamine/App.xaml.cs`

### 6.2 Cookie 解析任务

1. 接受可选 `Cookie:` 前缀和分号分隔的 `name=value`。
2. 只按第一个等号拆分，保留值中的后续等号。
3. 拒绝空名称、CR/LF、控制字符和大于 32 KiB 的输入。
4. 同名项采用最后一个值，绝不在错误消息中附加原值。
5. 解析结果先作为候选 Cookie；`LoginStatusApi` 成功前不得覆盖有效持久会话。
6. 手动登录成功后清理原始输入引用。

### 6.3 DPAPI 任务

1. 会话路径：`SettingsClient.ApplicationFolder()\Netease\session.dat`。
2. 定义带版本号的会话 envelope。
3. 使用 `ProtectedData.Protect(..., CurrentUser)`；可使用固定应用 entropy。
4. 在 `Dopamine.Services.csproj` 显式加入 net48 所需的 `System.Security` 引用，并以 CI 编译确认实际程序集解析。
5. 先写 `.tmp`，flush 后原子替换。
6. 解密、格式、I/O 和权限失败均返回结构化结果，不在构造函数抛出。
7. 使用后清零序列化明文字节；处理 `SecureString` 时清零 BSTR。
8. Logout 删除正式文件和 `.tmp`，清空 API Cookie 与所有 session cache。
9. Portable 移到另一用户导致 DPAPI 失败时回到 SignedOut，不降级明文。

### 6.4 状态机任务

实现：

```text
SignedOut / Restoring / SigningIn / SignedIn /
OfflineUnknown / Expired / Error
```

1. Restore 解密后调用 `LoginStatusApi`。
2. 明确未授权才删除会话。
3. 网络错误进入 `OfflineUnknown`，保留加密文件。
4. 二维码 generation 保证旧响应不能覆盖新登录。
5. `803` 后必须再次 LoginStatus 验证再保存。
6. Cookie 登录在验证成功后保存。
7. 账号切换递增 session generation 并清除推荐、URL、歌词缓存。
8. 通过 `SessionChanged` 通知 UI。
9. 在 `InitializeShell` 显示主窗口后安全启动 Restore；不能阻塞启动，也不能使用未观察异常的裸 `async void`。

### 6.5 测试

- Cookie parser 完整边界测试。
- DPAPI 当前用户 round-trip。
- 损坏、空、未知版本和不可解密文件。
- 网络失败不删除会话。
- 401/未登录会清理会话。
- 两次并发登录只有最新 generation 成功。
- Logout 幂等。
- 捕获日志，断言不含测试 Cookie 值。

回滚：删除 session 文件和服务注册；不触碰 `Settings.xml`。

## 7. Commit 4：设置 → 在线的二维码与 Cookie 登录 UI

建议提交信息：

```text
Add Netease sign-in to online settings
```

### 7.1 文件

修改：

- `Dopamine/Views/FullPlayer/Settings/SettingsOnline.xaml`
- `Dopamine/Views/FullPlayer/Settings/SettingsOnline.xaml.cs`
- `Dopamine/ViewModels/FullPlayer/Settings/SettingsOnlineViewModel.cs`
- `Dopamine/Languages/EN.xml`
- `Dopamine/Languages/ZH-CN.xml`
- `Dopamine/Dopamine.csproj`

按实现选择可新增：

- `Dopamine/Utils/QrCodeImageFactory.cs`

### 7.2 XAML 任务

1. 在页面 StackPanel 最顶部加入 `TitleLabel`“网易云音乐”。
2. 在现有搜索提供商和 Last.fm 区域之前放置网易云区域。
3. 未登录时显示使用 `MenuPivot` 风格的两个方法：
   - 二维码登录
   - Cookie 登录
4. QR 容器约 180×180，固定白底和足够 quiet zone。
5. QR 区显示当前状态和“刷新二维码”。
6. Cookie 区使用 `PasswordBox`，说明支持标准 Cookie Header。
7. 登录期间使用 `ProgressRing` 并禁用两个入口。
8. 已登录状态显示强调色成功图标、昵称和“退出登录”。
9. 不显示头像，避免在第一阶段引入远程图片缓存。
10. 保持现有设置页可滚动；验证窄窗口不出现水平裁剪。

### 7.3 ViewModel/代码隐藏任务

1. ViewModel 只保存登录状态、账号展示、QR 图像和命令，不保存原始 Cookie 字符串。
2. `PasswordBox` 通过 code-behind 把 `SecureString` 交给一次性命令，并立即清空。
3. Loaded：若未登录且没有活跃 QR，创建一次。
4. Unloaded：取消 QR 轮询。
5. 登录方法切换：取消不再可见方法的工作。
6. 轮询间隔 2 秒，处理 800/801/802/803。
7. 过期后停住，等待用户刷新，不自动无限生成。
8. Cookie 登录与 QR 登录使用同一个 `IsSigningIn` 门禁。
9. Logout 弹确认；成功后 UI 回到未登录。
10. 所有状态通过语言 key 显示。

### 7.4 手动验证

- 深色/浅色二维码可扫。
- 801 等待扫码、802 等待确认、803 登录成功、800 过期。
- 切换 Tab、离开设置和关闭程序后无残余轮询。
- 有效和无效 Cookie。
- 输入 Cookie 后控件被清空。
- 登录状态变化不破坏 Last.fm、在线搜索和歌词设置。
- 日志全文搜索不含测试 Cookie 和 unikey。

回滚：只移除网易云 UI；会话/API 服务仍可保留给后续诊断。

## 8. Commit 5：在线 Track 身份与临时播放队列

建议提交信息：

```text
Add transient queues for online tracks
```

### 8.1 新增文件

- `Dopamine.Services/Entities/TrackSourceKind.cs`
- `Dopamine.Services/Entities/TrackSourceInfo.cs`

修改：

- `Dopamine.Services/Entities/TrackViewModel.cs`
- `Dopamine.Services/Playback/IPlaybackService.cs`
- `Dopamine.Services/Playback/PlaybackService.cs`
- `Dopamine.Services/Playback/QueueManager.cs`
- `Dopamine.Services/Dopamine.Services.csproj`
- `Dopamine.Tests/Dopamine.Tests.csproj`

新增测试：

- `Dopamine.Tests/OnlineTrackViewModelTests.cs`
- `Dopamine.Tests/TransientQueueTests.cs`

### 8.2 Track 任务

1. 本地 Track 默认 `SourceKind.LocalFile`。
2. 在线 Track 带 `ProviderId=netease`、`RemoteId` 和可选 ArtworkUrl。
3. 推荐 mapper 创建合成 Track：
   - `Path`/`SafePath = netease://song/{id}`
   - 填充标题、艺术家、专辑、时长和文件名显示值。
   - 为现有 `TrackViewModel` 直接读取 `.Value` 的 nullable 字段提供安全默认值，例如 `TrackNumber=0`。
4. `DeepCopy` 保留来源信息；来源对象不得被多个副本意外可变共享。
5. 增加 `IsLocalFile`、`IsOnline` 和 `SupportsFileMetadataActions`。
6. 来源信息不加入 SQLite `Track` 或 `QueuedTrack` 列。

### 8.3 临时队列任务

1. 在 `IPlaybackService` 增加 `PlayTransientQueueAsync`。
2. 增加 `QueuePersistenceMode.Durable/Transient` 内部状态。
3. 在线推荐替换运行时队列时设置 Transient。
4. Transient 模式停止并忽略 queued-track 保存计时器。
5. 不删除磁盘中最后一个 Durable 本地队列快照。
6. 从本地音乐库重新建立队列时恢复 Durable 并按现有逻辑保存。
7. 在线歌曲不更新本地 PlayCount/SkipCount/DateLastPlayed。
8. 当前阶段拒绝在线/本地混合持久化；明确返回不支持结果，不静默写伪路径。
9. 保持随机顺序模型和 QueueID 复制语义。

### 8.4 测试

- 在线 Track DeepCopy 后 ID 和 SourceInfo 正确。
- 合成 SafePath 在推荐列表中稳定且不同歌曲不碰撞。
- Transient 队列不会调用 `IQueuedTrackRepository.SaveQueuedTracksAsync`。
- 从 Transient 切回本地后恢复保存。
- 在线切歌不调用本地 counter repository。
- 本地队列恢复、强制终止保存和随机顺序现有测试不回归。

回滚：移除来源和临时队列；数据库无需回滚。

## 9. Commit 6：播放源解析、官方 URL 与 CSCore 输入

建议提交信息：

```text
Resolve and play official Netease audio sources
```

### 9.1 新增文件

- `Dopamine.Core/Audio/AudioSource.cs`
- `Dopamine.Services/Playback/IPlaybackSourceResolver.cs`
- `Dopamine.Services/Playback/PlaybackSourceResolver.cs`
- `Dopamine.Services/Playback/NeteasePlaybackSourceResolver.cs`
- `Dopamine.Services/Playback/IOnlineAudioFallbackProvider.cs`
- 直连不通过门禁时新增 `Dopamine.Services/Playback/NeteaseTemporaryAudioCache.cs`
- 对应单元测试。

修改：

- `Dopamine.Core/Audio/IPlayer.cs`
- `Dopamine.Core/Audio/CSCorePlayer.cs`
- `Dopamine.Services/Playback/IPlaybackService.cs`
- `Dopamine.Services/Playback/PlaybackService.cs`
- `Dopamine.Services/Playback/PlaybackFailureReason.cs`
- `Dopamine/App.xaml.cs`
- 三个旧式 `.csproj`。

### 9.2 兼容探针

在正式选择实现前，用不含 Cookie 的受控公开音频 URL验证当前 CSCore.Ffmpeg：

- HTTPS 打开。
- 总时长。
- Seek。
- 暂停/恢复。
- 停止后释放。
- 取消快速切歌。

再用真实账号手动验证网易云可能返回的实际格式。探针结果记录在提交说明或测试记录中，不提交 URL token。

判定：

- 全部稳定：使用 RemoteUri。
- 任一关键能力不稳定：使用临时音频缓存。

### 9.3 AudioSource 任务

1. `IPlayer.Play` 从裸 filename 改为 `AudioSource`。
2. 本地文件路径继续走 `File.OpenRead`，保留特殊字符兼容。
3. RemoteUri 走 `FfmpegDecoder(uri)`，不执行 `File.Exists`。
4. `PlaybackService` 先 Resolve，再创建 Player。
5. 所有 `IPlayer` 实现和测试替身一起更新。
6. 解析失败不创建音频设备，不改变当前队列身份。
7. 先核对 `HyPlayer.NeteaseApi.RequestAsync` 是否接受 `CancellationToken`；若不接受，使用调用前后取消检查和 generation 丢弃迟到结果，并在验证记录中明确底层 HTTP 请求不能被真正中止。

### 9.4 官方解析任务

1. 本地 resolver 直接返回 LocalFile。
2. Netease resolver 按 `RemoteId` 请求 `ResolveOfficialAudioAsync`。
3. 检查登录、code、URL、试听和权限。
4. API 返回 HTTP 时优先升级为 HTTPS；不主动把 HTTPS 降级为 HTTP。
5. 缓存 URL 打开失败时清缓存并强制刷新一次。
6. 第二次失败返回结构化 `PlaybackFailureReason`。
7. 为当前解析建立 CTS；Stop、下一首、上一首和新选曲取消旧请求。
8. generation 检查保证旧解析结果不能开始播放。
9. 日志记录 songId 和错误分类，不记录 URL。

### 9.5 临时缓存分支

如果兼容探针选择缓存：

1. 路径为 `Cache\Temporary\Netease`。
2. 下载到随机 `.part`，完成后原子改名。
3. 取消/失败删除 `.part`。
4. 最大约 512 MiB，最长 24 小时，按最近使用清理。
5. 缓存只用于播放兼容，不提供“下载”或离线承诺。
6. 登出、启动和退出进行 best-effort 清理。
7. 按响应 `Content-Length` 上报下载比例；现有进度控件叠加低透明度缓冲条，缓存命中为 100%，取消/失败/停止时隐藏。

### 9.6 fallback seam

1. `IOnlineAudioFallbackProvider` 定义 Id、Order、CanHandle 和 TryResolveAsync。
2. 第一阶段 provider 集合为空或 Null Provider。
3. 只有官方明确 `NoCopyright`/`EmptyUrl` 才允许未来进入 fallback。
4. Authentication、Network、Cancelled 不进入 fallback。
5. 不加入 Node、Unblock 包、端口或设置。

### 9.7 测试与手动验证

- 本地文件解析不变。
- 在线 URL缓存、强刷一次和不无限重试。
- 空 URL、试听、会员、无版权和登录失效。
- 取消慢解析后旧歌曲不会突然播放。
- 播放、暂停、Seek、上一首、下一首和自然结束。
- MP3/FLAC/WMA 本地回归。
- fallback 集合为空时返回官方错误。

回滚：可以只回退在线 resolver，并保留 AudioSource 的本地兼容改造；若本地播放有任何回归，回退整个提交。

## 10. Commit 7：文件夹右侧“每日推荐”选项卡

建议提交信息：

```text
Add daily recommendations collection tab
```

### 10.1 新增文件

- `Dopamine/Views/FullPlayer/Collection/CollectionDailyRecommendations.xaml`
- `Dopamine/Views/FullPlayer/Collection/CollectionDailyRecommendations.xaml.cs`
- `Dopamine/ViewModels/FullPlayer/Collection/CollectionDailyRecommendationsViewModel.cs`

修改：

- `Dopamine.Core/Enums/PageEnums.cs`
- `Dopamine/Views/FullPlayer/Collection/CollectionMenu.xaml`
- `Dopamine/ViewModels/FullPlayer/Collection/CollectionMenuViewModel.cs`
- `Dopamine/ViewModels/FullPlayer/Collection/CollectionViewModel.cs`
- `Dopamine/ViewModels/FullPlayer/FullPlayerViewModel.cs`
- `Dopamine/App.xaml.cs`
- `Dopamine/Dopamine.csproj`
- `Dopamine/Languages/EN.xml`
- `Dopamine/Languages/ZH-CN.xml`

### 10.2 导航任务

1. 追加 `CollectionPage.DailyRecommendations = 6`。
2. 在“文件夹”PivotItem 后追加“每日推荐”。
3. `CollectionMenuViewModel` 读取保存值时使用 `Enum.IsDefined` 校验；未知值回退 `Artists`，让设置损坏和功能回滚不会留下空白页。
4. 注册 `CollectionDailyRecommendations` Prism view。
5. `FullPlayerViewModel` 顺序执行父内容 Region 和菜单 Region 导航，确保 `Collection` 父视图先完成加载。
6. `CollectionMenuViewModel` 等待 `CollectionRegion` 注册后直接导航 7 个页面；`CollectionViewModel` 仅保留方向动画。
7. 对 `false + 无异常` 的瞬态结果进行有限重试，并用 generation 丢弃快速切换产生的旧请求。
8. 不改变 0～5 旧值，不修改数据库或设置迁移。

### 10.3 ViewModel 任务

1. 不继承本地文件操作繁重的 `TracksViewModelBase`；只复用必要的播放、搜索和事件能力。
2. 属性至少包含：
   - Items/CollectionViewSource
   - SelectedItem
   - Count
   - IsInitialLoading
   - IsRefreshing
   - IsLoggedIn
   - HasError
   - ErrorMessageKey
3. Loaded：已登录时检查当前中国时间 06:00 推荐日；同日优先读取加密快照，跨日才请求 API。
4. Unloaded：取消页面请求并阻止旧响应更新 UI。
5. 监听 SessionChanged；登录成功自动加载，登出立即清空。
6. Refresh 命令重新读取当日缓存且禁止并发；只有当日尚无成功快照时才联网重试。
7. PlayAll 调用 `PlayTransientQueueAsync(items, first, PlaybackQueueContext.NeteaseDailyRecommendations)`；该上下文默认顺序播放，并独立持久化随机设置。
8. 双击/Enter 使用完整列表建立临时队列，从选中项开始。
9. 搜索按标题、艺术家、专辑过滤；搜索清空后恢复全部。
10. 后台刷新保留旧列表；首次错误显示内联重试。

### 10.4 XAML 任务

1. 布局与 `CollectionTracks.xaml` 对齐：`DockPanel Margin="10,20,10,26"`。
2. 标题行左侧数量 +“首每日推荐”。
3. 右侧透明“刷新”和强调色“播放全部”。
4. `DataGridEx` 开启 Recycling 虚拟化。
5. 列：歌曲、播放指示器、艺术家、专辑、时长。
6. 不显示评分、红心、文件计数和本地上下文菜单。
7. 使用专用右键菜单，仅保留跳转播放歌曲、播放选中项、下一曲、添加到当前在线队列和在线搜索。
8. 首次加载显示中心 ProgressRing。
9. 未登录、空结果、错误各有中心/内联状态。
10. 不下载远程封面；使用纯表格与默认 Now Playing 封面。

### 10.5 UX 验证

- “每日推荐”位于“文件夹”正右侧。
- 旧 `SelectedCollectionPage` 数值仍导航到原页面。
- 中文、英文、800/1024/1440 宽度和 100%/150% DPI。
- 搜索框在每日推荐页面有效。
- 未登录不会发 RecommendSongs 请求。
- 同一推荐日重进和重启复用 DPAPI 缓存；同日手动刷新不重复请求，跨 06:00 自动更新。
- 双击和 Enter 从所选行开始。
- 播放全部只有一个活动请求，重复点击不建立多队列。
- 本地随机开启时每日推荐仍默认按列表顺序；在每日推荐中切换随机不会改写本地随机设置。

回滚：删除第 7 个 Pivot 和 Region view；保留通用的枚举范围校验，并把已保存的值 `6` 回退到 `Artists`；登录与播放服务仍独立存在。

## 11. Commit 8：按网易云歌曲 ID 获取歌词

建议提交信息：

```text
Load Netease lyrics for online tracks
```

### 11.1 文件

修改：

- `Dopamine/ViewModels/Common/LyricsControlViewModel.cs`
- `Dopamine/ViewModels/Common/LyricsViewModel.cs`
- `Dopamine/Views/Common/LyricsControl.xaml`
- `Dopamine/Dopamine.csproj`（仅有新增文件时）
- `Dopamine.Tests/Dopamine.Tests.csproj`

新增测试：

- `Dopamine.Tests/NeteaseLyricsIntegrationTests.cs`

### 11.2 任务

1. `RefreshLyricsAsync` 在调用 MetadataService 前检查 TrackSourceInfo。
2. Netease 分支按 RemoteId 调 `INeteaseMusicService.GetLyricsAsync`。
3. 普通 LRC 构造成 `Lyrics(text, "Netease Cloud Music", Online)`。
4. 复用 `LyricsViewModel.SetLyrics` 和现有 `ILyricsService`。
5. 每次切歌递增歌词 generation 或取消旧 CTS。
6. 旧歌曲晚到响应不得覆盖当前歌词。
7. 无歌词使用现有空态，不影响播放。
8. 在线歌曲隐藏/禁用添加、编辑、保存到音频文件。
9. 本地歌词读取、旁车 LRC、第三方下载和编辑逻辑保持原顺序。
10. 第一阶段忽略翻译、罗马音和逐字歌词，但 DTO 保留扩展字段。

### 11.3 验证

- 同步 LRC 正常逐行高亮和自动滚动。
- 无歌词显示空态。
- 快速 A→B→C 切歌最终只显示 C。
- 暂停、Seek 和恢复后高亮正确。
- 在线歌曲不出现保存歌词到文件。
- 本地内嵌歌词、LRC 和手动编辑回归。

回滚：只移除在线分支；本地歌词完全保留。

## 12. Commit 9：所有文件型消费者按来源分流

建议提交信息：

```text
Guard file-only features for online playback
```

### 12.1 预计修改

- `Dopamine.Services/Entities/TrackViewModel.cs`
- `Dopamine/ViewModels/Common/CoverArtControlViewModel.cs`
- `Dopamine/ViewModels/Common/PlaybackInfoControlViewModel.cs`
- `Dopamine/ViewModels/Common/Base/ContextMenuViewModelBase.cs`
- `Dopamine/Services/Notification/LegacyNotificationService.cs`
- `Dopamine/Services/Notification/NotificationService.cs`
- `Dopamine.Services/Appearance/AppearanceService.cs`
- `Dopamine.Services/Blacklist/BlacklistService.cs` 或其调用点。
- 必要时 `CollectionFoldersViewModel`/FoldersService 的当前路径处理。

### 12.2 任务

1. 在线歌曲 Rating/Love setter 不调用 MetadataService 或本地仓库。
2. PlaybackInfo 对在线歌曲隐藏评分和红心控件。
3. 封面控制显示 Dopamine 默认封面，不按伪路径查本地元数据。
4. Legacy 通知仍显示标题/歌手，封面为空或默认。
5. SMTC 更新标题/歌手/专辑，不按伪路径取缩略图。
6. AppearanceService 不为在线伪路径提取封面色。
7. Context menu 隐藏：资源管理器、编辑标签、删除文件、添加本地播放列表、加入本地黑名单。
8. 本地播放计数、文件 metadata updater 和 folder highlighting 不处理在线伪路径。
9. Last.fm scrobble 和 Discord Rich Presence 保持可用。
10. 每个分流以 `IsLocalFile` 为依据，不以 `Path.StartsWith` 到处散落判断。

### 12.3 验证

- 在线歌曲播放期间日志没有伪路径文件读取异常。
- 通知和 SMTC 正常显示文字。
- Now Playing 评分、红心、歌词编辑和文件命令不可用。
- Last.fm/Discord 开启时不崩溃。
- 所有本地文件操作回归。

回滚：按消费者逐个回滚；如果回滚后会访问伪路径，应同时临时关闭每日推荐播放入口。

## 13. Commit 10：错误提示、本地化与安全审计

建议提交信息：

```text
Harden Netease errors and sensitive logging
```

### 13.1 文件

- `Dopamine.Services/Playback/PlaybackFailureReason.cs`
- `Dopamine.Services/Playback/PlaybackFailedEventArgs.cs`
- 登录、每日推荐和播放相关 ViewModel。
- `Dopamine/Languages/EN.xml`
- `Dopamine/Languages/ZH-CN.xml`
- 相关日志辅助代码和测试。

### 13.2 任务

1. 完成错误分类：认证、会话过期、网络、限流、无版权、会员、试听、空 URL、API 变化、解码、临时下载、取消、未知。
2. 每类错误绑定语言 key，不把 exception.Message 直接展示给用户。
3. 登录失效通过 SessionService 统一清理，不由多个 ViewModel各自删文件。
4. 网络错误不误删 Cookie。
5. 播放失败自动跳过时给出一次简短提示；连续失败聚合，避免每首弹窗风暴。
6. 审计所有新增日志、catch、ToString、HTTP handler 和第三方 debug 输出。
7. 添加敏感模式测试：
   - `MUSIC_U=`
   - `__csrf=`
   - `codekey=`
   - 网易云 CDN 完整 URL
   - `Cookie:` Header。
8. 确认 crash/error 对象不包含原始响应正文。
9. 确认 EN 包含全部 key，ZH-CN 无遗漏和重复。
10. XML 用 UTF-8 感知工具解析；不要因终端乱码误判文件损坏。

### 13.3 验证

- 测试日志输入包含假 Cookie，输出不包含秘密值。
- 断网、401、无版权和空 URL分别显示正确文案。
- 语言切换后新页面即时更新。
- 所有语言文件可解析，英语默认 key 集完整。

回滚：错误映射可独立回滚，但 Cookie 日志保护不可被回滚为明文。

## 14. Commit 11：自动化、Portable 与完整回归

建议提交信息：

```text
Verify Netease playback integration
```

### 14.1 自动化检查

在可用工具链执行：

```powershell
nuget restore .\Dopamine.sln -NonInteractive
msbuild .\Dopamine.sln /p:Configuration=Release /p:Platform=AnyCPU /m
```

然后：

- 运行 `Dopamine.Tests`。
- 解析所有修改的 XML/XAML/csproj/config。
- `git diff --check`。
- 搜索敏感模式和未登记的新源文件。
- 比对构建输出与 `PackagerConfiguration.xml`。
- 触发并等待 GitHub Actions `Build Portable`。

若本机缺少构建环境：

- 记录“未本地编译”。
- 完成静态/XML/依赖清单检查。
- 推送必须经用户授权。
- 以 Actions 作为编译/打包证据，不能把它说成运行态账号验证。

### 14.2 Portable artifact 审计

1. 下载对应 commit 的 artifact。
2. 检查新增 DLL 和版本。
3. 检查 `Dopamine.exe.config` redirects。
4. 在没有开发环境的干净 Windows 用户启动。
5. 确认应用不会从开发机 `packages` 目录偶然加载 DLL。
6. 确认 DPAPI 会话只对创建它的 Windows 用户可用。

### 14.3 手动测试矩阵

登录：

- 二维码等待、扫码、确认、成功、过期、刷新、取消。
- 正确 Cookie、错误 Cookie、含等号 Cookie、切换账号。
- 重启恢复、断网恢复、明确失效、Logout。
- Portable 复制到另一 Windows 用户后要求重新登录。

每日推荐：

- 未登录空态。
- 首次加载、同日缓存、手动刷新、空结果、网络错误。
- 搜索过滤。
- 中文/英文、深色/浅色、DPI 和窄窗口。

播放：

- 双击中间歌曲，从所选歌曲开始。
- 播放全部。
- 暂停、恢复、Seek、上一首、下一首、随机、循环、自动下一首。
- URL缓存命中和失败后强刷一次。
- 无版权、会员、试听和连续不可播放歌曲。
- 快速连续点击不同歌曲，旧解析不抢播。

歌词：

- 同步歌词、高亮、Seek、无歌词、快速切歌。
- 在线歌曲无编辑/保存入口。
- 本地歌词完整回归。

集成：

- 托盘控制、通知、SMTC、Last.fm、Discord。
- 本地 MP3/FLAC/WMA、本地封面、评分、红心。
- 本地队列跨重启和强制终止恢复。
- 在线队列退出后不写入 SQLite；重启恢复最后本地队列。

### 14.4 最终安全检查

```powershell
git grep -n -I -e "MUSIC_U=" -e "__csrf=" -e "codekey="
git status --short --branch
git diff --check
```

搜索结果只允许测试中的固定假数据模式，不能有真实值。还需人工检查 git diff、日志样例和测试 fixture。

## 15. 每提交通用检查清单

每个实现提交完成前：

1. `git status --short`，确认只包含本提交文件。
2. 回读所有修改附近上下文。
3. 确认旧式 `.csproj` 已登记新增文件。
4. XML/XAML/config 可解析。
5. `git diff --check`。
6. 新测试通过；不能运行时明确记录。
7. 不存在真实账号或 URL。
8. 本地歌曲至少做与改动风险相称的回归。
9. 提交信息只描述一个主题。
10. 未经用户明确要求不 push。

## 16. 总体验收标准

- “每日推荐”是“文件夹”右侧的第 7 个音乐库 Pivot。
- 登录位于“设置 → 在线”，二维码和 Cookie 两种方式均可用。
- Cookie 使用 DPAPI CurrentUser 加密，`Settings.xml` 和日志没有秘密。
- 登录后可加载账号每日推荐。
- URL 在播放时懒获取并缓存 15 分钟，失败最多强刷一次。
- 在线歌曲进入明确的 Transient 队列，不写 Track/QueuedTrack 数据库。
- CSCore 能稳定直连，或通过已验证的临时文件回退播放。
- 普通 LRC 能显示、高亮和跟随 Seek。
- 无版权、会员、试听、网络和登录失效可区分。
- 本地文件功能和 Portable 打包无回归。
- 第一阶段没有运行 Fastify、Node 或 Unblock sidecar。
- fallback seam 已存在，但不会伪装成可用的 Unblock 实现。

## 17. 停止并重新评估的条件

- API 包许可证不能确认。
- System.Text.Json/BCL 冲突要求大范围升级现有框架依赖。
- Portable 仍遗漏依赖或只能依赖开发机 GAC/packages 才启动。
- 远程直连和临时缓存都不能稳定 Seek/切歌。
- 为接在线歌曲必须立即迁移整个 Track/QueuedTrack 数据库。
- Cookie 出现在任何日志、异常、Settings.xml 或测试产物。
- 在线改动破坏本地播放、队列恢复或强制终止持久化。
- API 变化导致无法可靠区分登录成功和失败。

达到停止条件时保留已通过的低层提交，关闭用户入口，先修架构或依赖，不靠吞异常继续堆功能。

## 18. 后续阶段：`UnblockNeteaseMusic`

当前阶段完成后另开设计与实施计划，至少包含：

1. 选定并锁定 Unblock 版本、Node/runtime 版本和许可证。
2. 用户显式启用设置与风险/版权说明。
3. loopback-only sidecar、随机端口、健康检查和生命周期管理。
4. 官方提供者优先，只有 `NoCopyright`/`EmptyUrl` 进入 fallback。
5. 登录失效、网络故障和取消不触发 Unblock。
6. 不向不需要登录的后备音源发送网易云 Cookie。
7. 匹配结果校验歌曲名、歌手、专辑、时长和版本。
8. sidecar 崩溃不影响本地音乐与官方可播放歌曲。
9. Portable 打包、杀毒误报、代理冲突、端口占用和退出清理测试。
10. 可独立关闭和回滚，不修改每日推荐 UI、队列模型和歌词主链路。

未来 provider 顺序固定为：

```text
OfficialNeteaseProvider
→ configured IOnlineAudioFallbackProvider(s)
→ structured failure
```

不要在当前第一阶段加入一个总是返回成功、空 URL 或静默代理的占位 Unblock provider。
