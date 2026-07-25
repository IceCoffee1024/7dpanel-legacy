---
state: Draft
document_role: Change Record
last_updated: "2026-07-25"
---

# Admin 综合概览、主机身份与脚本重启三阶段设计草案

> 本文暂存已经讨论确认的后端与 Admin 概览方向，属于未实施的 Change Record，不是当前实现证据。当前产品、界面、架构和测试事实分别以[产品需求](../../PRD.md)、[界面设计](../../design.md)、[系统架构](../../architecture.md)和[测试策略](../../test.md)为准；后端与 Admin 的未来边界分别见[后端目标蓝图](../../architecture/backend-target-blueprint.md)和[Admin 前端目标蓝图](../../architecture/admin-frontend-target-blueprint.md)。

> 本草案不授权实施、发布、真实 7DTD 操作或 Git 提交。进入实施计划前，必须先把最终批准的产品行为写入 `CAP-01`、`CAP-05` 和 `NFR-02` 的权威合同，并按阶段确认是否需要拆分为独立设计规格。

## 目标与原则

将现有 Admin Dashboard 参考实现逐步收敛为单服综合运维首页，使服主能够判断面板、游戏、主机与数据采样是否正常，查看服务器和主机身份、当前与近期指标、需要处理的问题和近期活动，并由 `Owner` 立即启动预先配置的平台重启脚本。本草案不再假定 Admin 页面只消费 `GET /api/v1/health`；现有参考实现已经消费游戏统计、系统平台、系统指标、重启设置和控制台命令接口，新接口需要明确聚合这些边界。

设计遵循以下原则：

- 保留匿名 `GET /health` 与 `GET /api/v1/health` 的精确三字段合同，不把认证运维信息扩入健康端点。
- Overview 使用独立认证快照；面板在线、游戏就绪、数据新鲜度和性能健康是不同状态，不以单一布尔值或缺失 FPS 推导全部状态。
- 保留最后成功数据及其真实时间；单个数据源失败不让整个概览伪装成离线或正常。
- 游戏对象只在 SevenDays Adapter 内复制为不可变产品快照，不向 Web 或浏览器暴露游戏活对象。
- 卷根路径仅按 `Owner` 权限裁剪后进入主机卷明细；脚本路径、其他主机路径、环境变量、凭据、有效 Token 和控制台敏感原文不进入概览响应。旧重启策略中的自定义命令只返回是否已配置和安全显示名，不返回可执行原文。
- 当前系统用户名、稳定设备唯一标识和公网 IP 是第一阶段必需字段，但只向 `Owner` 返回。
- 重启操作只承诺操作系统已创建重启脚本进程；不等待脚本、不确认脚本退出码，也不声明服务器重启成功。
- 三个阶段分别形成可验证的纵向切片；后续阶段不要求第一阶段提前建设通用指标平台或未来抽象。

## 与现有 Admin Dashboard 参考实现的对照结论

本节记录对 `src/views/Dashboard/` 参考实现的吸收范围。参考实现用于字段、交互和展示行为对照，不是本仓库当前实现证据；它不能替代后端代码、测试和部署验证。

### 页面入口、模块与可吸收的展示经验

- 参考实现入口是 `/:locale/dashboard`，使用 `createWebHashHistory`、认证路由守卫和 `keepAlive`；新版仍以本仓库 Admin 的 `/` 登录后首页和现有路由边界为准，不机械迁移旧路由方案。
- 旧 `Overview`、`Status`、`Monitor`、`SystemInfo`、`QuickActions`、`RecentActivity` 只作为信息归属参考。新版可以重新组合组件，不要求保留六个同名目录或旧主列/侧列结构。
- `MyCard`、环形图、折线图、顶部状态信号和响应式列数是可借鉴的表达方式，不是本 spec 固定的 UI 约束；只保留容量、趋势、状态层级和小屏可读性这些用户结果。
- `vue-i18n`、亮/暗主题适配、Tooltip、图例和百分比坐标轴继续作为可吸收行为，但图表实现不要求复制旧组件。
- 参考实现的 `src/views/Dashboard/index.vue` 同时承担请求、定时器、状态推导、格式化和布局编排；目标路由视图只作为组合表面，请求与副作用迁移到局部 composable。

### 字段逐项核对

以下字段均纳入本草案的对照范围，不因统一 Overview 响应而丢失：

| 领域 | 已确认字段/行为 | 参考实现状态 | 目标吸收方式 |
|---|---|---|---|
| 总体状态 | 在线/离线、FPS 健康等级、FPS `>=18` 健康、`10-18` 性能下降、`<10` 严重异常 | 已实现 | 保留阈值展示；最终阈值是否固化仍需 Windows/Linux 负载基线确认；缺失 FPS 改用独立不可用状态 |
| 基础信息 | 服务器名称、服务端版本、地区、语言、游戏服务器 IP、游戏模式 | 已实现 | 继续使用 `serverName`、`serverVersion`、`region`、`language`、`serverIp`、`gameMode`；明确 `serverIp` 是游戏连接地址还是公网地址 |
| 玩家指标 | 在线玩家数、最大玩家数、历史玩家数 | 已实现 | 使用 `onlinePlayers`、`maxOnlinePlayers`、`historicalPlayers`，并补充同次快照派生的 `offlineHistoricalPlayers` |
| 性能指标 | FPS、RSS、Heap | 已实现 | 使用 `serverFps`、`residentSetBytes`、`managedHeapBytes`；旧 `Heap` 是 `GC.GetTotalMemory(false)` 的托管堆，不再称为 Unity Heap；进程 CPU 与整机 CPU 分开命名 |
| 游戏对象指标 | 僵尸数/最大僵尸数、动物数/最大动物数、Chunk、Chunk GameObjects、Entity、Item | 已实现 | 继续使用 `zombies`、`maxZombies`、`animals`、`maxAnimals`、`chunks`、`chunkGameObjects`、`entities`、`items`；高级诊断按第二阶段缓存和主线程边界返回 |
| 运行时间 | 服务端运行时长、游戏内天数、小时、分钟、下次自动重启时间 | 已实现 | 旧 `uptime` 改为 `worldSessionUptimeSeconds`，明确它来自 Unity 世界/场景会话；7DTD 进程时长单独使用 `processUptimeSeconds`；保留 `gameTime` 和 `nextRunAtUtc` |
| 容量图 | 在线玩家/空闲槽位、活跃僵尸/剩余容量、活跃动物/剩余容量、已用/可用物理内存、已用/可用磁盘、MB 转换、使用率 | 已实现 | 保留现有 `Status` 与 `Doughnut` 展示；后端传输字节，前端按语言和容量自适应显示 |
| CPU/RAM 趋势 | CPU 使用率折线、RAM 使用率折线、当前 CPU、当前 RAM、已用物理内存、最近 10 点、约 3 秒间隔、约 30 秒窗口、Tooltip、图例、百分比坐标轴 | 已实现 | 保留短期趋势；服务端提供采样时间和状态，前端只保留当前标签页 10 点 |
| 网络趋势 | 入站、出站、累计收发字节差、采样时间差、`b/Kb/Mb/Gb` 单位、自适应纵轴、最近 10 点 | 已实现 | 保留计算和图表行为；后端过滤回环接口并明确这是主机流量，不宣称为单个 7DTD 进程流量 |
| 详细概览 | 服务器名称、服务器 IP、端口、地区、语言、运行时长、游戏时间、游戏名称、游戏世界、服务端版本、游戏模式、游戏难度 | 已实现 | `GamePrefs.GameName` 映射为 `saveGameName`；产品标题单独使用 `gameTitle`；`GamePrefs.GameWorld` 只映射一个 `worldName`，不在没有独立来源时复制出 `mapName` |
| 快捷操作 | 下次计划重启、重启服务器、关闭服务器、确认框、加载状态、按钮禁用、成功/失败提示、重启/关闭控制台命令主线程执行 | 已实现 | 保留确认、加载和禁用行为；重启改为脚本启动语义，关闭按钮保留现有能力但不纳入本次脚本重启响应合同 |
| 近期活动 | 登录、玩家进入、玩家离开、活动文本、相对时间、最多 8 条、超过 3 条滚动、活动总数、最近更新时间、空状态 | 已实现 | 保留现有视觉和 8 条限制；数据源改为后端固定用途 SQLite 摘要，并补充管理操作和脚本启动事件 |
| 主机平台 | 操作系统、CPU 型号、CPU 核心数、系统内存、设备名称、设备型号、设备类型、设备唯一标识、操作系统版本、当前系统用户名 | 已实现大部分 | 保留 `SystemInfo` 展示；增加 OS family、运行时版本、可空 CPU 频率、架构、公网 IPv4/IPv6、进程 ID、启动时间；设备 ID 和用户名改为 Owner-only |
| 生命周期 | 进入页面读取平台信息和下次重启时间；每 3 秒刷新游戏统计、CPU、内存、磁盘、网络；激活恢复；停用暂停；异常保留已有数据、缺失显示未知 | 已实现基础版 | 保留激活/停用暂停和异常保留；改为概览、轻量指标、高成本诊断、公网 IP 分层刷新，并增加页面可见性、取消请求和手动重新采样 |
| 本地化与适配 | vue-i18n、亮色/暗色/主题重建图表、桌面多列、中屏两列、小屏单列 | 已实现 | 直接吸收现有机制，不固定新的像素布局 |

