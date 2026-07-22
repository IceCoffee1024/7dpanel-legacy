---
state: Current
document_role: Change Record
last_updated: "2026-07-22"
---

# Owner 踢出在线玩家与持久审计设计规格

> 本规格描述尚未实现的后端与 Admin 纵向切片，不是当前实现证据。当前产品、界面、架构和测试事实分别以[产品需求](../../PRD.md)、[界面设计](../../design.md)、[系统架构](../../architecture.md)和[测试策略](../../test.md)为准；批准的未来后端边界见[后端目标架构蓝图](../../architecture/backend-target-blueprint.md)。

## 上游与目标

本切片落实 `CAP-02` 和 `CAP-05`：当前 `Owner` 从在线玩家页面明确确认并填写原因后踢出一名在线玩家；后端在游戏主线程重新校验目标身份、调用 7DTD 类型化踢出 API，并持久化操作者、目标、原因、时间和可信结果。

本切片同时验证第一条状态变更链路所需的边界：Application 用例编排、动作专属容量、持久审计意图、主线程目标防竞态、同步结果和未知结果语义。它不把当前只读在线玩家查询或受限控制台命令的成功证据当作状态变更动作已经通过。

## 范围

本切片包含：

- Owner-only 的 `POST /api/v1/players/{entityId}/kick`。
- 显式确认和 trim 后 1 至 200 个字符的必填原因。
- 以当前在线玩家快照中的平台身份防止 `entityId` 复用导致误踢。
- Application 中的 `KickPlayerUseCase`、请求与结果模型以及窄 `IPlayerActions` 输出端口。
- SevenDays Adapter 中基于 `GameThreadDispatcher` 和 `GameUtils.KickPlayerForClientInfo` 的类型化实现。
- SQLite 中永久保留的玩家动作审计记录和遗留 `Pending` 恢复。
- Admin 桌面表格和移动列表中的动作菜单、确认对话框、稳定错误呈现和成功后刷新。
- 后端、前端和持久化边界的自动化验证，以及明确保留的真实进程证据缺口。

本切片不包含：

- 封禁、解除封禁、禁言、传送或任意控制台命令。
- 通用玩家动作注册表、命令总线或新的 Domain 项目。
- Admin 审计查询页面、动作状态轮询 API 或自动重试。
- `Admin`、`Viewer` 或通用权限矩阵；当前持久身份仍只有 `Owner`。
- 审计自动清理、保留期配置或归档任务。
- 把真实 Windows `v3.0.1-b4` 玩家断开验证设为本切片自动化完成门。

## 方案取舍

采用类型化玩家动作端口和持久审计，不通过 `IRestrictedConsoleGateway` 拼接 `kick` 命令。控制台路径依赖名称或字符串参数解析和文本输出，不适合作为稳定产品 API，也无法可靠防止过期快照误踢。

不预先建立 Kick、Ban 和 Teleport 的通用动作框架。三种动作的目标、确认、持久状态和失败语义不同；当前只有 Kick 一个生产消费者，提前抽象会违反仓库的最小完整纵向切片规则。

首版同步等待动作终态。常规情况下请求直接返回可信成功或失败；如果客户端连接在游戏动作开始后先结束，Application 仍完成审计，不能把 HTTP 取消或超时伪造成动作失败。首版不增加 `202 Accepted`、状态查询 API 或客户端幂等键。

## HTTP 契约

### 请求

```http
POST /api/v1/players/{entityId}/kick
Authorization: Bearer <token>
Content-Type: application/json
```

```json
{
  "expectedPlatformIdentity": {
    "combinedId": "Steam_76561198000000000",
    "platform": "Steam"
  },
  "reason": "违反服务器规则",
  "confirmed": true
}
```

- `entityId` 必须是非负整数。
- `expectedPlatformIdentity.combinedId` 和 `platform` 必须与在线玩家快照中的主平台身份一致且非空。
- `reason` 在后端 trim 后必须为 1 至 200 个字符，允许 Unicode；相同内容用于游戏拒绝消息和审计。
- `confirmed` 必须精确为 `true`。缺失或为 `false` 时请求在创建审计和进入游戏主线程前被拒绝。
- Controller 从当前认证主体取得稳定 `Subject=owner`，不接受客户端提交操作者身份。

### 成功响应

当目标身份在游戏主线程重新校验通过，且 `GameUtils.KickPlayerForClientInfo` 已返回并安排断开时，返回 `200 OK`：

