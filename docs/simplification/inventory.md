---
status: Active
last_updated: "2026-08-19"
---

# 复杂度盘点

## 目的

本文件记录简化候选的调查证据和处置结论。候选不等于缺陷，也不自动授权删除或合并；只有完成调用链、边界、风险和验证调查后，才能进入实施。

简化原则、风险等级和标准实现路径见[简化工作章程](README.md)，执行顺序见[六阶段路线图](roadmap.md)。当前架构事实和验证状态分别由[系统架构](../architecture.md)与[测试策略](../test.md)拥有。

## 盘点规则

1. 先记录真实调用路径，再评价层数；
2. 区分本质复杂性和偶然复杂性；
3. 区分 A、B、C 风险等级，不用同一标准处理所有能力；
4. 接口数量、文件数量和项目数量只作为线索，不作为删除理由；
5. 测试替身本身不构成生产接口理由；
6. 不以目标蓝图、设计规格或历史测试结果作为当前实现证据；
7. 每个实施候选必须有受影响边界、验证方式和回滚方式；
8. 发现产品、架构或测试事实变化时，更新对应权威文档；
9. 一轮最多实施三个低耦合候选，验证稳定后再扩大范围；
10. 不能证明安全简化的候选保持 `暂缓`，不为追求进度强行处理。

## 状态定义

候选只使用以下状态：

- `待调查`：只有线索，尚未核实完整路径；
- `调查中`：正在收集调用、边界、测试和运行时证据；
- `已确认`：证据足以支持明确处置；
- `实施中`：已批准并正在进行可回滚变更；
- `已完成`：变更和适用验证已完成；
- `保留`：复杂性有明确生产价值；
- `暂缓`：价值或影响不明确，当前不处理；
- `撤销`：候选假设被证据否定。

这些状态只表示简化候选进度，不得替代[测试策略](../test.md)中的能力成熟度和门禁状态。

## 三个追踪样本

阶段二必须先完成三个代表性样本。样本用于比较设计强度是否与风险相称，不要求立即修改对应能力。

### 样本一：简单只读字段

- 风险等级：C
- 选择标准：现有稳定查询中新增或展示一个不敏感、无副作用字段；
- 选定能力：`CAP-01` 综合概览中的现有游戏日时指标；本样本只追踪现有字段，不新增产品字段；
- 需要记录：
  - 从数据来源到页面经过的工程、文件和类型；
  - DTO、parser、mapping 和生成类型数量；
  - DI、OpenAPI、测试和文档触点；
  - 哪些层拥有真实转换、权限或未知值语义；
  - 哪些层只传递相同值。
- 状态：`调查中`

### 样本二：普通配置修改

- 风险等级：B
- 选择标准：可校验、可回退、不启动任意脚本的现有配置；
- 选定能力：`CAP-07` 彩色聊天设置更新；本样本追踪 SQLite 持久化、运行时快照和 Admin Mutation，不改变聊天功能范围；
- 需要记录：
  - 读取、校验、版本冲突、持久化和运行时应用路径；
  - 是否存在重复 settings、DTO 或 parser；
  - 是否确实需要审计、事务和运行时快照；
  - Controller、Use Case、Store、Adapter 每层的独立责任；
  - 前端导航、表单和反馈状态。
- 状态：`调查中`

### 样本三：危险异步操作

- 风险等级：A
- 选择标准：已有持久作业、恢复或结果未知语义的操作；
- 选定能力：`CAP-03` 备份恢复与回滚；本样本追踪持久作业、Local Adapter、恢复 marker、安全副本、终态收据和人工回滚；
- 需要记录：
  - 授权、确认、排队、执行、审计和终态链路；
  - 游戏线程、文件系统、SQLite 或进程边界；
  - 幂等、并发、重启恢复和人工回滚；
  - HTTP 202、作业状态和真实副作用之间的区别；
  - 哪些复杂性不可删除。
- 状态：`调查中`

## 基线记录

阶段一的最新基线结果必须写入[测试策略](../test.md)的权威证据区。本节只记录盘点所需的引用，不复制通过数量、失败数量或成熟度。