### 参考实现已有接口与新边界的映射

参考 Admin 当前使用以下接口，目标不要求浏览器继续并行拼装它们：

| 参考接口 | 当前用途 | 目标边界 |
|---|---|---|
| `GET /api/GameServer/Stats` | 游戏统计、FPS、玩家、游戏时间、游戏对象、服务器基础信息 | 作为 `game` 快照的数据来源，最终由 `GET /api/v1/overview` 或第二阶段诊断接口聚合 |
| `GET /api/Devices/SystemPlatformInfo` | 主机平台和系统身份 | 作为 `host` 静态身份来源，增加 Owner-only 裁剪和设备 ID 派生 |
| `GET /api/Devices/SystemMetricsSnapshot` | CPU 时间、物理内存、磁盘、网络累计计数器 | 作为轻量指标和容量图来源，最终由服务端采样器控制缓存 |
| `GET /api/Restart/Settings` | 下次计划重启时间和重启配置 | 作为首页只读重启策略摘要来源；第三阶段再决定是否复用其计划执行模型，首页不承担配置编辑 |
| `POST /api/GameServer/ExecuteConsoleCommand` | 发送 `ty-RestartServer` 或 `shutdown`，并指定 `inMainThread: true` | 旧接口只作为服务内兼容来源；目标 `shutdown` 使用固定的 `POST /api/v1/server/shutdown`，`restart` 使用 `POST /api/v1/server/restart` 启动 `.cmd/.sh`，浏览器不再获得任意命令入口 |
| `GET /api/sse` | 游戏事件和玩家进出事件 | 可作为实时事件输入，但近期活动最终由固定用途审计/活动存储提供摘要 |

新接口使用 `/api/v1` 命名是产品边界选择，不表示上述参考接口已经被替换。实现时需要明确聚合层与既有 API 的服务内复用关系。

`GET /api/sse` 仍承担 `CAP-01` 要求的认证实时连接、Welcome 快照和命名游戏事件职责；Overview 的轮询快照不能替换该合同。近期活动可以由 SSE/游戏事件写入固定用途存储，再由 Overview 返回安全摘要，但必须定义事件到达、持久化和页面读取之间允许的延迟。

### 参考 DTO 中存在但当前 Dashboard 未展示的字段

以下字段已在参考前端生成类型或重启模块中存在，不能在后续接口收敛时无意丢失；本轮确认后均需在综合首页获得明确位置或兼容处置：

- `StatsDto`：`offlinePlayers`、`isBloodMoon`、`maxHeap`、`chunkObservedEntities`。
- `SystemPlatformInfoDto`：`operatingSystemFamily`、`processorFrequency`、`frameworkVersion`。
- `MemoryInfoDto`：`totalVirtualMemory`、`availableVirtualMemory`。
- `DiskInfoDto`：`name`、`driveType`、`driveFormat`、`freeSpace`、`totalSize`、`usedSize`、`rootPath`；当前页面将多个磁盘汇总。
- `NetworkInfoDto`：`id`、`mac`、`name`、`trademark`、`networkType`、`speed`、`ipAddresses`、`bytesReceived`、`bytesSent`。
- `RestartFeatureSettingsDto`：`isEnabled`、`cronExpression`、`timeZoneId`、`warningLeadSeconds`、`warningMessage`、`warningStages`、`saveWorldBeforeRestart`、`restartMode`、`restartCommand`、`deferScheduledRestartDuringBloodMoonWindow`、`bloodMoonPreDuskProtectionHours`、`bloodMoonDeferMinutes`、`historyRetentionDays`、`nextRunAt`。
- `RestartRunRequestDto`：`reason`、`warningLeadSecondsOverride`、`restartModeOverride`。

这些字段按旧页面职责进入新版首页，而不是集中堆在顶部：

| 旧字段 | 新字段或处置 | 首页位置 | 权限与阶段 |
|---|---|---|---|
| `offlinePlayers` | `offlineHistoricalPlayers = max(historicalPlayers - onlinePlayers, 0)` | `Status` 玩家容量与历史玩家明细 | 第二阶段；`Owner,Admin,Viewer` |
| `isBloodMoon` | `bloodMoon.active` | 顶部服务器状态信号与 `Overview` 游戏时间附近 | 第二阶段；`Owner,Admin,Viewer` |
| `maxHeap` | `maxMemoryConsumption` 诊断信封；验证 `GameManager.MaxMemoryConsumption` 后才填充 `semantic` 与 `valueBytes` | `Status` 资源诊断明细 | 第二阶段；未验证时 `availability: "unverified"`，不伪造“最大 Unity Heap” |
| `chunkObservedEntities` | `chunkObservedEntities` | `Status`/高级游戏诊断 | 第二阶段；`Owner,Admin,Viewer` |
| `operatingSystemFamily` | `host.osFamily` | `SystemInfo` 操作系统明细 | 第一阶段；`Owner,Admin,Viewer` |
| `processorFrequency` | `host.processorFrequencyMHz` | `SystemInfo` CPU 明细 | 第一阶段；可空诊断值，不参与健康判断 |
| `frameworkVersion` | `host.runtimeVersion` | `SystemInfo` 运行环境明细 | 第一阶段；`Owner,Admin,Viewer` |
| `totalVirtualMemory`、`availableVirtualMemory` | `host.secondaryMemory.kind/totalBytes/availableBytes` | `Status` 内存详情 | 第一阶段；Windows 标记 `virtualAddressSpace`，Linux 标记 `swap`，禁止当成同一跨平台指标比较 |
| `DiskInfoDto` 全字段 | `host.storage.volumes[]`：`name`、`driveType`、`fileSystem`、`freeBytes`、`totalBytes`、`usedBytes`、`rootPath` | `Status` 磁盘汇总下的卷明细，并在 `SystemInfo` 提供主卷身份 | 第一阶段；容量对全部已授权角色可见，`rootPath` 仅 `Owner` 可见 |
| `NetworkInfoDto` 全字段 | `host.network.interfaces[]`：`id`、`macAddress`、`name`、`description`、`type`、`linkSpeedBitsPerSecond`、`ipAddresses`、累计收发字节 | `Monitor` 网络图下的接口明细，静态身份也可从 `SystemInfo` 进入 | 第三阶段；接口 ID、MAC、IP、描述仅 `Owner`，聚合速率对全部已授权角色可见 |
| `RestartFeatureSettingsDto` 全字段 | `restart.policy` 只读摘要；`restartCommand` 改为 `customCommandConfigured` 与安全显示名 | `QuickActions` 的“自动重启策略”详情 | 第一阶段读取，第三阶段执行计划；不得在首页编辑或返回命令原文 |
| `RestartRunRequestDto` | 作为旧立即重启请求兼容证据，不是状态字段 | 不展示为信息卡；新版脚本重启确认框只提交 `{ "confirmed": true }` | 明确不吸收 `reason` 和两个 override，避免绕过预配置脚本边界 |

除 `RestartRunRequestDto` 这种操作输入和 `restartCommand` 可执行原文外，旧 DTO 字段不得只停留在 Adapter 内部。非 `Owner` 请求需要保留结构化 `forbidden`/裁剪状态，不能因为字段敏感而让前端误判为数据缺失。

### 已确认的改造边界

- 不再把“当前只消费健康接口”作为现状描述；参考实现的多接口输入只作为聚合设计依据。
- 保留现有展示结构，但将 Dashboard 路由视图改造成组合表面，使用 `useOverview()`、`useLiveMetrics()`、`useGameDiagnostics()`、`useRestartServer()` 和统一页面可见性刷新逻辑承载状态与副作用。
- 不再使用单一 `gameServerStats` 是否存在推导全部在线状态；保留最后成功值、真实采样时间和分区 `available/stale/unavailable/forbidden`。
- `serverIp`（游戏连接地址）与 `host.publicNetwork.ipv4/ipv6`（主机公网地址）分开建模。
- `GamePrefs.GameName` 只表示存档名称，目标字段为 `saveGameName`；`gameTitle` 是产品标题；`GamePrefs.GameWorld` 只映射 `worldName`，没有独立证据时不返回重复的 `mapName`。
- 旧 `Heap` 只映射 `managedHeapBytes`，旧 `Uptime` 只映射 `worldSessionUptimeSeconds`；进程 RSS、进程运行时长和整机资源分别由主机采样器提供。
- 设备唯一标识以稳定 SHA-256 不透明值返回，当前系统用户名、设备 ID 和公网地址只对 `Owner` 返回；不能只依赖前端隐藏。
- Dashboard 保留“关闭服务器”按钮及现有确认/加载/错误提示；本草案只把“重启服务器”改为预配置脚本启动，不将关闭操作误写成脚本重启的同一响应合同。
- 关闭服务器也必须通过固定的服务端能力或固定命令白名单暴露为专用边界；浏览器不能提交任意命令、命令参数、脚本路径或环境变量。关闭操作与重启操作分别确认、分别授权、分别审计，并在 `capabilities.shutdownAvailable` 为假时禁用。
- Dashboard 暂不加入概览内玩家列表、玩家详情、地图钻取、长期历史查询、配置编辑或重启计划编辑；项目已有的独立玩家、地图和重启路由可继续作为后续导航目标。

