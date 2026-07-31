---
state: Current
document_role: Design Spec
last_updated: "2026-07-31"
---

# 聊天命令混合测试设计规格

## 文档角色与批准状态

本文档是用户已批准的聊天命令“虚拟玩家为主、真实 `ClientInfo` 为窄边界补充”测试设计，当前有效，但不是当前实现或测试通过证据。当前组件、生命周期和依赖事实以[系统架构](../../architecture.md)为准，验证分级、真实环境门禁和证据状态以[测试策略](../../test.md)为准。

本设计只增加测试能力，不改变聊天命令的产品合同、玩家可见命令语义、权限模型或正常生产路径。实现完成并取得证据后，才能把稳定事实提升到上述 living docs。

## 背景与问题

当前自动化可以直接验证 `GameChatCommandCatalog`、Community consumer、SQLite 状态机和审计，也有通过反射构造未初始化 `ClientInfo` 形状后调用聊天协调器的桥接测试。这些测试适合快速覆盖命令矩阵，但不能证明以下真实边界：

- 运行中的 `ConnectionManager` 能按稳定玩家身份找到唯一在线连接；
- 每次执行前都能从当前连接重新取得 `ClientInfo`，而不是持有可能离线或换人的旧引用；
- 真实 `ClientInfo` 能进入现有聊天命令入口并使用私发回复路径；
- 真实 7DTD 生命周期、主线程调度和游戏类型签名与当前二进制兼容。

把全部命令都交给真实玩家执行会使自动化变慢、环境脆弱，并可能误触发踢出、重启、传送或奖励。因此采用两层混合模型：虚拟玩家测试承担主要正确性门禁，真实 `ClientInfo` 测试只证明无法由虚拟对象证明的游戏边界。

## 目标

- 以虚拟玩家矩阵持续覆盖命令解析、路由、业务 consumer、审计、幂等和错误结果。
- 在真实 `v3.0.1-b4` 进程中，使用一个由稳定 ID 明确指定的在线玩家验证窄 `ClientInfo` 边界。
- 对危险动作设置不可绕过的安全默认值，并让每个未执行动作产生明确 `Skipped` 或 `RecordedOnly` 证据。
- 复用现有 `GameThreadDispatcher`、聊天协调器、命令目录和类型化 Application 端口，不引入第二套生产命令框架。
- 不要求 Harmony；测试运行器通过现有 Mod 生命周期和显式方法调用进入命令路径。

## 非目标

- 不以真实玩家测试替代单元、SQLite 集成或 Application 测试。
- 不从显示名、entity ID、在线列表顺序或“第一个玩家”推断测试目标。
- 不缓存 `ClientInfo`、`EntityPlayer`、网络连接或其他游戏活对象供后续步骤使用。
- 不注册新的玩家聊天命令、HTTP 管理面、脚本入口或通用测试插件框架；仅提供固定的管理员控制台入口 `7dp-test chat <status|virtual|boundary|all>`。
- 不用 Harmony patch `ModEvents.ChatMessage`、命令执行或玩家动作。
- 不自动执行真实踢出或真实服务器重启，也不允许配置项打开这两类副作用。
- 不在默认配置下执行真实传送或奖励发放。
- 不把缺少真实玩家记录为测试通过，也不因真实玩家缺失而让虚拟主门禁失效。

## 总体模型

```text
虚拟主通道
  virtual player snapshot
    -> production parser/catalog/router
    -> real Application use case + isolated SQLite/test doubles
    -> assertions and recorded effects

真实窄通道（默认关闭）
  administrator console trigger after GameReady
    -> resolve current ClientInfo by exact stable ID on game thread
    -> build one typed chat-command test invocation
    -> production coordinator/catalog/router
    -> private reply + typed effect policy
    -> structured Passed/Failed/Skipped/RecordedOnly evidence
```

两个通道使用同一规范命令能力标识、活动命令描述和结果码，但不共享可变测试状态。真实窄通道不得替换生产活动目录、拦截普通玩家消息或修改正常聊天订阅。

## 虚拟玩家主通道

虚拟玩家是只含稳定 `crossplatformId`、显示名、当前在线/世界/位置等命令所需标量的测试夹具，不伪造或序列化 `ClientInfo`。测试直接组合当前生产 parser、`GameChatCommandCatalog`、Community router/consumer 和类型化用例，按场景使用隔离 SQLite 数据库、固定时钟、固定 ID factory 与记录型游戏端口。

虚拟矩阵是每次变更必须运行的主门禁，至少覆盖：