```json
{
  "operationId": "8f742dcfe65a454d8f919e164ace77d7",
  "status": "succeeded",
  "target": {
    "entityId": 171,
    "name": "Player",
    "platformIdentity": {
      "combinedId": "Steam_76561198000000000",
      "platform": "Steam"
    }
  },
  "requestedAtUtc": "2026-07-22T08:00:00.0000000+00:00",
  "completedAtUtc": "2026-07-22T08:00:00.1000000+00:00"
}
```

这里的 `succeeded` 表示 7DTD 原生 API 已接受目标并安排断开，不表示 HTTP 请求内已经观察到玩家从连接集合消失。参考 `v3.0.1-b4` 实现会先发送 `NetPackagePlayerDenied`，再通过协程约延迟 0.5 秒断开连接；真实进程验收需要另外观察玩家随后离线。

### 稳定失败

所有普通 API 失败继续使用当前 `application/problem+json` 和稳定 `code`：

| HTTP | `code` | 语义 |
|---|---|---|
| 400 | `player_kick_confirmation_required` | 未明确确认，未创建审计或游戏副作用 |
| 400 | `invalid_player_kick_reason` | 原因 trim 后为空或超过 200 个字符 |
| 400 | `invalid_player_identity` | 预期平台身份缺失或无效 |
| 401 | `authentication_required` | 沿用当前认证失败语义 |
| 403 | `forbidden` | 当前主体无权执行动作 |
| 409 | `player_not_online` | 主线程校验时目标已离线 |
| 409 | `player_identity_changed` | 同一 `entityId` 已对应另一平台身份 |
| 503 | `game_not_ready` | 游戏运行时尚未就绪 |
| 503 | `player_action_busy` | 另一个踢出动作占用本 Gateway 容量 |
| 503 | `game_thread_timeout` | 游戏主线程未在启动截止时间前开始动作，且动作不会晚到执行 |
| 503 | `audit_unavailable` | 审计意图无法持久化，因此动作没有执行 |
| 503 | `audit_completion_unavailable` | 动作结果已经产生，但审计终态暂时无法确认 |
| 500 | `player_kick_failed` | 原生动作执行抛出未公开的内部失败 |

Problem Details 不包含原始异常、堆栈、数据库路径、玩家 IP 或其他未批准字段。内部日志使用当前 trace id，并在已经创建审计时同时记录 `operationId`。

## Application 与数据流

### 组件职责

- Web Adapter 负责路由、Owner 授权、HTTP DTO 校验、认证主体提取和 Problem Details 映射。
- `KickPlayerUseCase` 负责应用级校验、踢出专属 single-flight、审计生命周期、调用顺序和稳定结果。
- `IPlayerActions` 只表达类型化的踢出动作，不接收 HTTP、SQLite、`ClientInfo`、`EntityPlayer` 或控制台文本。
- SevenDays Adapter 负责游戏就绪后的实时目标解析、主线程身份校验和原生 API 调用。
- SQLite Adapter 负责审计 migration、插入意图、合法终态更新和遗留状态恢复，不承载游戏规则。
- Bootstrap 只增加显式 DI 注册和现有生命周期中的恢复调用，不引入自动扫描。

### 执行顺序

```text
PlayersController
  -> validate HTTP shape and Owner authentication
  -> KickPlayerUseCase
  -> validate confirmation, reason and expected identity
  -> acquire kick-specific single-flight gate
  -> persist Pending audit intent
  -> IPlayerActions.KickAsync
  -> GameThreadDispatcher
  -> resolve ClientInfo by entityId on the game thread
  -> compare current primary platform identity
  -> GameUtils.KickPlayerForClientInfo(ManualKick, reason)
  -> complete audit as Succeeded / Failed / Unknown
  -> return stable HTTP result
  -> release gate
```

动作专属 single-flight 必须由 `KickPlayerUseCase` 在插入 `Pending` 之前获取。未获得容量的请求直接返回 `player_action_busy`，不会生成没有执行机会的审计记录。该 gate 只保护当前踢出用例实例，不推广为所有玩家动作的全局基础设施；SevenDays Gateway 仍只拥有主线程解析和原生动作。

### 取消与超时

- 请求尚未进入游戏主线程时，客户端取消、启动截止时间或宿主停止可以阻止动作执行。
- `GameThreadDispatcher` 一旦把请求从 `Pending` 原子切换为 `Running`，客户端取消或 HTTP 超时不得终止动作，也不得覆盖真实结果。
- 主线程启动超时必须保证已排队委托随后不会执行。
- 动作开始后即使 HTTP 连接断开，Application 仍等待动作返回并尝试完成审计。
- 宿主停止时拒绝新动作；尚未开始的动作取消并完成为 `Failed`，已开始的动作等待原生调用结果后再完成审计。