| 项目 | 权威来源 | 盘点用途 | 状态 |
|---|---|---|---|
| 能力成熟度与发布阻塞 | [测试策略](../test.md#能力成熟度唯一台账) | 确定核心验证优先级 | 已复验 |
| 复杂性预算与趋势 | [测试策略](../test.md#复杂性预算与趋势) | 发现增长热点，不直接决定删除 | 已复验；当前通过 |
| 后端聚合构建与测试 | [测试策略](../test.md) | 建立重构前回归基线 | 已复验 |
| Admin 静态、单元与构建 | [测试策略](../test.md) | 建立前端回归基线 | 已复验；当前通过 |
| OpenAPI 漂移 | [测试策略](../test.md) | 确保传输合同未被简化破坏 | 已复验 |
| 发布物与真实边界 | [测试策略](../test.md) | 区分本地代码证据和发布证据 | 环境阻塞已确认 |

## 初始候选假设

下面只记录从当前架构规模产生的调查方向，状态均为 `待调查`，不得直接据此修改代码。

| ID | 候选方向 | 风险 | 需要回答的问题 | 状态 |
|---|---|---:|---|---|
| SIM-001 | 一对一公共接口 | 混合 | 是否跨外部边界、保护依赖方向或存在第二生产消费者？ | 待调查 |
| SIM-002 | 纯转发 Use Case、Service、Gateway 或 Store | 混合 | 是否拥有校验、授权、事务、状态转换或错误映射？ | 待调查 |
| SIM-003 | 重复 DTO、parser 和 mapping | B/C | 是否表达不同信任边界，还是重复同一合同？ | 待调查 |
| SIM-004 | 简单查询进入过重流程 | C | 是否不必要地使用 Job、持久状态机或通用审计？ | 待调查 |
| SIM-005 | 重复文档事实 | C | 实现、验证或发布状态是否在唯一权威来源之外重复维护？ | 待调查 |
| SIM-006 | 未使用 Registry 和扩展点 | B/C | 是否存在非测试生产消费者或已批准近期消费者？ | 待调查 |
| SIM-007 | 默认导航中的扩展与实验能力 | B/C | 未启用或未验证能力是否增加所有用户的学习成本？ | 待调查 |
| SIM-008 | Domain 物理项目边界 | A/B | Domain 是否拥有独立规则和不变量，合并是否会损害依赖方向？ | 待调查 |
| SIM-009 | Hosting 物理项目边界 | A/B | Hosting 是否拥有独立生命周期边界，合并是否引入反向引用？ | 待调查 |
| SIM-010 | Adapter 物理拆分 | A | 是否可减少工程数量而仍机械阻止 Application 依赖基础设施？ | 待调查 |
| SIM-011 | 重复测试夹具与对象图 | B/C | 能否共享稳定测试基础而不隐藏不同能力的合同？ | 待调查 |
| SIM-012 | 多份配置和权限声明 | B | 是否存在导航、路由、Controller 和模块状态之间的手工重复事实？ | 待调查 |

## 已确认候选

### SIM-001：Steam 持久玩家身份查询端口

- 状态：`保留`
- 类型：接口
- 主要能力：Players
- 风险等级：A
- 当前实现锚点：`backend/src/Core/LSTY.SevenDPanel.Application/Players/PlayerWebIdentity.cs` 中的 `IPlayerPersistentIdentityLookup`
- 当前调用路径：Steam OpenID callback → `PlayerAuthenticationService` → `IPlayerPersistentIdentityLookup` → `SevenDaysPersistentPlayerIdentityLookup` → `GameThreadDispatcher` → `persistentPlayers`
- 生产消费者：Web 认证服务；生产实现：SevenDays Adapter；测试使用 Stub。
- 外部边界：Web 身份验证到 7DTD `persistentPlayers` 注册表与游戏主线程。
- 当前成本：新增一个公开 Application 接口和 DI 映射。
- 保留价值：阻止 Web/Application 引用 7DTD 活对象和集合；实现只返回 `SteamId`、`PrimaryId`、`DisplayName` 三个稳定标量；SevenDays Adapter 独占 SteamID64 校验和 dispatcher timeout。
- 候选处置：保留；已在[系统架构](../architecture.md#系统边界)记录合同，并将已审查的公共接口预算基线更新为 `144`。
- 不允许退化的不变量：Web/Application 不得访问 `persistentPlayers`；查询必须在 `GameThreadDispatcher` 内执行；无匹配或歧义匹配不得创建玩家会话。
- 所需验证：复杂性预算、玩家认证聚焦测试、DI/依赖方向测试。
- 权威文档影响：[系统架构](../architecture.md)已更新；当前基线记录见[测试策略](../test.md)。
- 回滚方式：如未来移除玩家 Steam 登录切片，先删除 Web consumer、DI 映射和 SevenDays Adapter，再在同一变更中删除端口并恢复预算；不能把 Web consumer 直接连到游戏程序集。
- 结论：这是实际跨项目、跨线程和跨运行时边界的端口，不属于一对一无价值抽象。

## 三个样本调查结论

### C 级样本：CAP-01 综合概览游戏日时指标

- 状态：`已确认`
- 入口：`GET /api/v1/overview` → `OverviewController` → `GetOverviewUseCase` → `IGameOverviewQuery`。
- 游戏边界：`SevenDaysGameOverviewQuery` 将采集闭包交给 `GameThreadDispatcher.Enqueue`；就绪后在游戏线程读取 `world.worldTime`，转换为字符串，并以 `SevenDaysMetricSample<string>` 表达值与 warning。未就绪、取消、超时和 stale 由该边界拥有。
- Application：将 SevenDays sample 转为带 `source`、`unit`、`observedAtUtc`、`value/warning` 不变量的 `ObservedMetric<string>`，再放入 `GameRuntimeMetrics` 和 `GameOverviewSnapshot`；`GetOverviewUseCase` 并行聚合 game、host、activity，并隔离分区失败。
- Web：`OverviewController` 选择 Owner/non-Owner 受众；`OverviewHttpModels` 将应用模型映射为 `GameRuntimeMetricsHttpResponse` 和 `ObservedMetricHttpResponse<string>`；OpenAPI snapshot 定义 `gameDayTime` 合同。
- Admin：OpenAPI snapshot 经 `openapi-ts` 生成 types、Fetch SDK 和 Pinia Colada query；`useOverview` 使用生成 query 后调用 `overviewParser`。parser 拒绝未知字段、校验 11 个指标共享 `observedAtUtc`、检查 value/warning 配对并冻结结果；状态卡和详细指标面板只消费解析后的模型。
- DI/测试：`SevenDaysGameOverviewQuery` 绑定 `IGameOverviewQuery`，`GetOverviewUseCase` 由 Bootstrap 注册；SevenDays、Dispatcher、Application、HTTP、OpenAPI、生成客户端、parser、composable 和指标面板均有测试触点。

#### 必须保留的复杂性

- 游戏线程调度、GamePrefs/GameManager/World 就绪检查和开始前取消/超时保证，是 7DTD 线程亲和性和生命周期产生的必要边界。
- `value/source/unit/observedAtUtc/warning` 并非重复展示字段；详细面板实际展示它们，且未知值不能伪造成零值。
- 缓存、in-flight 去重、独立取消和 stale 回退限制游戏线程读取频率，并区分“当前不可用”和“最后一次成功采样”。
- OpenAPI、生成客户端和运行时 parser 分别承担跨进程合同、机械传输和浏览器信任边界；不能直接删除生成层。

#### 已确认候选

##### SIM-013：未接入生产路径的手工概览请求包装

- 状态：`已完成`
- 类型：重复请求通道 / 纯转发
- 主要能力：Operations
- 风险等级：C
- 证据：`features/server-status/api/overview.ts` 只执行 `requestJson('/api/v1/overview') → parseOverview`；生产 `useOverview` 已使用生成的 `overviewGetQuery()`，静态引用只剩自身测试。
- 实际变更：删除 `features/server-status/api/overview.ts`；从 `overview.test.ts` 删除请求 mock、包装 import 和 `describe('fetchOverview')` 两项孤立测试；保留 `parseOverview` 测试、生成 SDK/query、`useOverview` 测试和 `OverviewStatusSummary` fixture 回退。
- 验证结果：全仓搜索不再发现手工 `fetchOverview` 生产包装；概览/parser、生成客户端和 `useOverview` 聚焦测试通过；受影响聚焦测试通过；Admin typecheck、lint、全量 Vitest `145` 文件/`1008` 项、production build、`api:check` 全部通过。
- 回滚方式：恢复该 API 文件及其测试，不改变 HTTP、OpenAPI 或领域模型。
- 结论：低风险纯转发删除完成；调用路径缩短，生成 query 仍是唯一生产请求入口。

##### SIM-014：摘要卡中的 fixture 兼容字段回退

- 状态：`已完成`
- 类型：兼容字段 / UI 回退
- 主要能力：Operations
- 风险等级：C
- 证据：parser 只接受现代 `runtimeMetrics` wire shape 并拒绝 legacy alias；四个回退读取只服务手写 frontend fixture，生产传输不生成这些字段。
- 实际变更：将首页 fixture 迁移到完整 `runtimeMetrics`；删除 `GameOverview` 的四个 fixture-only 可选字段，以及 `OverviewStatusSummary`、`ServerInformationPanel` 的四个 legacy 回退读取；保留 parser 对 legacy `gameTime` 的负例测试。
- 验证结果：server-status、首页和 ServerOperationsView 聚焦测试 `6` 文件/`55` 项通过，Admin typecheck 与聚焦 lint 通过；最终 Admin 全量门禁通过。
- 结论：兼容责任已从生产模型/UI 移除，wire/parser 和 `useOverview` 生命周期语义不变。

### B 级样本：CAP-07 彩色聊天设置更新

- 状态：`已确认`
- 入口：Owner 页面 `community/chat/appearance.vue` → `useColoredChat` → 生成的 `chatUpdateColoredSettingsMutation` → `PUT /api/v1/chat/colored/settings`。
- Admin：`ColoredChatView` 将服务端设置复制为 draft，校验六类颜色并把空值规范为 `null`；controller 管理取消、认证失效、403、并发保护、dirty 状态和以服务端 authoritative response 回填，不做乐观成功。
- Web：`ChatController` 在 Owner 授权下校验 ModelState 和枚举，读取 actor，将 HTTP model 映射为 Application model，调用保存用例并映射回 HTTP response。
- Application：`SaveColoredChatSettingsUseCase` 按严格顺序执行 actor 校验、业务规范化、`IColoredChatStore.SaveSettings`、`IChatRuntimeConfiguration.ApplyColoredChatSettings`、`IChatOperationAuditTrail.Record`，持久化成功后才热应用。
- Persistence：`SqliteColoredChatStore` 再次规范化 settings，更新 singleton row 并读回 authoritative 值；`007_GameChat.sql` 提供表、约束、默认值和索引。迁移由 DbUp 按脚本事务执行；普通 settings UPDATE 本身不是显式跨操作事务。
- Runtime：`ChatRuntimeState` 启动时加载 settings/profiles/mutes，保存后用 CAS 替换 immutable snapshot，避免更新颜色时覆盖其他 chat 状态。
- Audit/DI：Application 记录 `SaveColoredSettings` 和 changed fields；SQLite audit trail 只保存摘要；Bootstrap 显式绑定 Store、runtime configuration、audit 和 use case。
- 测试：SQLite 默认值和 round-trip、迁移/WAL、runtime 加载顺序、Admin picker/normalize、生成 mutation、HTTP/OpenAPI 均有覆盖；当前缺少针对彩色设置用例本身“store 成功 → runtime apply → audit”的直接测试。

#### 必须保留的复杂性

- Owner 授权、HTTP/Application/SQLite/运行时的端口和生成 OpenAPI 是真实信任与部署边界。
- 持久化成功后再热应用，避免数据库与运行时状态分叉。
- immutable/CAS snapshot 防止设置更新覆盖 profiles、chat settings 或 mutes。
- SQLite singleton/enum/RGB 约束和审计摘要属于数据完整性与可追溯性边界。

#### 已确认但暂缓处理的候选

##### SIM-015：彩色设置规范化在 Application 与 SQLite Adapter 重复执行

- 状态：`保留`
- 类型：重复校验
- 主要能力：Community
- 风险等级：B
- 证据：characterization tests 证明 Application 在持久化前规范化，SQLite Adapter 对直接端口写入独立规范化，SevenDays runtime 对直接配置独立规范化；这些入口可被独立调用。
- 候选处置：保留；规范化分别保护 Application 输入、持久化完整性和运行时 immutable snapshot，不是同一信任边界内的连续重复。
- 验证结果：保存顺序、store authoritative result、失败时不更新 runtime/audit、SQLite 直接写入、runtime 直接配置和 CAS 并发更新均有直接测试；Chat 聚焦测试 `61/61` 和最终后端聚合测试通过。

##### SIM-016：彩色设置读取用例的纯转发

- 状态：`保留`
- 类型：纯转发 Use Case
- 主要能力：Community
- 风险等级：B
- 证据：`GetColoredChatSettingsUseCase.Execute()` 只调用 `IColoredChatStore.GetSettings()`，直接测试证明它恰好执行一次 Application→port 委托且不重新规范化结果。
- 候选处置：保留；该一跳入口维持 Web→Application→port 依赖方向，并与其他 chat query use case 保持一致，删除会让 Web 形成直接 persistence 例外而没有减少真实边界。
- 验证结果：Application 委托、保存 ordering 和 authoritative response characterization 通过；未新增生产抽象或行为。

### A 级样本：CAP-03 备份恢复与回滚

- 状态：`已确认`
- 入口：已验证备份 → `BackupCatalogTable` → `BackupsView` → `RestoreConfirmModal` → `useBackups.restore` → 生成的 `restoreBackup` mutation。
- Web：`BackupsController` 只允许 Owner，强确认失败在 enqueue 前返回 422；成功路径建立幂等的 Restore job、调用 `StageRestore` 并返回 `202 PendingRestart`。
- Application：`StageRestore` 校验强确认、job 状态、artifact kind/id、manifest、verified、SHA-256 和 UTC；通过 `IBackupCatalog`、`IJobStore`、`IPendingRestoreMarkerStore`、`IRestartScriptLauncher` 端口，严格执行 marker → `Queued` CAS 到 `PendingRestart` → 可选启动脚本。
- Persistence：Restore job 和 `restore_job_payloads` 在 SQLite transaction 中写入；幂等键重试必须携带相同 payload；领域状态机要求 `Queued → PendingRestart`，重启后才能继续。
- Local Adapter：marker/receipt 使用批准根下的 versioned JSON 和原子替换；启动时重新验证 artifact snapshot、manifest、ZIP 路径、哈希、数量和压缩比，构造 staging、target、safety、rollback 路径。
- 安全副本/回滚：先写 `Prepared` receipt，再逐文件替换；对现有 target 创建同卷 safety copy；失败时按已替换文件回滚，无法确认时保留 `Prepared` 并映射 `ResultUnknown`。
- 启动顺序：`ApplyingPendingRestore → MigratingDatabase → ReconcilingRestoreResult`；恢复 panel SQLite 时必须先恢复、再 migration、最后 merge job 终态。
- 审计：当前主要通过 jobs 的 unified audit projection 暴露 `serverOperation`；它能提供 actor、correlation 和当前 job 状态，但不是每个 stage/rollback 的独立不可变审计事件。
- 测试：前端确认、Owner/强确认、StageRestore、marker/receipt、ZIP 安全、原子替换、回滚、启动三阶段、merge-once、DI 均有聚焦测试。

#### 必须保留的复杂性

- marker 与 receipt 分离于待恢复数据库，才能在 panel SQLite 被覆盖或进程中断时保留恢复事实。
- catalog/artifact 双重验证、approved-root、ZIP-slip/压缩炸弹限制和逐文件哈希是恢复安全边界。
- safety copy、逐文件回滚和 `Prepared/RolledBack/RollbackFailed/ResultUnknown` 是多文件副作用可恢复性的成本。
- 恢复、migration、结果合并的固定启动顺序是数据库恢复一致性的必要条件。
- 幂等键、row-version CAS、强确认和 Owner 授权不可因减少层数而删除。

#### 已确认的高风险不一致

##### SIM-017：Restore job 对通用 worker 可见但不应被 worker 消费

- 状态：`已完成`
- 类型：状态机/队列语义不一致
- 主要能力：Operations
- 风险等级：A
- 证据：`BackgroundWorkerJobStore.TryClaimNext` 原先用 `kind <> Restore` 负向排除；这能避开当前 Restore，却会让未来未知 JobKind 默认可被 generic worker claim，且能力边界没有显式表达。
- 实际变更：建立七种 generic-worker-supported JobKind 正向 allowlist，并在候选 SELECT 与 row-version CAS UPDATE 使用相同参数化约束；Restore 和未知类型均不可 claim，Restore 在队首不阻塞后续支持类型。
- 保留的不变量：`BEGIN IMMEDIATE`、FIFO、worker id、row-version CAS、rollback 和 consumer 的 Restore 防御分支不变；`StageRestore`、marker、receipt、safety copy、rollback、startup、migration 和 OpenAPI 均未修改。
- 验证结果：supported/Restore/unknown/后续可执行 Job 回归覆盖通过；Restore/worker 聚焦测试 `78/78`，最终后端聚合测试通过。
- 结论：A 级正确性边界已由负向例外改为显式能力 allowlist，没有通过删除恢复安全机制来简化。

## 当前处置结论

经过两轮证据驱动处理：

1. `SIM-013`：已完成删除手工概览请求包装，生成 query 保持唯一生产请求入口；
2. `SIM-014`：已迁移 fixture 并删除生产模型/UI 中的 legacy 回退；
3. `SIM-015`：保留；Application、SQLite 与 runtime 规范化保护独立信任边界；
4. `SIM-016`：保留；读取用例维持 Web→Application→port 依赖方向；
5. `SIM-017`：已完成 generic worker 正向 JobKind allowlist 修复，恢复安全边界全部保留。

本轮不批准物理项目合并或批量接口删除；后续候选仍需按相同的生产调用链、characterization、独立审查和聚合门禁流程处理。

## 候选记录模板

新增候选时复制以下模板。每个候选只能有一个主要能力所有者。

```markdown
## SIM-###：候选名称

- 状态：待调查
- 类型：接口 / 转发层 / DTO / 映射 / 项目边界 / 文档 / 导航 / 测试 / 功能
- 主要能力：Operations / Players / Community / Economy / Automation / Administration / Platform
- 风险等级：A / B / C
- 当前实现锚点：
- 当前调用路径：
- 生产消费者：
- 外部边界：
- 当前成本：
- 保留价值：
- 候选处置：保留 / 合并 / 删除 / 自动生成 / 暂缓
- 预期减少的概念：
- 不允许退化的不变量：
- 所需验证：
- 权威文档影响：
- 回滚方式：
- 结论：
```

## 处置判断

### 保留

满足以下任一条件时通常保留：

- 跨越游戏、SQLite、文件系统、网络、进程或 Web 信任边界；
- 保护明确的跨项目依赖方向；
- 存在第二个生产实现或消费者；
- 承载业务不变量、授权、事务、幂等、恢复或状态转换；
- 隔离难以控制的生命周期、并发或第三方兼容风险。

### 合并

同时满足以下条件时优先考虑合并：

- 层之间不跨信任或运行时边界；
- 中间层没有独立校验、授权、事务、状态或错误语义；
- 合并后仍能保持局部测试和依赖方向；
- 调用者数量和影响范围可控；
- 有明确、低成本回滚方式。

### 删除

同时满足以下条件时可以考虑删除：

- 没有生产消费者；
- 不属于已批准近期范围；
- 不承载兼容、恢复或发布责任；
- 删除不会迫使其他边界直接耦合；
- 适用测试和构建可以证明无行为退化。

### 自动生成

满足以下条件时考虑由工具生成，而不是继续手写：

- 内容由单一合同确定；
- 人工编辑不应成为事实来源；
- 生成结果可确定性复现和漂移检查；
- 生成物不会隐藏业务规则或失败语义。

## 每轮实施限制

每一轮简化必须：

1. 选择不超过三个已确认候选；
2. 优先选择 C 级或低耦合 B 级候选；
3. 不在同一轮同时改变产品行为和物理项目边界；
4. 先运行受影响的聚焦验证，稳定后只运行一次适用聚合验证；
5. 更新候选结论和必要的权威文档；
6. 记录回滚方式；
7. 比较修改前后的调用路径和概念数量；
8. 若没有可测量的理解或变更成本改善，停止扩大同类处理。