## 已确认范围

### 第一阶段：综合首页基础闭环

- 面板状态、产品版本、游戏就绪状态和采样时间。
- 服务器名称、游戏标题、存档名称、服务端版本、世界、模式、难度、地区、语言、游戏连接 IP、端口、在线人数、人数上限、历史玩家数、FPS、世界会话时长、7DTD 进程时长、游戏时间和下次计划重启时间。
- `game.status` 明确表示游戏在线、离线、启动中、已停止或未知；它与面板状态、游戏就绪状态和数据新鲜度分开返回。
- 操作系统、OS family、版本、架构、运行时版本、CPU 型号/核心数/可空频率、系统物理内存、平台明确标记的虚拟地址空间或 Swap、设备名称/型号/类型、设备唯一标识、当前系统用户名、公网 IPv4/IPv6、7DTD 进程 ID、进程启动时间/运行时长、主机 CPU、进程 CPU、RSS、托管堆、磁盘汇总和全部固定卷明细。
- 自动重启策略的完整只读摘要，包括启用状态、计划表达式、时区、警告、保存世界、重启模式、是否配置自定义命令、血月延迟策略、历史保留期和下一次时间；首页不编辑这些设置。
- 最多 8 条安全近期活动和根据当前快照计算的需要处理项。
- Owner-only 立即启动 Windows `.cmd` 或 Linux `.sh` 重启脚本，并保留固定的 Owner-only 关闭服务器能力。

### 第二阶段：实时指标与高级游戏诊断

- 最近约 30 秒的 CPU、内存、FPS 和在线人数浏览器内短期趋势。
- 系统内存容量、离线历史玩家、血月状态、僵尸/上限、动物/上限、Chunk、Chunk GameObjects、Chunk Observed Entities、Entity 和 Item 当前统计，以及验证后才能命名暴露的 `GameManager.MaxMemoryConsumption`。
- 轻量指标与高成本游戏诊断使用不同采样周期和缓存。

### 第三阶段：网络、历史趋势与自动重启计划

- 主机非回环网络接口的实时入站/出站速度、接口身份、MAC、绑定 IP、类型、描述、链路速率和累计计数器；敏感接口字段仅 `Owner` 可见。
- 有限保留期和受限查询范围的 SQLite 指标历史。
- 复用同一脚本启动边界的自动重启计划执行与下一次执行时间；完整策略继续由独立重启页面编辑，综合首页只读展示。

## 明确不在当前草案内

- 多服务器聚合、服务器切换器或云端控制面。
- 保证重启脚本完成、保证新进程启动、回写最终重启结果或外部 Agent/Supervisor。
- 允许浏览器提交脚本路径、命令、参数、环境变量或任意控制台命令。
- 把设备唯一标识用作认证凭据或授权依据。
- 把公网 IP 自动检测结果视为权威网络配置。
- 第一阶段的历史时间范围查询、长期趋势、新增或编辑任意 Cron、配置编辑或重启计划编辑；已有计划表达式允许只读展示。
- 未经真实基线验证即固定 FPS `18/10` 健康阈值。

## 后端总体边界

### HTTP 接口

保留现有健康接口，新增：

| 接口 | 权限 | 责任 |
|---|---|---|
| `GET /api/v1/overview` | `Owner,Admin,Viewer` | 返回一次综合首页快照；设备/用户/公网、卷路径和网卡身份只对 `Owner` 可见，同时返回安全的只读重启策略摘要 |
| `POST /api/v1/server/restart` | `Owner` | 立即启动当前平台预配置的重启脚本并返回 `202 Accepted` |
| `POST /api/v1/server/shutdown` | `Owner` | 执行固定的安全关服能力；不接受任意控制台命令，并返回既有关服结果语义 |
| `GET /api/v1/metrics/live` | `Owner,Admin,Viewer` | 第二阶段轻量实时指标 |
| `GET /api/v1/diagnostics/game` | `Owner,Admin,Viewer` | 第二阶段高成本游戏诊断快照 |
| `GET /api/v1/metrics/history` | `Owner,Admin,Viewer` | 第三阶段受限历史趋势查询 |

`GET /api/v1/overview` 使用统一 `sampledAtUtc`，每个独立数据源另携带 `observedAtUtc` 与 `availability`。主机、游戏、近期活动或公网检测中的单项失败保持 HTTP 200，并把对应分区标记为 `available`、`stale`、`unavailable` 或 `forbidden`。认证、授权和整体请求格式错误仍使用既有 HTTP 与 Problem Details 语义。

### 第一阶段响应轮廓

```json
{
  "sampledAtUtc": "2026-07-24T10:00:00Z",
  "panel": {
    "status": "running",
    "product": "7DPanel",
    "version": "0.1.0",
    "gameReadiness": "ready"
  },
  "game": {
    "availability": "available",
    "status": "online",
    "serverName": "My Server",
    "gameTitle": "7 Days to Die",
    "saveGameName": "My Game Host",
    "serverVersion": "V 3.0.1 (b4)",
    "worldName": "Navezgane",
    "serverIp": "192.0.2.10",
    "gameMode": "GameModeSurvival",
    "difficulty": 2,
    "region": "NorthAmericaEast",
    "language": "English",
    "serverPort": 26900,
    "serverFps": 32.1,
    "onlinePlayers": 3,
    "maxOnlinePlayers": 8,
    "historicalPlayers": 12,
    "gameTime": {
      "days": 18,
      "hours": 14,
      "minutes": 35
    },
    "worldSessionUptimeSeconds": 7200,
    "observedAtUtc": "2026-07-24T09:59:58Z"
  },
  "host": {
    "availability": "available",
    "identityAvailability": "available",
    "operatingSystem": "Windows",
    "osFamily": "Windows",
    "operatingSystemVersion": "Windows Server 2022",
    "architecture": "x64",
    "runtimeVersion": "4.0.30319.42000",
    "deviceName": "GAME-SERVER-01",
    "deviceModel": "PowerEdge R750",
    "deviceType": "server",
    "deviceId": "7dp_device_example",
    "currentSystemUser": "GAME-SERVER-01\\7dtd",
    "cpuModel": "Intel Xeon",
    "cpuCoreCount": 16,
    "processorFrequencyMHz": 2600,
    "systemMemoryTotalBytes": 34359738368,
    "systemMemoryUsedBytes": 17179869184,
    "systemMemoryAvailableBytes": 17179869184,
    "systemMemoryUsagePercent": 50.0,
    "processId": 1234,
    "processStartedAtUtc": "2026-07-24T02:00:00Z",
    "processUptimeSeconds": 28800,
    "hostCpuPercent": 12.4,
    "processCpuPercent": 8.7,
    "residentSetBytes": 2147483648,
    "managedHeapBytes": 268435456,
    "secondaryMemory": {
      "availability": "available",
      "kind": "virtualAddressSpace",
      "totalBytes": 140737488224256,
      "availableBytes": 140735340740608
    },
    "storage": {
      "availability": "available",
      "primaryDataVolumeId": "volume_c",
      "freeBytes": 53687091200,
      "totalBytes": 107374182400,
      "usedBytes": 53687091200,
      "volumes": [
        {
          "id": "volume_c",
          "name": "C:\\",
          "driveType": "Fixed",
          "fileSystem": "NTFS",
          "freeBytes": 53687091200,
          "totalBytes": 107374182400,
          "usedBytes": 53687091200,
          "rootPath": "C:\\"
        }
      ]
    },
    "publicNetwork": {
      "availability": "available",
      "ipv4": "203.0.113.10",
      "ipv6": null,
      "source": "configured",
      "observedAtUtc": "2026-07-24T09:55:00Z"
    }
  },
  "restart": {
    "availability": "available",
    "nextRunAtUtc": "2026-07-25T02:00:00Z",
    "policy": {
      "enabled": true,
      "scheduleExpression": "0 6 * * *",
      "timeZoneId": "Asia/Shanghai",
      "warningLeadSeconds": 300,
      "warningMessage": "Server will restart in {minutes} minutes.",
      "warningStages": [],
      "saveWorldBeforeRestart": true,
      "restartMode": "externalScript",
      "customCommandConfigured": false,
      "customCommandDisplayName": null,
      "deferScheduledRestartDuringBloodMoonWindow": true,
      "bloodMoonPreDuskProtectionHours": 2,
      "bloodMoonDeferMinutes": 30,
      "historyRetentionDays": 30
    }
  },
  "attention": [],
  "recentActivity": {
    "availability": "available",
    "items": [],
    "totalCount": 0,
    "latestObservedAtUtc": null
  },
  "capabilities": {
    "restartScriptConfigured": true,
    "shutdownAvailable": true,
    "manualRefreshAvailable": true
  }
}
```