## 审计模型

首版新增单一玩家动作审计表，永久保留记录，不增加清理任务。最小字段为：

| 字段 | 语义 |
|---|---|
| `operation_id` | 服务端生成的不可变 32 位小写十六进制标识，主键 |
| `action_type` | 首版固定为 `kick` |
| `actor_subject` | 当前固定为 `owner` |
| `target_entity_id` | 请求时目标实体 id |
| `target_name` | 动作执行时重新解析到的玩家名快照；目标已离线且无法解析时为空 |
| `target_platform_id` | 预期主平台 `combinedId` |
| `target_platform` | 预期主平台名称 |
| `reason` | trim 后的踢出原因 |
| `requested_at_utc` | 审计意图持久化时间 |
| `completed_at_utc` | 终态时间，可空 |
| `status` | `Pending`、`Succeeded`、`Failed` 或 `Unknown` |
| `failure_code` | 稳定失败码，可空 |

首版不增加可见 `Running` 状态，因为没有状态查询消费者。合法状态转换只有：

```text
Pending -> Succeeded
Pending -> Failed
Pending -> Unknown
```

- 审计意图插入失败时返回 `audit_unavailable`，绝不能调用游戏动作。
- `player_not_online`、`player_identity_changed`、主线程启动超时和原生调用失败完成为 `Failed`。
- 原生 API 已返回时完成为 `Succeeded`，不等待连接集合变化。
- 终态更新失败时记录保持 `Pending`，接口返回 `audit_completion_unavailable`，不能伪造 `Failed`。
- 每次进程启动在接受玩家动作前，把上次进程遗留的 `Pending` 原子更新为 `Unknown`；崩溃后无法证明动作是否执行，因此不得推断成功或失败。
- 终态更新只允许匹配 `operation_id` 且当前状态为 `Pending`，防止重复完成覆盖原始证据。

## SevenDays Adapter

主线程动作不得使用 `Party.KickPlayer`，该 API 只移除队伍成员。`v3.0.1-b4` 的服务器踢出路径由 `ConsoleCmdKick` 调用：

```csharp
GameUtils.KickPlayerForClientInfo(
    clientInfo,
    new GameUtils.KickPlayerData(
        GameUtils.EKickReason.ManualKick,
        0,
        default(DateTime),
        reason));
```

SevenDays Adapter 必须：

- 在 `GameThreadDispatcher` 委托内部读取 `ConnectionManager.Instance.Clients`。
- 按 `entityId` 找到唯一 `ClientInfo`，目标不存在时返回类型化 `PlayerNotOnline`。
- 比较当前 `PlatformId.CombinedString` 和 `PlatformIdentifierString` 与请求中的预期身份，任一不一致时返回类型化 `PlayerIdentityChanged`。
- 使用主线程重新读取的玩家名作为成功结果和审计目标快照。
- 只在身份校验通过后构造 `ManualKick` 并调用 `GameUtils.KickPlayerForClientInfo`。
- 不解析控制台输出，不接受任意命令，不把 `ClientInfo` 暴露给 Application。

## Admin 交互

- 桌面表格和移动列表复用一个玩家动作入口；首版菜单只包含“踢出玩家”。
- 按钮和菜单使用现有 Nuxt UI 与图标库；危险命令使用明确文案和危险色，不使用只靠颜色表达的状态。
- 打开确认对话框时固定目标快照，展示玩家名、平台和身份标识；后续 10 秒轮询不得替换正在确认的目标。
- 原因为多行输入，trim 后 1 至 200 个字符；前端即时校验只改善体验，不能替代后端校验。
- 确认按钮明确显示“踢出玩家”。提交期间锁定表单和目标，禁止关闭后重复提交同一请求。
- 成功后关闭对话框、显示包含玩家名的成功通知并立即刷新在线玩家。由于原生 API 延迟断开，首次刷新仍包含目标不等同于动作失败。
- `player_not_online` 和 `player_identity_changed` 关闭或阻止继续提交，并刷新列表；身份变化必须提示原快照已经过期。
- `player_action_busy`、`game_not_ready`、`game_thread_timeout` 和 `audit_unavailable` 保留对话框及原因，允许用户显式重试。
- `401` 沿用当前会话失效跳转；`403` 隐藏或禁用动作并显示无权限状态。
- 网络错误和 `audit_completion_unavailable` 显示“结果尚无法确认”，不得自动重试或渲染为失败。刷新在线玩家只能作为辅助信息，不能重写审计结论。
- 本切片不新增审计查询页面；持久记录由后续 `CAP-05` 查询切片提供 UI。