- `help`、名称/别名、大小写、前缀、`AllowNoPrefix` 和参数分隔符；
- 普通聊天放行、未知命令、停用命令、参数无效和 consumer 异常；
- 经济、商店、兑换、每日奖励、家、城市、TPA 和投票的成功与拒绝路径；
- `Begin(pending) → Execute once → Complete(terminal)` 审计顺序，以及 intent/terminal 写入失败语义；
- 同一幂等键、重复消息、并发和恢复场景不会重复产生业务副作用；
- 私发结果只记录规范结果码和消息数量，不依赖真实网络连接。

除专门验证游戏程序集签名的既有兼容测试外，新增业务矩阵不得通过 `FormatterServices.GetUninitializedObject` 把未初始化对象当成真实 `ClientInfo` 证据。

## 真实 `ClientInfo` 窄通道

### 启用与生命周期

配置节固定为 `ChatCommandTesting`：

| 字段 | 默认值 | 规则 |
|---|---:|---|
| `Enabled` | `false` | 只控制真实边界；为 `false` 时不解析玩家、不执行真实探针，也不产生游戏副作用，`status`/`virtual` 仍可用 |
| `TestPlayerId` | `null` | 启用时必填；trim 后作为稳定跨平台 ID 使用 |
| `AllowTeleport` | `false` | 只有 `Enabled=true` 时才可能生效 |
| `AllowRewardDelivery` | `false` | 只有 `Enabled=true` 时才可能生效 |

无效配置必须记录安全诊断并整体回退为禁用，不得猜测玩家或部分启用。控制台桥随 Mod 生命周期注册，但只有管理员在 `GameReady` 后显式执行固定命令时才运行诊断；它不订阅或 patch 普通聊天事件。

### 稳定玩家选择与重解析

`TestPlayerId` 优先与当前 `ClientInfo.CrossplatformId.CombinedString` 做精确 ordinal 匹配；没有跨平台匹配时允许精确回退到 `PlatformId.CombinedString`。禁止回退到 `playerName`、entity ID、模糊比较或首个在线玩家；任一层出现多匹配都不得任意选取。

每个真实场景都必须在 `GameThreadDispatcher` 委托内重新枚举当前连接并解析唯一 `ClientInfo`，随后在同一受控调用内复制需要的稳定标量并调用聊天命令入口。运行器不能跨场景保存 `ClientInfo`。零个匹配、多个匹配、身份字段缺失、玩家正在断开或游戏不再 ready 时，当前场景记录为 `Skipped`；不得把它改成失败副作用、改选其他玩家或自动重试状态变更命令。

### 调用边界

真实场景直接构造类型化聊天命令测试输入并调用现有协调器/目录路径，不把测试文本广播为普通全局聊天。命令处理和私发回复都在受控游戏线程边界内使用刚解析的 `ClientInfo`；离开该边界后只保留稳定 ID、当次 entity ID、显示名、结果码和计数。

这一路径验证真实游戏对象与私发签名，但不需要 Harmony，因为它不观察或改写游戏原生命令执行点，也不动态 patch `ModEvents.ChatMessage`。

## 副作用策略

| 场景类别 | 默认行为 | 可否放开真实副作用 | 结果状态 |
|---|---|---|---|
| 只读、解析、清单和私发回复 | 使用真实 `ClientInfo` 执行窄边界 | 是，属于默认窄验证 | `Passed` / `Failed` / `Skipped` |
| 踢出玩家 | 只调用记录型端口，保存固定目标与调用次数 | 否，始终禁止真实踢出 | `RecordedOnly` |
| 服务器重启 | 只调用记录型端口，保存计划意图与调用次数 | 否，始终禁止真实重启 | `RecordedOnly` |
| 传送 | 默认不调用真实端口 | 仅 `AllowTeleport=true`，且本场景再次解析同一稳定玩家后允许 | 默认 `Skipped`；显式放开后为 `Passed` / `Failed` |
| 奖励发放 | 默认不调用真实端口 | 仅 `AllowRewardDelivery=true`，且本场景再次解析同一稳定玩家后允许 | 默认 `Skipped`；显式放开后为 `Passed` / `Failed` |

踢出与重启必须在测试组合根中绑定专用记录实现，不能仅依赖场景代码“不要调用”。即使其他 opt-in 均开启，这两类端口也不能解析到生产动作实现。

传送和奖励的 opt-in 只允许规格内固定、类型化场景，不接受配置中的任意命令文本、坐标、物品名、奖励包或脚本。状态变更一旦开始，不因日志超时或测试取消自动重试；结果未知必须如实记录，避免重复传送或发放。

## 隔离与组合边界