示例地址使用文档保留网段，不代表真实部署值。`Admin` 或 `Viewer` 请求保持 `host` 的非敏感资源数据，但返回 `identityAvailability: "forbidden"`，且不返回设备标识、系统用户名、公网地址、卷根路径和网卡身份字段的实际值。示例中的重启策略是只读摘要；`scheduleExpression` 可以承载现有计划表达式，但首页不接受编辑，`restartCommand` 原文不进入响应。

### 响应字段、状态和阶段归属

- `panel.status` 只表示 7DPanel 宿主是否存活；`game.status` 表示游戏服务器状态，取值至少包括 `online`、`offline`、`starting`、`stopped` 和 `unknown`；`gameReadiness` 只表示游戏对象是否已经可以安全读取，三者不能互相替代。
- 第一阶段统一返回游戏基础信息、服务器连接地址、在线/历史玩家、FPS、游戏时间、世界会话时长、进程时长/RSS/托管堆、主机身份、物理内存、平台明确标记的第二类内存、磁盘汇总/卷明细和只读重启策略；第二阶段再提供 3 秒轻量趋势和高成本游戏对象诊断，不得因为拆分接口而丢失字段。
- 第一阶段字段命名以响应示例为准：`gameTitle`、`saveGameName`、`serverVersion`、`serverPort`、`maxOnlinePlayers`、`serverFps`、`worldName`、`gameTime`、`worldSessionUptimeSeconds`、`processUptimeSeconds`、`residentSetBytes` 和 `managedHeapBytes`。参考 DTO 的 `GameName`、`gameVersion`、`port`、`maxPlayers`、`fps`、`gameWorld`、`historyPlayers`、`uptime`、`heap` 等名称必须在 Adapter 映射处明确记录，不能由前端猜测。
- `gameTitle` 是固定产品标题；`saveGameName` 来自 `GamePrefs.GameName`；`worldName` 来自 `GamePrefs.GameWorld`。在验证出独立地图来源前不返回 `mapName`，前端也不把 `worldName` 复制成第二个“地图”值。
- `game.serverIp` 是玩家连接使用的游戏地址；`host.publicNetwork.ipv4/ipv6` 是主机公网地址。两者可以不同，也不能用一个字段替代另一个。
- `host.processId`、`processStartedAtUtc` 和 `processUptimeSeconds` 指当前 7DTD 进程（Mod 与游戏同进程）的运行信息，不代表宿主操作系统启动时间。主机系统启动时间如未来需要展示，应使用单独字段。
- `worldSessionUptimeSeconds` 来自 `Time.timeSinceLevelLoad`，表示当前 Unity 世界/场景会话；`processUptimeSeconds` 由 `Process.StartTime` 推导，表示当前 7DTD 进程。页面必须分别标注，不能统称为同一个“服务端运行时长”。
- `hostCpuPercent` 是整机 CPU，`processCpuPercent` 是 7DTD 进程 CPU；首次差值样本为 `null`。`residentSetBytes` 表示进程 RSS/工作集，`managedHeapBytes` 来自 `GC.GetTotalMemory(false)`；不再返回语义重复的 `workingSetBytes`、`managedMemoryBytes`，也不把托管堆称作 Unity Heap。
- `systemMemoryUsedBytes`、`systemMemoryAvailableBytes`、`systemMemoryTotalBytes` 和 `systemMemoryUsagePercent` 必须成组定义。`secondaryMemory.kind` 在 Windows 为 `virtualAddressSpace`，Linux 为 `swap`；只有 `kind` 相同的数据才允许比较。`storage.freeBytes/totalBytes/usedBytes` 由已纳入的卷计算，卷级 `usedBytes = totalBytes - freeBytes`，`primaryDataVolumeId` 指向当前 Mod/数据目录所在卷，全部保留原始字节值。
- `panel`、`game`、`host`、`restart` 和 `recentActivity` 都必须有独立的 `availability` 或等价状态。`attention` 和 `capabilities` 的状态不应通过空数组或缺少字段猜测。
- `recentActivity.items` 每项至少包含稳定 `id`、`type`、`messageKey`、`messageArgs`、`severity` 和 `occurredAtUtc`；`totalCount` 表示符合保留策略的总条数，`latestObservedAtUtc` 表示活动存储最近一次成功读取时间，不等同于最后一条活动发生时间。
- `attention` 每项至少包含 `code`、`severity`、`messageKey`、`messageArgs` 和 `observedAtUtc`，可选 `action` 用于指向已授权的处理入口。正常时返回空数组，异常项不能只返回一段不可本地化的原始文本。
- `restart.nextRunAtUtc` 与 `restart.policy` 在第一阶段只读展示既有计划及完整安全摘要；计划未启用或不可用时返回明确状态和原因，不伪造时间。第三阶段只负责计划执行/历史能力，配置编辑继续位于独立重启页面。

## 第一阶段后端设计

### 聚合与数据流

```text
OverviewController
  -> GetOverviewUseCase
     -> IPanelRuntimeStatus
     -> IGameOverviewQuery
     -> IHostMetricsSampler
     -> IRecentActivityQuery
     -> OverviewAttentionEvaluator
```

- `OverviewController` 只处理身份、角色、HTTP DTO 和 Problem Details 映射。
- `GetOverviewUseCase` 组合独立快照并保留各自状态，不读取 7DTD 静态对象、`Process`、文件系统或 SQLite 细节。
- `SevenDaysGameOverviewQuery` 在 SevenDays Adapter 内复制游戏字段；实时字段使用受控主线程读取并形成不可变快照，并发请求共享约 3 至 5 秒缓存。
- 在线人数复用现有在线玩家投影；最大人数和稳定服务器设置从已经加载的配置复制，不通过网页请求反复解析配置文件。
- `HostMetricsSampler` 通过当前 `Process`、`Environment`、运行时平台信息和 `DriveInfo` 生成主机快照。进程 CPU 以相邻两次 `Process.TotalProcessorTime` 和墙钟时间差计算并按逻辑处理器数量归一；整机 CPU 以 Windows `GetSystemTimes` 或 Linux `/proc/stat` 的相邻平台计数器计算。两者首次样本都返回 `null`，不能伪造 `0%`。
- `OverviewAttentionEvaluator` 是纯计算服务，根据输入生成稳定代码，例如 `game_not_ready`、`game_snapshot_stale`、`disk_space_low`、`audit_gap_detected`、`restart_script_not_configured` 和 `public_ip_unavailable`。

### 旧后端字段来源与吸收映射

旧版 `GameServerController.GetStatistics()` 和 `DeviceHelper` 只作为兼容证据与数据源线索。目标实现借鉴“从哪里取”，不复刻 Controller 在 HTTP 请求线程直接读取游戏活对象、重复扫描实体或把平台差异压成同名字段的实现。

#### 游戏侧可借鉴来源

| 新版语义 | 旧版来源 | 吸收规则 |
|---|---|---|
| `serverName`、`region`、`language`、`serverIp`、`serverPort`、`gameMode`、`difficulty` | `GamePrefs` 对应项 | 从已加载配置复制；`serverIp` 仅表示游戏监听/连接配置，不推导公网 IP |
| `saveGameName` | `GamePrefs.GameName` | 明确为存档名称；不再映射为游戏产品标题 |
| `worldName` | `GamePrefs.GameWorld` | 只形成一个世界字段；不复制为 `mapName` |
| `gameTitle` | 7DPanel 产品元数据 | 固定为受本地化管理的“7 Days to Die”；不从 `GamePrefs.GameName` 获取 |
| `serverVersion` | `Constants.cVersionInformation.LongString` | 保留原始服务端版本字符串，同时允许未来增加结构化版本字段 |
| `gameTime` | `world.GetWorldTime()` 与 `GameUtils.WorldTimeToDays/Hours/Minutes` | 在同一游戏快照中一次读取并转换 |
| 在线/历史/离线历史玩家 | `world.Players.Count` 与 `GetPersistentPlayerList().Players.Count` | 在同次快照中取得，`offlineHistoricalPlayers = max(historicalPlayers - onlinePlayers, 0)`，避免跨请求差值为负 |
| `serverFps` | `gameManager.fps.Counter` | 作为瞬时游戏 FPS；缺失不等于面板离线 |
| `worldSessionUptimeSeconds` | `Time.timeSinceLevelLoad` | 只表示当前 Unity 世界/场景会话，不表示进程启动时长 |
| `managedHeapBytes` | `GC.GetTotalMemory(false)` | 直接以字节输出；不先转 `float MB`，不命名为 Unity Heap |
| `residentSetBytes` | `SystemInformation.GetRSS.GetCurrentRSS()` | 作为当前进程 RSS 候选来源；需与 `Process.WorkingSet64` 做平台实测后选择一个规范来源 |
| 僵尸/动物数量 | 遍历 `world.Entities.list` 并按存活类型计数 | 合并为一次受控扫描，禁止 Overview 与诊断接口各自重复全量遍历 |
| 上限、Entity、Item、Chunk、Chunk GameObjects | `GamePrefs.MaxSpawned*`、`world.Entities.Count`、`EntityItem.ItemInstanceCount`、`Chunk.InstanceCount`、`GetDisplayedChunkGameObjectsCount()` | 复制到第二阶段不可变诊断快照；没有容量上限的字段只显示数量 |
| `chunkObservedEntities` | `world.m_ChunkManager.m_ObservedEntities.Count` | 第二阶段诊断字段，和 `entities`、`chunkGameObjects` 分开标注 |
| `bloodMoon.active` | `world.aiDirector.BloodMoonComponent.BloodMoonActive` | 第二阶段状态字段；对象链任一节点不可用时返回 `null` 和来源状态 |
| 旧 `maxHeap` | `GameManager.MaxMemoryConsumption` | 先以 `maxMemoryConsumption.availability: "unverified"` 表明兼容缺口；验证 v3.0.1-b4 中的单位、更新时机和限制语义后再填充 `semantic` 与 `valueBytes`，验证前不得称为最大 Unity Heap |