## 验证

### Application

- 未确认、无效原因和无效身份在审计与动作之前拒绝。
- 获取不到动作容量时返回 busy 且不插入审计。
- `KickPlayerUseCase` 的 single-flight 在成功、异常、取消和超时后都释放。
- 审计意图必须先于 `IPlayerActions` 调用成功持久化。
- 每种类型化动作结果完成到正确终态和稳定失败码。
- 动作成功后终态写入失败保持 `Pending`，不能改写为失败。
- 进程启动把遗留 `Pending` 更新为 `Unknown`，并且不会覆盖已有终态。

### SevenDays Adapter

- 所有游戏对象访问和原生动作都发生在 dispatcher 委托内。
- `entityId` 和主平台身份必须同时匹配。
- 调用参数精确使用 `ManualKick` 和批准原因。
- 排队阶段取消或启动超时后委托不会晚到执行。
- 动作已经开始时，请求取消不会覆盖真实结果。
- 主线程解析和原生动作在成功、异常、取消和超时路径都返回确定结果，不保留活动游戏对象。

### SQLite Adapter

- DbUp migration 首次执行和重复启动均成功。
- 审计插入保存不可变操作者、目标、原因和时间快照。
- 终态更新只从 `Pending` 执行一次。
- 并发完成、数据库锁定、写入失败和启动恢复具有确定性结果。
- 连接和文件句柄在成功与失败路径释放。

### Web Adapter

- 匿名请求返回 401，Owner 请求可以执行；当前未提供的非 Owner 角色路径不伪造测试证据。
- 请求 JSON、路径参数、确认、原因和身份校验具有稳定 400 Problem Details。
- 游戏未就绪、busy、离线、身份变化、主线程超时和审计不可用映射到批准的状态码与 `code`。
- Controller 不直接引用游戏程序集、SQLite 实现或活动游戏对象。
- camelCase 成功响应只包含批准字段。

### Admin

- 桌面表格和移动列表都能打开同一个确认流程。
- 对话框固定原目标快照，轮询更新不会改变待执行目标。
- 原因边界、单次提交、成功通知和立即刷新可确定验证。
- 401、403、离线、身份变化、busy、不可用和未知结果使用稳定文案，不泄露原始服务端异常。
- 未确认和无效原因不能发出请求；网络错误不触发自动重试。

### 聚合门与证据缺口

- 后端 Release build 和全部自动化测试通过。
- Admin lint、typecheck、Vitest 和 build 通过。
- 现有 Playwright 真实环境场景保持可配置，但本切片不把真实游戏踢出设为自动化完成门。
- Windows `v3.0.1-b4` 上玩家收到拒绝原因、约 0.5 秒后断开、在线列表更新且 SQLite 审计一致仍是明确未验证项。
- 在完成该真实进程验证前，当前架构和测试文档不得宣称 `CAP-02` 的真实踢出验收已经通过。

## 文档影响

- [产品需求](../../PRD.md)中的 `CAP-02` 和 `CAP-05` 已拥有产品合同，本设计不修改其范围。
- 本规格批准后才创建一个链接到本文的 dated implementation plan。
- 实现完成并验证后，才把持久审计、玩家动作和当前错误合同提升到[系统架构](../../architecture.md)。
- 实现 Admin 交互后更新[界面设计](../../design.md)中的当前玩家页面行为。
- 自动化策略和真实证据缺口在实现后同步到[测试策略](../../test.md)。
- 精确模块命令或契约说明只更新最近的后端或 Admin README，并从高层文档链接，不复制命令块。
- [后端目标架构蓝图](../../architecture/backend-target-blueprint.md)已经定义批准的玩家动作流；除非实现发现目标决策本身需要改变，否则不更新。
- `CHANGELOG.md` 只在该能力准备作为用户可见功能发布时更新。

## 完成定义

- 本规格中的后端、SQLite、Web 和 Admin 自动化验证通过。
- 所有新增错误码、响应字段和审计状态与本规格一致。
- 状态变更动作没有通过控制台字符串、Adapter 互相引用或未批准的通用框架实现。
- 当前架构、界面和测试文档只记录已经实现并验证的事实。
- 真实游戏断开证据仍缺失时被显式记录，不以模拟调用或自动化测试替代。