- 虚拟通道使用测试项目内的 isolated fixture，不进入发布物运行路径。
- 真实通道使用专用、显式组合的测试场景集；不得替换全局 `GameChatCommandCatalog` 或污染普通玩家使用的活动快照。
- 记录型 kick/restart 端口只属于测试组合，不提升为通用生产 action registry。
- 真实 teleport/reward 只复用已有类型化 Application/SevenDays 端口和已有幂等/审计边界，不创建直接游戏 API 快捷路径。
- 测试运行器不新增数据库 schema。需要业务状态时复用现有专用 Store；运行摘要通过结构化日志进入真实 smoke 证据，不建立第二套审计事实源。
- 停服时停止接收新场景，等待当前受控调用限时收束，并将未开始项记录为 `Skipped`、已开始但结果不可确认项记录为 `Failed` 或既有未知结果码。

## 证据模型

每次运行使用一个进程内 `runId`，为每个场景输出一条结构化摘要，至少包含：

- `runId`、场景 ID、规范命令能力 ID 和实际调用名称；
- 通道 `Virtual` 或 `RealClientInfo`；
- 目标稳定 ID 的安全摘要、当次解析到的 entity ID 与解析时间；
- `Passed`、`Failed`、`Skipped` 或 `RecordedOnly`；
- 规范结果码、handler/动作调用次数和副作用策略；
- `Skipped`/失败原因，以及 teleport/reward opt-in 的有效值；
- kick/restart 的记录型端口证明，不含真实动作成功声明。

证据不得记录完整聊天正文、命令参数、兑换码、Token、私发正文、网络对象字符串或 `ClientInfo.ToString()`。汇总必须分别报告虚拟通过数、真实通过数、跳过数、record-only 数和失败数；`Skipped` 与 `RecordedOnly` 不计作真实副作用通过。

## 失败与跳过语义

- 虚拟主门禁断言失败：测试失败，阻止该变更通过。
- 测试功能禁用：不运行真实通道，属于预期默认状态。
- 启用但 `TestPlayerId` 无效：配置整体禁用并记录诊断。
- 指定玩家未在线或中途离线：相关真实场景 `Skipped`，运行器不得选择替代玩家。
- kick/restart：调用记录型端口成功后为 `RecordedOnly`，永远不能记为真实动作 `Passed`。
- teleport/reward 未 opt-in：对应场景 `Skipped`，不是通过。
- 已 opt-in 的真实动作结果未知：按既有类型化结果记录失败/未知，不自动重试、不伪造成功。
- 真实通道失败不抹除已经通过的虚拟证据，但候选发布的真实聊天命令门禁仍保持未满足。

## 验证要求

### 自动化

- options 默认禁用、启用时稳定 ID 必填、禁用时两个副作用 flag 强制为 false；
- 虚拟玩家完整命令矩阵、审计顺序、幂等、普通聊天放行与单次执行；
- 真实 resolver 的精确稳定 ID、零/多匹配、每场景重解析、离线和身份变化；
- kick/restart 无法解析到真实端口，teleport/reward 双重 gate 默认拒绝；
- 真实场景不会替换生产目录、不会注册第二个普通聊天订阅、不会持有 `ClientInfo`；
- 生命周期只运行一次、停止取消、结果分类和敏感字段扫描；
- 依赖规则继续禁止 Core/Application 引用 7DTD 类型。

### 真实进程

一次受控 Windows `v3.0.1-b4` smoke 应按以下顺序保留证据：

1. 默认配置确认测试功能未启动；
2. 只设置 `Enabled=true` 和精确 `TestPlayerId`，保持两个 opt-in 为 false；
3. 指定玩家在线时验证真实解析、至少一个只读命令与私发结果；
4. 验证 kick/restart 仅为 `RecordedOnly`，teleport/reward 为 `Skipped`；
5. 指定玩家离线后再次运行或在场景间离线，确认结果为 `Skipped` 且没有替代玩家；
6. 只有获得单独操作确认时，才分别开启 teleport 或 reward opt-in 执行对应窄场景；
7. 恢复 `Enabled=false`，正常关服并确认没有额外 Harmony patch 或未收束工作。

Linux 对应证据不由本设计自动视为通过；是否加入候选发布门禁继续由测试策略决定。

## 完成条件

- 虚拟玩家仍是聊天命令正确性的主要自动化门禁，且不依赖伪造 `ClientInfo`。
- 默认发布配置中测试功能关闭，所有真实状态变更开关关闭。
- 真实玩家只由精确稳定 ID 选择，每个场景在游戏线程重新解析 `ClientInfo`，缺失时明确跳过。
- kick/restart 在任何配置下都只记录不执行；teleport/reward 只有显式 opt-in 才能进入现有类型化真实端口。
- 真实窄通道使用现有生命周期和显式调用，不安装或要求 Harmony patch。
- 运行摘要能区分 `Passed`、`Failed`、`Skipped` 与 `RecordedOnly`，且不泄漏敏感正文或游戏网络对象。
- 自动化与一次适用真实进程 smoke 取得后，再按证据更新系统架构和测试策略；在此之前本文保持变更设计角色。