#### 主机侧可借鉴来源

| 新版语义 | 旧版来源 | 吸收规则 |
|---|---|---|
| 整机 CPU 累计时间 | Windows `GetSystemTimes`；Linux `/proc/stat` | 保留平台 Adapter；由服务端以相邻样本计算使用率，不把累计计数器交给浏览器计算 |
| 物理内存 | Windows `GlobalMemoryStatusEx`；Linux `/proc/meminfo` 的 `MemTotal/MemAvailable` | 统一输出字节和使用率，并防止总量为零、溢出和解析异常 |
| `secondaryMemory` | Windows `ullTotalVirtual/ullAvailVirtual`；Linux `SwapTotal/SwapFree` | 保留旧数据但显式区分 `virtualAddressSpace` 与 `swap`，不再统一叫“虚拟内存” |
| 磁盘卷 | `DriveInfo.GetDrives()` | 返回全部可读固定卷及聚合；逐卷处理未就绪、权限和文件系统异常，不能因一个卷失败丢弃全部卷 |
| 网卡身份与累计流量 | `NetworkInterface.GetAllNetworkInterfaces()`、`GetIPProperties()`、`GetIPv4Statistics()` | 过滤回环/隧道/未启用接口；保留稳定接口 ID，扩展 IPv4/IPv6，并在服务端按接口对齐计算速率 |
| 设备名称/型号/类型、OS/OS family、CPU 型号/频率、系统内存提示值 | Unity `SystemInfo` | 可作为跨平台候选值；CPU 频率与设备模型允许为空，系统内存容量以 OS 原生采样为准 |
| `runtimeVersion`、当前系统用户名、逻辑处理器数 | `Environment.Version`、`Environment.UserName`、`Environment.ProcessorCount` | 保留；Windows 用户名补充域，处理器数明确为当前进程可见逻辑处理器数量 |

### 吸收前必须完成的改造

1. 游戏对象只在受控游戏主线程读取，立即复制成不可变快照；Web Controller、序列化器和后台数据库任务不得持有 `World`、`Entity`、`GameManager` 等活对象。
2. 基础游戏快照共享 3 至 5 秒缓存；实体扫描、Chunk、血月和对象计数共享 10 至 15 秒高成本快照，并设置排队/执行超时。不能每个 HTTP 请求重新遍历实体。
3. `GameName`、`GameWorld`、`Heap`、`Uptime` 和 `MaxHeap` 先按上表修正语义，再进入产品 DTO；禁止沿用旧名称后仅靠前端文案解释。
4. 整机 CPU、进程 CPU 和网络速率都由服务端基于两个带时间戳样本计算。首次样本、时间差异常、休眠恢复和计数器重置返回 `null`，不返回伪造的零。
5. 网络按稳定接口 ID 对齐，处理接口新增、移除、重命名、累计值回绕/重置和负差值；汇总只累加同一采样窗口中的可靠接口。
6. 磁盘逐卷处理 `IsReady`、权限和读取异常，明确聚合范围，并对 `rootPath` 做 `Owner` 裁剪；容量统一以字节传输。
7. Windows `ull*Virtual` 与 Linux Swap 通过 `secondaryMemory.kind` 分开建模；不得生成可跨平台比较的统一“虚拟内存使用率”。
8. `GamePrefs.ServerIP` 不能当作公网 IP；公网 IPv4/IPv6 继续由配置优先、显式检测和缓存边界负责。
9. 不使用 Unity `deviceUniqueIdentifier` 作为新版设备 ID；使用 OS 稳定标识派生 SHA-256 不透明值，且只向 `Owner` 返回。
10. 旧版 MB `float` 字段改为整数字节，时间统一为 UTC 或明确秒数；转换和本地化只在前端显示层进行。
11. 网卡 ID、MAC、绑定 IP、描述、卷根路径、设备 ID、系统用户名和公网地址只向 `Owner` 返回；其他角色保留聚合容量/速率及结构化裁剪状态。
12. 旧 `restartCommand` 仅吸收“是否配置”和安全显示名；脚本路径、命令原文、参数和环境变量不得进入 Overview，也不得由首页提交。任意控制台命令入口不属于字段吸收范围。

### 主机身份

- Windows 当前系统用户名由 `Environment.UserDomainName` 与 `Environment.UserName` 组合；Linux 使用 `Environment.UserName`。该值表示运行 7DTD 的操作系统身份，不表示当前面板用户。
- Windows 设备标识来源为系统 `MachineGuid`，Linux 来源为 `/etc/machine-id`，必要时回退 `/var/lib/dbus/machine-id`。
- API 不返回原始操作系统标识。后端以产品命名空间和 SHA-256 生成稳定、不透明的 `7dp_device_...`；相同系统镜像可能产生相同值，因此它只用于显示和运维关联，不用于认证、授权或秘密材料。
- CPU 型号、可空频率、设备型号、设备类型、OS family 和运行时版本由平台 Adapter 获取；无法可靠取得时保持字段为 `null`，并保留分区可用状态。`processorFrequencyMHz` 只用于诊断展示，不参与容量、健康或性能下降判断。

### 公网 IP

公网地址按以下顺序解析：

1. `config.json` 明确配置的 IPv4/IPv6，返回 `source: "configured"`。
2. 服主显式启用自动检测后，访问配置好的 HTTPS 检测地址，返回 `source: "detected"`。
3. 检测失败时保留 `availability: "unavailable"`、空地址和真实观察时间，不以本机网卡地址推断公网地址。

自动检测使用 2 至 3 秒总超时、严格 IPv4/IPv6 解析和 10 至 30 分钟成功缓存。检测发生在独立采样边界，不阻塞其他概览分区。产品不把第三方公网检测设为核心能力的强依赖；未启用检测且未配置地址时明确报告不可用。

公网地址字段结构第一阶段始终存在，但地址值允许不可用：当配置或检测成功时，`ipv4` 与 `ipv6` 至少一个非空并返回 `availability: "available"`；两者都为空时必须返回 `availability: "unavailable"`、`reason` 和最近一次真实观察时间，不能用本机内网地址或任意默认值填充。非 `Owner` 请求不返回地址值，只返回 `availability: "forbidden"`。

### 磁盘统计范围

磁盘响应返回全部可读取的本地固定卷，并标记包含 Mod 数据目录或显式数据根目录的 `primaryDataVolumeId`。`storage.volumes[]` 保留卷名、类型、文件系统、总量、可用量、已用量和 Owner-only 根路径；顶部容量卡使用全部纳入卷的聚合值，主卷单独醒目标注。排除可移动卷、网络映射卷和不应计入的 Overlay；卷未就绪、权限不足或采样失败时在该卷或采样分区返回独立状态，不能因一个卷失败丢弃其他卷，也不能用其他卷数据伪装主卷成功。

### 近期活动

第一阶段新增固定用途的 SQLite `recent_activity`，最多向 Overview 返回 8 条，并按保留策略限制总行数。批准活动类型包括：

- 面板登录成功；
- 玩家进入与离开；
- 玩家管理操作安全摘要；
- 控制台命令安全摘要；
- 重启脚本启动或启动失败。

活动不保存密码、Token、API Key、公网检测响应、玩家 IP、完整控制台参数/输出、脚本路径或命令行。它不是通用 Event Bus，不允许运行时注册任意活动类型。

### 重启脚本配置与执行

`config.json` 增加平台脚本配置，路径相对于 Mod 目录解析并规范化：

```json
{
  "restart": {
    "windowsScript": "scripts/restart-server.cmd",
    "linuxScript": "scripts/restart-server.sh",
    "workingDirectory": "."
  }
}
```

配置缺失、路径无效或平台脚本不存在不阻止面板启动，只让 `restartScriptConfigured=false`。浏览器请求体只接受精确 `{ "confirmed": true }`，不能传入路径、参数或环境变量。

```text
ServerOperationsController
  -> RestartServerUseCase
     -> IServerOperationAuditTrail records intent
     -> IRestartScriptLauncher
        -> Process.Start(cmd.exe or /bin/sh)
     -> audit launch result
     -> 202 Accepted
```

- Windows 显式启动 `cmd.exe /d /s /c` 与配置脚本；Linux 显式启动 `/bin/sh` 与配置脚本。
- 不重定向标准输入、输出或错误，不等待脚本退出；脚本自行管理日志。
- `Process.Start` 返回非空 `Process` 后立即释放本地进程句柄并返回 `202 Accepted`。
- 响应只包含 `operationId`、`status: "restart_script_started"` 和 `startedAtUtc`，不包含最终重启状态。
- 同一瞬间的并发请求使用 single-flight，避免同时创建多份脚本进程；前端收到 202 后锁定当前页面操作。
- 稳定错误码包括 `restart_confirmation_required`、`restart_script_not_configured`、`restart_script_not_found`、`restart_script_start_failed` 和 `audit_unavailable`。
- 审计只记录固定操作类型、操作者、请求时间、`Pending/Started/Failed` 和稳定失败码。脚本已创建但审计终态更新失败时保留 `Pending` 并写服务端告警；不把未知审计终态解释为重启成功。

`POST /api/v1/server/shutdown` 只接受同样的确认结构 `{ "confirmed": true }`，服务端固定调用既有主线程关服能力或固定 `shutdown` 白名单，不接受浏览器命令文本。它拥有独立的 `ShutdownServerUseCase`、权限检查、审计记录和结果状态；页面只能根据其明确的 `Accepted/Completed/Failed/Unknown` 结果显示，不复用 `restart_script_started` 文案。

## 第二阶段后端设计

### 轻量实时指标

`GET /api/v1/metrics/live` 返回当前采样，不返回历史：

- 7DTD 进程 CPU；
- RSS；
- 托管堆；
- 系统内存已用/总量；
- FPS；
- 在线玩家数量；
- `sampledAtUtc` 与各来源状态。

浏览器约每 3 秒请求，服务端使用单例采样器和 2 至 3 秒缓存，多个标签页不重复执行同一窗口内的昂贵采样。`processCpuPercent` 明确表示当前 7DTD 进程，`managedHeapBytes` 明确表示 `GC.GetTotalMemory(false)` 的托管堆，不把它描述为整机内存或 Unity 原生内存。

### 高成本游戏诊断

`GET /api/v1/diagnostics/game` 返回 `offlineHistoricalPlayers`、`bloodMoon.active`、僵尸/上限、动物/上限、Chunk、Chunk GameObjects、Chunk Observed Entities、Entity 和 Item 当前统计。旧 `maxHeap` 不再作为数值字段返回；改用 `maxMemoryConsumption` 诊断信封，在语义验证前返回 `availability: "unverified"`、`semantic: null`、`valueBytes: null`，验证通过后才填充经批准的语义和值。所有字段在游戏主线程复制，使用 10 至 15 秒缓存、排队/执行超时和独立可用状态。单项无法取得时返回 `null`，不让整个接口失败。

每个字段在实施前必须从只读 `7dtd-reference/v3.0.1-b4` 验证真实来源、线程要求、单位和生命周期，并通过真实进程采样耗时证明不会明显破坏帧预算。FPS 健康等级在 Windows/Linux 典型负载基线建立后另行批准，当前草案不固化阈值。

## 第三阶段后端设计

### 网络流量

`INetworkMetricsSampler` 使用 Windows/Linux 平台 Adapter 统计主机非回环网络接口，而不是宣称获得单个 7DTD 进程流量。采样器保存累计收发字节和采样时间，通过差值计算每秒速度，并处理接口变化、计数器重置、休眠和异常时间间隔。无可靠样本时返回 `null`。

网络响应包含聚合层和接口层：聚合层至少固定为 `sampledAtUtc`、`inboundBytesPerSecond`、`outboundBytesPerSecond`、`totalBytesReceived`、`totalBytesSent` 和 `availability`；`interfaces[]` 包含 `id`、`macAddress`、`name`、`description`、`type`、`linkSpeedBitsPerSecond`、`ipAddresses`、`bytesReceived`、`bytesSent`、接口级速率和独立状态。速率以字节/秒传输，前端负责按 `b/Kb/Mb/Gb` 和当前语言格式化。接口层在首页 `Monitor` 的网络图下以明细形式展示，但接口 ID、MAC、绑定 IP 和描述只对 `Owner` 返回。

### 指标历史

后台采样器每 10 至 30 秒向 SQLite `metric_samples` 写入 CPU、RSS、系统内存、磁盘、网络速度、FPS 和在线人数。设置固定保留期和删除策略，不保存高成本 Chunk/Entity 明细历史。`GET /api/v1/metrics/history` 限制时间范围、分辨率和最大点数；写入或清理失败不影响游戏运行和当前指标读取，只生成可观察告警。

历史查询至少定义 `fromUtc`、`toUtc`、`resolution` 和 `maxPoints`，服务端强制最大时间范围、最小分辨率和最大点数；响应保留每点的 `sampledAtUtc`、指标值和数据状态。查询失败、数据缺口和保留期外范围必须分别返回可识别状态，不能补造连续曲线。

### 自动重启计划

第三阶段优先复用现有重启设置的计划解析、时区、分级警告、保存世界、血月延迟和历史保留逻辑，但执行终点改为第一阶段同一个 `IRestartScriptLauncher`。综合首页只读展示完整安全摘要，配置编辑继续由独立重启页面负责。旧 `restartCommand` 原文不进入 Overview，旧 `RestartRunRequestDto` 的临时模式/警告覆盖也不进入脚本启动接口。到期后只记录脚本进程是否成功创建并计算下一次计划时间，不保证服务器最终重启成功。

## Admin 概览设计

### 页面目标与信息顺序

用户进入 `/` 后应能快速回答：

1. 面板、游戏和数据采样是否正常；
2. 当前负载、玩家和世界时间是否异常；
3. 运行在哪台设备、哪个系统身份与哪个公网地址；
4. 当前有哪些需要处理的问题；
5. 最近发生了什么；
6. 是否需要由 Owner 立即启动重启脚本。

页面按以下内容层级组织，但本草案不固定像素布局或视觉稿：

- 顶部当前状态：面板/游戏状态、在线人数、FPS、血月状态、世界会话时长、7DTD 进程时长和游戏时间。
- 服务器信息（对应旧 `Overview`）：服务器名称、游戏标题、存档名称、版本、世界、模式、难度、地区、语言、连接 IP/端口、人数上限和采样时间；没有独立来源时不显示重复的地图字段。
- 主机平台（对应旧 `SystemInfo`）：OS/OS family/版本/架构、运行时版本、CPU 型号/逻辑核心/可空频率、设备信息、稳定设备 ID、当前系统用户名、公网 IPv4/IPv6、进程 ID 和启动时间；Owner 可展开网卡身份和主卷根路径。
- 资源容量（对应旧 `Status`）：玩家在线/空闲/历史/离线历史、僵尸、动物、物理内存、平台明确标记的虚拟地址空间或 Swap、磁盘汇总与全部卷明细；同时展示 RSS、托管堆、Chunk、Chunk GameObjects、Chunk Observed Entities、Entity 和 Item，不为无上限计数构造百分比。
- 实时监控（对应旧 `Monitor`）：整机/进程 CPU、RAM、FPS、在线人数和网络聚合趋势；第三阶段在网络图下展示每个接口的类型、链路、绑定地址和累计/实时流量。
- 需要处理：按严重、警告、提示排序；正常时显示紧凑空状态。
- 近期活动：最多 8 条安全摘要、相对时间、精确时间和最近更新时间。
- 快捷操作（对应旧 `QuickActions`）：重新采样、Owner-only 重启脚本和固定关闭服务器操作，并提供自动重启策略的完整只读详情；编辑重启计划仍跳转独立路由。玩家列表、地图和 API Keys 继续通过既有独立路由访问，不在概览内展开。

### 页面状态

- `Loading`：首次概览请求尚未完成，分区使用骨架，不伪造零值。
- `Fresh`：快照成功且处于批准的新鲜窗口。
- `Partial`：总体请求成功，但至少一个分区 `unavailable` 或 `forbidden`；其他分区正常展示。
- `Stale`：保留最后成功数据、原采样时间和过期标记。
- `Offline`：没有可用快照或 API 不可达；不能把缺失 FPS 单独解释为离线。
- `RestartScriptStarted`：只提示“重启脚本已启动，服务器连接可能中断”，不显示“重启成功”。

稳定后端代码由 `vue-i18n` 映射中文与英文；原始异常文本不进入页面。设备 ID、公网 IP、当前系统用户名、卷根路径以及网卡 ID/MAC/绑定 IP/描述只在 `Owner` 响应中显示，并提供不写入 URL、日志、Storage 或全局状态的临时复制反馈。

### 刷新策略

- 综合概览约每 30 秒刷新；页面重新可见时立即采样一次。
- 第二阶段轻量指标约每 3 秒刷新，浏览器只保存最近 10 个点，约覆盖 30 秒。
- 高成本游戏诊断约每 10 至 15 秒刷新。
- 公网 IP 成功样本由服务端缓存 10 至 30 分钟，前端不单独高频请求。
- 页面隐藏、路由离开或组件卸载时暂停/取消请求；恢复时重新采样。
- 自动刷新之外保留手动“重新采样”；请求失败保留最后成功值及其时间。

综合概览由原参考实现的全量约 3 秒刷新调整为分层刷新，是本草案明确的行为变化：基础身份和低成本汇总约 30 秒，轻量趋势约 3 秒，高成本游戏诊断约 10 至 15 秒。实现和验收必须分别验证三种刷新周期，不能将任一周期误称为整页实时。

### Vue 组件与状态边界

路由页保持组合表面。旧参考项目入口是 `src/views/Dashboard/index.vue`；本仓库目标 Admin 仍使用 `frontend/apps/admin/src/pages/index.vue`，只吸收旧页面的信息职责：

```text
frontend/apps/admin/src/pages/index.vue
  -> Dashboard（目标中的组合表面，可沿用现有组件名称）
     -> OverviewStatusSummary
     -> ServerInformationPanel
     -> HostPlatformPanel
     -> ResourceCapacityPanel
     -> LiveMetricsPanel
     -> NetworkInterfaceDetailsPanel
     -> RestartPolicySummary
     -> AttentionPanel
     -> RecentActivityPanel
     -> QuickActionsPanel
     -> RestartServerDialog
```

- `Dashboard` 只编排分区和页面级刷新，不包含每个分区的格式化细节；旧参考 `Dashboard/index.vue` 的字段格式化和请求副作用不能原样复制，应迁移到目标局部 composable。
- `useOverview()` 拥有综合快照、刷新、取消、部分失败、过期和最后成功数据。
- `useLiveMetrics()` 在第二阶段拥有 3 秒采样和当前标签页最近 10 个点。
- `useGameDiagnostics()` 拥有高成本诊断刷新与独立过期状态。
- `HostPlatformPanel` 承担 OS、运行时、设备与进程身份，`ResourceCapacityPanel` 承担物理/第二类内存和卷级容量，`NetworkInterfaceDetailsPanel` 承担第三阶段 Owner-only 接口身份与流量；这些详情不得挤入顶部状态卡。
- `RestartPolicySummary` 只展示安全摘要并跳转独立配置页，不持有配置表单状态，也不显示 `restartCommand` 原文。
- `useRestartServer()` 拥有确认、提交锁、稳定错误映射和 `restart_script_started` 反馈。
- `useShutdownServer()` 拥有独立确认、提交锁、固定关服结果映射和审计关联；不能复用重启脚本的成功文案。
- `usePageVisibilityRefresh()` 统一页面可见性控制，避免各组件重复安装监听器。
- 这些数据属于路由局部服务器状态，不新增全局 Pinia Store；认证和角色仍由现有 Auth Store 拥有。
- 各展示组件使用显式、类型化 props，危险操作通过显式事件上抛；展示组件不直接发请求或读写 Storage。

## 错误、安全与诚实状态

- 匿名请求返回 401，角色不足返回 403；主机身份字段对非 Owner 返回分区级 `forbidden`，不通过前端隐藏替代服务端授权。
- 公网检测失败、CPU 首次无样本、游戏未就绪、SQLite 摘要失败和高成本统计超时分别保留独立状态，不能互相替代。
- 所有大小以字节传输，前端自适应显示 MB、GB 或 TB；时间使用 UTC/明确单位，前端按当前语言格式化。
- 没有明确容量上限的 Chunk、Entity 和 Item 只显示当前数值，不渲染虚构百分比。
- `Process.Start` 只接收服务端已验证的固定配置，不组合任何浏览器输入。
- 重启脚本创建成功不等于脚本完成、进程退出、新服务器启动或健康检查通过。
- 设备唯一标识不是秘密，但属于 Owner-only 运维信息；原始 OS 标识不进入 API。

## 验证思路

### 第一阶段

- Application 单元测试覆盖聚合、部分数据失败、Owner 字段裁剪、注意事项、时间戳和重启确认。
- Host Adapter 测试覆盖 CPU 首样本/差值、Windows 虚拟地址空间与 Linux Swap 的不同 `kind`、全部固定卷/主数据卷/单卷失败、OS family、运行时版本、可空 CPU 频率、Windows/Linux 用户名、设备 ID 摘要和无效平台数据。
- 公网检测测试覆盖配置优先、IPv4/IPv6 验证、超时、缓存和不可用状态，不访问真实第三方服务。
- SevenDays 测试覆盖 `GameName -> saveGameName`、`GameWorld -> worldName`、`Heap -> managedHeapBytes`、`Uptime -> worldSessionUptimeSeconds`、游戏未就绪、主线程快照、缓存共享、字段可空和超时，并证明不生成重复 `mapName` 或虚假 Unity Heap。
- SQLite 测试覆盖近期活动保留与服务器操作审计。
- Katana 测试覆盖 Overview 权限矩阵、部分成功、Owner 202、非 Owner 403、未确认、缺失脚本、启动异常、固定关服能力和任意命令输入拒绝。
- Admin 单元/组件测试覆盖 Loading/Fresh/Partial/Stale/Offline、Owner 敏感字段、全部卷/网卡/重启策略摘要、不同第二类内存标签、两个运行时长标签、复制反馈、自动刷新暂停、重启文案和关闭文案；当前参考前端缺少完整 Dashboard 专项测试，不能把参考页面现状当作测试证据。
- Windows/Linux 使用无副作用测试脚本写临时标记文件，验证 API 确实创建脚本；不要求真实重启 7DTD。

### 第二阶段

- 验证多个客户端共享服务端采样缓存，浏览器只保留当前标签页 10 个点。
- 验证离线历史玩家派生、血月、Chunk Observed Entities 等高成本字段的主线程来源、超时和真实进程采样耗时；在暴露 `GameManager.MaxMemoryConsumption` 前单独验证其单位、生命周期和真实语义。
- 在批准 FPS 阈值前保存 Windows/Linux 空载与典型负载基线。

### 第三阶段

- Windows/Linux 分别验证网络接口身份裁剪、IPv4/IPv6、接口变化、计数器重置、负差值、聚合和单位换算。
- SQLite 验证采样保留、删除、并发读取、受限范围与最大点数。
- 自动重启计划验证时区、夏令时、重复触发、禁用和跨进程重新加载；仍只断言脚本启动结果。

## 与现有产品文档的范围对齐

当前 `docs/design.md` 的概览定义还包括最近备份、失败任务、即时公告入口和手动备份快捷入口。本草案聚焦服务器运行总览、主机身份、近期活动、重启和关服，不重新定义备份、公告或任务状态的业务合同；在正式更新 `docs/PRD.md` 和 `docs/design.md` 前，必须明确以下二选一：

- 第一阶段首页同时接入这些现有模块的只读摘要和快捷入口；或
- 保持本草案的范围，将备份、公告和任务入口作为独立页面导航目标，并在概览中保留异常跳转/状态摘要，不把它们误写成已实现能力。

在选择完成前，不能用本 spec 覆盖现有产品文档对概览页面的要求。

## 依赖安装决策

### 必需新增依赖

当前没有必需新增的前端或后端包。三个阶段的目标能力可以复用现有依赖与平台能力：

- Admin 复用现有 `vue`、`@nuxt/ui`、`@vueuse/core`、`pinia`、`vue-i18n`、`valibot`、`vue-router`、`vitest`、`@vue/test-utils` 和 `@playwright/test`。3 秒趋势和环形容量图优先使用 SVG/CSS 与已有 Vue 响应式能力，不安装图表库。
- 页面刷新、取消请求和可见性监听使用原生 `AbortController`、Fetch 以及现有 `@vueuse/core`；不新增查询缓存库或持久化 Store 插件。
- 后端复用 .NET Framework `net48` BCL 的 `Process`、`Environment`、`DriveInfo`、`NetworkInterface`、`HttpClient`、`TimeZoneInfo` 和平台 Adapter；不新增跨平台系统监控包。
- 近期活动、指标历史和审计复用现有 `Microsoft.Data.Sqlite`、`SQLitePCLRaw.bundle_e_sqlite3`、`Dapper` 和 `DbUp`；不重复引入 SQLite provider 或 ORM。
- 现有 Web API、Katana OWIN、依赖注入和 JSON 参考程序集继续复用，不为 Overview 单独引入 Web 框架。

### 仅在真实需求出现时评估的候选包

以下不是本次安装清单，必须先有真实消费者、评估版本/许可证/包体积和测试门禁后再决定：

- `@unovis/vue` 与 `@unovis/ts`：只有原生 SVG/CSS 无法满足可访问性、Tooltip、坐标轴或趋势交互时才引入，且两个包配套安装。
- `@hey-api/openapi-ts`：只有后端 OpenAPI 契约稳定并需要生成客户端时，作为 `devDependency` 引入；在此之前使用手写类型化 API 映射。
- `@pinia/colada`：只有至少两个真实查询消费者需要服务端查询缓存、去重、失效和 Mutation 管理时才评估，当前不使用。
- `event-source-plus` 或 `@microsoft/fetch-event-source`：SSE 的 Header Bearer、取消和重连策略优先先用现有 Fetch 型边界实现；确有复杂度后再选其一，不能同时安装。

因此，本阶段不执行 `pnpm add` 或新增 `<PackageReference>`；如果后续选择候选包，必须同时更新对应的 `package.json`/锁文件或 `.csproj`，并补充安装理由与验证结果。

## 大概目录结构

目录只在对应纵向切片真正实现时创建，不提前生成空 Feature。以下结构沿用现有六项目后端边界和 Admin 目标蓝图；当前仍是目标草图，不代表文件已经存在。

```text
7dpanel/
|-- backend/
|   |-- src/
|   |   |-- Core/LSTY.SevenDPanel.Application/
|   |   |   |-- Overview/
|   |   |   |   |-- GetOverviewUseCase.cs
|   |   |   |   |-- Models/OverviewSnapshot.cs
|   |   |   |   |-- Models/OverviewAttention.cs
|   |   |   |   |-- Models/RecentActivityItem.cs
|   |   |   |   |-- Models/GameOverviewSnapshot.cs
|   |   |   |   |-- Models/HostOverviewSnapshot.cs
|   |   |   |   |-- Models/HostStorageVolume.cs
|   |   |   |   |-- Models/RestartPolicySummary.cs
|   |   |   |   |-- Ports/IGameOverviewQuery.cs
|   |   |   |   |-- Ports/IHostMetricsSampler.cs
|   |   |   |   `-- Ports/IRecentActivityQuery.cs
|   |   |   |-- ServerOperations/
|   |   |   |   |-- RestartServerUseCase.cs
|   |   |   |   |-- ShutdownServerUseCase.cs
|   |   |   |   |-- Models/ServerOperationResult.cs
|   |   |   |   `-- Ports/IRestartScriptLauncher.cs
|   |   |   |-- Metrics/
|   |   |   |   |-- LiveMetricsSnapshot.cs
|   |   |   |   `-- Diagnostics/GameDiagnosticsSnapshot.cs
|   |   |   `-- ...
|   |   |-- Adapters/LSTY.SevenDPanel.Adapters.Web/
|   |   |   |-- Inbound/Http/OverviewController.cs
|   |   |   |-- Inbound/Http/ServerOperationsController.cs
|   |   |   `-- Inbound/Http/OverviewHttpModels.cs
|   |   |-- Adapters/LSTY.SevenDPanel.Adapters.SevenDays/
|   |   |   `-- Outbound/Overview/SevenDaysGameOverviewQuery.cs
|   |   |-- Runtime/LSTY.SevenDPanel.Hosting/
|   |   |   `-- Platform/
|   |   |       |-- HostMetricsSampler.cs
|   |   |       |-- HostStorageSampler.cs
|   |   |       |-- HostNetworkSampler.cs
|   |   |       |-- PublicNetworkAddressResolver.cs
|   |   |       |-- WindowsHostMetricsAdapter.cs
|   |   |       `-- LinuxHostMetricsAdapter.cs
|   |   |-- Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/
|   |   |   |-- SqliteRecentActivityStore.cs
|   |   |   |-- SqliteMetricHistoryStore.cs
|   |   |   `-- Migrations/
|   |   |       |-- 004_RecentActivity.sql
|   |   |       `-- 005_MetricSamples.sql
|   |   |-- Bootstrap/LSTY.SevenDPanel/
|   |   |   `-- Configuration/PanelHostConfig.cs
|   |   `-- ...
|   |-- tests/LSTY.SevenDPanel.Tests/
|   |   |-- Overview/
|   |   |-- HostMetrics/
|   |   |-- ServerOperations/
|   |   `-- Persistence/
|   `-- scripts/
|       `-- README.md
|-- frontend/apps/admin/
|   |-- package.json
|   |-- pnpm-lock.yaml
|   |-- src/
|   |   |-- pages/index.vue                         # 路由组合表面
|   |   |-- features/server-status/
|   |   |   |-- api/overview.ts
|   |   |   |-- model/overview.ts
|   |   |   |-- composables/useOverview.ts
|   |   |   |-- composables/useLiveMetrics.ts
|   |   |   |-- composables/useGameDiagnostics.ts
|   |   |   |-- composables/usePageVisibilityRefresh.ts
|   |   |   `-- ui/
|   |   |       |-- OverviewStatusSummary.vue
|   |   |       |-- ServerInformationPanel.vue
|   |   |       |-- HostPlatformPanel.vue
|   |   |       |-- ResourceCapacityPanel.vue
|   |   |       |-- LiveMetricsPanel.vue
|   |   |       |-- StorageVolumeDetails.vue
|   |   |       |-- NetworkInterfaceDetailsPanel.vue
|   |   |       |-- AttentionPanel.vue
|   |   |       `-- RecentActivityPanel.vue
|   |   |-- features/server-operations/
|   |   |   |-- api/serverOperations.ts
|   |   |   |-- composables/useRestartServer.ts
|   |   |   |-- ui/RestartPolicySummary.vue
|   |   |   |-- ui/RestartServerDialog.vue
|   |   |   `-- ui/ShutdownServerDialog.vue
|   |   |-- shared/api/http.ts
|   |   |-- shared/time/
|   |   |-- shared/ui/
|   |   `-- app/i18n/
|   `-- tests/
|       |-- unit/
|       |-- component/
|       `-- e2e/
`-- <ModDirectory>/
    `-- scripts/
        |-- restart-server.cmd                   # Windows 由服主部署
        `-- restart-server.sh                    # Linux 由服主部署
```

平台采样初期先放在现有 Hosting 的明确 `Platform` 边界内；只有 Windows/Linux 实现、依赖或测试形成独立项目边界时，才拆出新的 Platform Adapter 项目。前端 `pages/index.vue` 是本仓库当前目标入口；外部参考项目的 `src/views/Dashboard/index.vue` 仅作为字段和模块对照路径，不应机械复制。

## 文档影响与后续条件

- 本草案扩展了 `CAP-01` 可观察状态、加入 Owner 主机身份和公网信息，并新增手动脚本启动行为。正式批准实施前先更新[产品需求](../../PRD.md)，明确这些用户结果、权限和“不保证重启成功”的验收合同。
- 页面信息顺序、部分状态、敏感字段和确认交互由[界面设计](../../design.md)拥有；本草案批准后再把稳定产品交互写入 Current 设计。
- 项目、端口、采样、缓存、平台来源、SQLite 和脚本边界由[系统架构](../../architecture.md)拥有；只有实现并验证后才能提升为 Current 事实。
- 第二、第三阶段会改变测试环境、真实进程和跨平台门禁，必须同步评估[测试策略](../../test.md)。
- 脚本配置、安装权限和实际运维步骤应由最接近脚本的 README 或未来操作文档拥有，不在本草案复制机器命令。
- 不更新 `CHANGELOG.md`，因为这里没有已发布行为。
- 当前不创建实施计划。用户审阅并批准本草案后，再决定是先为第一阶段创建独立主规格，还是把本文收敛为只覆盖第一阶段的可实施规格。

## 草案审阅检查点

审阅本文时重点确认：

- 三阶段范围和顺序是否保持独立、可交付；
- 第一阶段是否必须包含系统用户名、稳定设备 ID 和公网 IPv4/IPv6；
- 设备 ID 是否接受稳定 SHA-256 不透明值而不暴露原始 OS 标识；
- Overview 读取权限与 Owner-only 主机身份裁剪是否符合预期；
- 公网地址的配置优先、显式检测和缓存边界是否符合部署要求；
- 公网字段是否结构必有但允许地址不可用，磁盘是否返回全部固定卷并明确标记主数据卷；
- `game.status`、`gameTitle/saveGameName/worldName`、世界会话/进程时长、RSS/托管堆/CPU、Windows 虚拟地址空间/Linux Swap 和近期活动响应结构是否无歧义；
- 离线历史玩家、血月、Chunk Observed Entities、OS family、运行时版本、CPU 频率、卷明细、网卡明细和完整重启策略摘要是否都有首页位置与权限裁剪；
- 关闭操作是否使用固定服务端边界、独立权限/审计和结果文案；
- `docs/design.md` 中备份、失败任务、公告和手动备份入口是纳入本页还是保留独立导航；
- 前后端是否确认无需新增依赖，候选包是否没有提前写入安装清单；
- 重启操作是否只承诺 `Process.Start` 创建脚本进程并立即返回 202；
- 前端是否保持局部 composable、诚实状态、分层刷新和无视觉稿约束；
- 进入实施前是否先更新 PRD，并按阶段决定规格拆分。
