---
state: Current
document_role: Implementation Plan
primary_spec: ../specs/2026-07-26-legacy-parity-economy-community-design.md
last_updated: "2026-07-27"
---

# 旧版本功能对齐第四波：经济、奖励、传送与社区投票实施计划

> **面向智能体执行者：** 使用 `superpowers:subagent-driven-development`（推荐）或 `superpowers:executing-plans` 逐任务实施，并使用 checkbox（`- [ ]`）跟踪进度。

**对应规格：** [旧版本功能对齐第四波：经济、奖励、传送与社区投票设计规格](../specs/2026-07-26-legacy-parity-economy-community-design.md)

**目标：** 交付服务器本地双重记账、幂等奖励和商业流程、家/城市/好友/返回点传送及踢人/重启投票，使玩家只通过登记式游戏命令消费能力，`Owner` 只通过专用 Admin API 和页面管理配置与核对异常状态。

**架构：** Domain 只承载账本平衡、grant/购买/传送/投票状态转换等纯不变量；Application 以稳定 `crossplatformId` 协调能力专属原子 Store、第三波类型化物品动作、第二波持久作业和 SevenDays 类型化传送。SQLite 以 `011_EconomyCommunity.sql` 建立本波基础写模型，并以向前 migration 承载已发布库的兼容扩展；Web 只提供 Owner-only 管理合同；Admin 通过生成客户端、严格 parser、URL keyset 和非乐观状态组合页面。

**技术栈：** C# `11.0`、.NET Framework `4.8`、Microsoft.Data.Sqlite/Dapper/DbUp、ASP.NET Web API 2/Katana、Microsoft.Extensions.DependencyInjection、xUnit、Vue `3.5` Composition API、TypeScript `6.0`、Vue Router、Pinia Colada、Valibot、Nuxt UI `4`、Vitest `4.1`、Hey API、pnpm `11`。

## 当前执行记录（2026-07-27）

- 本次已核实 Daily 奖励策略的持久化、API、`daily` 命令与 Owner 管理页接线：幂等键固定为 `ruleId` + `crossplatformId` + UTC `yyyyMMdd`；无规则或规则禁用时拒绝；外部标识固定为 `daily`。页面区分未配置、冲突、权限不足和不可用，并在冲突时保留草稿。
- 本次已核实 Community 后端完整 API 合同，以及基于 `expectedRowVersion` 的原子更新保护；奖励证据运行时已接入生产路径，并补充回归覆盖。Community Admin 现显示完整城市（含停用项）、好友记录、传送操作和投票轮次，同时保留按 ID/双方查询及 `ActionQueued` 子集；城市保存和投票结算成功后刷新相应全量列表。
- 新增 Daily 与 Community endpoint 后已重新执行 `pnpm api:schema`（`1/1`）与 `pnpm api:gen`，生成合同含 Daily 读写及 Community 全量查询。automation、Discord、GeoIP 的 i18n 文案缺口也已补齐。
- 最后聚焦验证为后端受影响过滤 `32/32`、Admin Daily/Community/i18n `37/37` 和 Admin typecheck；Vitest 退出码为 `0`，但保留既有 happy-dom teardown、fork worker 及外部连接噪声。Current 架构、设计和测试文档已提升实现事实。真实 7DTD 购买/传送/投票/未知结果验证、Playwright、publish、全量 Admin Vitest/ESLint/build 与 Windows/Linux smoke 仍未执行，不能标记为产品验收完成。

---

## 权威边界与执行前置

- 当前事实以[系统架构](../../architecture.md)为准；本计划及所链接 Target 文档只记录批准的实现路径，不构成当前实现证据。Daily UI、Community Admin 全量查询消费、Daily/Community endpoint 后的 OpenAPI/SDK 刷新、聚焦验证和 Current 文档提升已完成；剩余工作是取得真实游戏、浏览器、发布和跨平台环境证据，在此之前不得宣称产品验收完成。
- 页面信息架构与状态规则遵循[产品设计](../../design.md)，验证层级和真实环境门槛遵循[测试策略](../../test.md)。
- 目标依赖方向遵循[后端目标架构蓝图](../../architecture/backend-target-blueprint.md)、[Admin 前端目标架构蓝图](../../architecture/admin-frontend-target-blueprint.md)和[旧版本功能对齐目标蓝图](../../architecture/legacy-feature-parity-target-blueprint.md)。这些链接是 Current/Target 参考，不是第二个 primary dated design spec。
- 串行执行前确认第二波已提供 `LSTY.SevenDPanel.Domain`、持久作业/计划重启和有界 consumer，第三波已提供稳定玩家资料、目录重验、`GrantItemUseCase` 及可查询的玩家动作终态。若这些生产合同尚未存在，停止第四波实施；不得在本波复制前序实现或临时绕过。
- 仓库外旧项目 `7dtd-serveradmin` 只用于核对 `bal/pay/moneytop/daily/shop/buy/redeem`、`homes/sethome/delhome/home/cities/city/tpa/tpaccept/tpreject/back`、`votekick/voterestart` 的入口和字段语义。不得复制旧代码、DTO、页面、SQL 或其自由控制台命令奖励。
- 金额始终是非负 `long` 最小单位；玩家 `postedBalance - reservedDebit` 不得为负。系统账户允许负 `postedBalance`，但每个已提交事务的 Debit/Credit 总额必须相等。
- 不创建通用 repository、事件总线、脚本平台、万能 action/payload JSON、通用补偿工作流或只有测试消费者的抽象。奖励动作首版只登记第三波已有的 `GrantItem` 和 `ResetSkills`，不能接收控制台原文。
- 迭代时只运行当前任务列出的聚焦测试。任务 10 仅运行一次后端受影响聚合门禁和一次 Admin 受影响聚合门禁；真实 7DTD 只执行规格要求的一次购买发放、一次传送、一次投票动作及一次结果未知故障注入，不运行 Playwright 或 publish。
- 本计划不授权 `git commit`、`git push`、`git reset`、`git revert` 或其他 Git 操作；所有提交必须等待用户显式授权。
- 每日奖励配置作为 `011` 之后的向前 migration 实施，以保持已创建 SQLite 数据库的升级路径；不得回写历史 migration。

## 串行合流与并行边界

1. 任务 1 的 Domain 合同和 `011_EconomyCommunity.sql` migration 由主执行者串行完成。
2. 任务 2 的账本是任务 3～6 的共同依赖，必须先稳定。
3. 任务 3（grant）、任务 5（传送）和任务 6 的投票 Domain/Application 部分可并行；任务 4 依赖任务 3，投票动作接线依赖第二波计划重启和现有踢出。
4. 任务 7 的 runtime/DI 和任务 8 的 HTTP/OpenAPI/生成客户端均由主执行者串行合流。
5. 任务 9 的四个 Admin Feature 可由独立子代理并行，但路由、`AppShell.vue`、双语目录和生成文件只由一名集成者修改。

### 任务 1：固定 Domain 状态机与本波唯一 SQLite migration

**文件：**

- 新建：`backend/src/Core/LSTY.SevenDPanel.Domain/Economy/LedgerRules.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Domain/Rewards/GrantStateMachine.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Domain/Community/TeleportStateMachine.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Domain/Community/VoteStateMachine.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Migrations/011_EconomyCommunity.sql`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/EconomyCommunityDomainTests.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/EconomyCommunityMigrationTests.cs`

**固定状态：**

```csharp
public enum GrantOperationState { Reserved, Dispatching, PendingReconciliation, Completed, Failed, Refunded, Compensated }
public enum TeleportOperationState { Reserved, Dispatching, PendingReconciliation, Completed, Failed, Refunded }
public enum VoteRoundState { Open, Passed, Rejected, Expired, Cancelled, ActionQueued, ActionSucceeded, ActionFailed, ActionResultUnknown }
public enum LedgerSide { Debit, Credit }
```

**固定 schema：** `011` 依次创建 `economy_accounts`、`economy_transactions`、`economy_entries`、`economy_reservations`；`reward_packages`、`reward_package_entries`、`grant_operations`、`grant_operation_entries`、`shop_products`、`shop_purchases`、`redeem_codes`、`redeem_attempts`、`achievement_definitions`、`achievement_progress`、`online_reward_rules`、`reward_eligibilities`；`teleport_settings`、`player_homes`、`cities`、`friendships`、`friend_requests`、`teleport_operations`、`teleport_cooldowns`、`player_return_points`；`vote_configurations`、`vote_rounds`、`vote_eligible_players`、`vote_ballots`。它必须从第三波 `010_PlayerEvidenceActions.sql` 升级，并在末尾重新创建第一波 `unified_audit_projection`，把经济事务、grant、购买/兑换/奖励、传送和投票的稳定摘要作为专用来源加入查询，不复制兑换码、物品明细或敏感玩家数据。

```sql
CREATE UNIQUE INDEX ux_economy_transaction_idempotency ON economy_transactions(idempotency_key);
CREATE UNIQUE INDEX ux_reward_eligibility ON reward_eligibilities(rule_kind, rule_id, crossplatform_id, eligibility_key);
CREATE UNIQUE INDEX ux_redeem_player ON redeem_attempts(code_id, crossplatform_id, normalized_code_digest);
CREATE UNIQUE INDEX ux_vote_ballot ON vote_ballots(round_id, crossplatform_id);
```

- [x] **步骤 1：写纯规则和迁移失败测试**

  覆盖非负金额、平衡分录、玩家不可透支、系统账户可为负；grant/传送未知结果不能自动转为 Completed 或 Failed；投票只能结算一次。迁移测试断言所有表、外键、CHECK、唯一键、keyset 索引和 `PRAGMA foreign_key_check`，并重复执行 `SqliteDatabaseBootstrapper.Upgrade()`。

- [ ] **步骤 2：运行聚焦测试确认 RED**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~EconomyCommunityDomainTests|FullyQualifiedName~EconomyCommunityMigrationTests"
  ```

  预期：先因四个 Domain 类型和 `011` migration 缺失失败；不得接受只有编译缺失而没有规则断言的伪 RED。

- [ ] **步骤 3：实现最小状态转换和 schema 后转 GREEN**

  `reward_package_entries` 使用显式 `entry_kind` 与类型化列，不保存 payload JSON；`redeem_codes` 只保存 SHA-256 digest、masked prefix 和 normalization version；坐标保存 `world_id/x/y/z/yaw`；所有 UTC 存 Unix 毫秒。执行同一步骤 2 命令，预期全部通过且 migration 可重复。

### 任务 2：交付账户、双重记账、冻结、调整、转账与查询

**文件：**

- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Economy/EconomyModels.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Economy/IEconomyLedgerStore.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Economy/EconomyUseCases.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Economy/SqliteEconomyLedgerStore.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/EconomyLedgerUseCaseTests.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/SqliteEconomyLedgerStoreTests.cs`

**固定接口：**

```csharp
public interface IEconomyLedgerStore
{
    AccountSnapshot GetOrCreatePlayerAccount(string crossplatformId, string idempotencyKey, long openingAmount, DateTimeOffset occurredAtUtc);
    LedgerWriteResult Commit(LedgerTransactionDraft transaction);
    FundsReservationResult TryReserve(FundsReservationDraft reservation);
    LedgerWriteResult Capture(string reservationId, string transactionId, string idempotencyKey, DateTimeOffset occurredAtUtc);
    bool Release(string reservationId, DateTimeOffset occurredAtUtc);
    AccountPage QueryAccounts(AccountKeysetQuery query);
    TransactionPage QueryTransactions(TransactionKeysetQuery query);
}
```

`LedgerTransactionDraft` 固定包含 `transactionId/type/idempotencyKey/occurredAtUtc/actorKind/actorId/relatedCrossplatformId/businessKind/businessId/correlationId/reason/entries`；条目固定为 `accountId/side/amount`。游标分别按 `(posted_balance DESC, account_id ASC)` 和 `(occurred_utc DESC, transaction_id DESC)`。

- [x] **步骤 1：写用例与并发 Store 失败测试**

  覆盖首次开户只发行一次、余额查询、冻结阻止玩家支出/转账但允许 Owner 补偿和退款、正负调整使用 `system:issuance`/`system:recovery`、转账双边原子更新、同幂等键返回同结果、并发透支仅一方成功、事务回滚不留下账户/分录/预留、交易与排行榜稳定 keyset。

- [ ] **步骤 2：确认 RED 后实现短事务**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~EconomyLedgerUseCaseTests|FullyQualifiedName~SqliteEconomyLedgerStoreTests"
  ```

  `SqliteEconomyLedgerStore` 每个写方法只打开一个短连接和一个 `BeginTransaction(deferred: false)`；先验证借贷和幂等，再条件更新 `posted_balance/reserved_debit`、插入事务与分录并提交。任何代码不得直接更新余额而不写平衡分录。

- [ ] **步骤 3：转 GREEN 并验证账本总和**

  重新运行步骤 2 命令。预期测试通过，并断言所有 committed transaction 的 Debit 总和等于 Credit 总和，所有玩家 `posted_balance >= reserved_debit >= 0`。

### 任务 3：交付奖励包、grant operation、核对、退款与显式补偿

**文件：**

- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Rewards/RewardModels.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Rewards/IRewardStore.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Rewards/IRewardDeliveryPort.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Rewards/RewardUseCases.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Rewards/SqliteRewardStore.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Rewards/ThirdWaveRewardDeliveryAdapter.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/RewardGrantUseCaseTests.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/SqliteRewardStoreTests.cs`

**固定接口：**

```csharp
public interface IRewardDeliveryPort
{
    Task<RewardDeliveryResult> DeliverAsync(RewardDeliveryCommand command, CancellationToken cancellationToken);
}

public sealed class RewardDeliveryCommand
{
    public string GrantOperationId { get; }
    public string CrossplatformId { get; }
    public int ExpectedEntityId { get; }
    public string ExpectedWorldId { get; }
    public IReadOnlyList<ResolvedRewardEntry> Entries { get; }
}
```

- [x] **步骤 1：写配置、执行与恢复失败测试**

  覆盖包名/描述/启停/排序；Item 保存 `internalName/kind/quantity/minQuality/maxQuality/catalogVersion`，Currency 保存非负 `long`，RegisteredAction 只接受 `ResetSkills`。保存和执行均通过当前 `IGameResourceCatalog` 重验内部名；重复资格键只创建一个 grant；第三波动作 Completed 后才完成，ResultUnknown 或终态写失败进入 `PendingReconciliation`，重启扫描不自动重发。

- [ ] **步骤 2：确认 RED 后实现 Store 与第三波 adapter**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~RewardGrantUseCaseTests|FullyQualifiedName~SqliteRewardStoreTests"
  ```

  `ThirdWaveRewardDeliveryAdapter` 逐项调用第三波公开的 `GrantItemUseCase` 或 `ResetPlayerSkillsUseCase`，保存各自 operation ID 并查询真实终态；不得直接操作 `ItemStack`、拼接控制台命令或为未知结果重试。货币只在全部游戏副作用确认后通过任务 2 账本发行。

- [ ] **步骤 3：实现人工确认、退款和补偿并转 GREEN**

  人工确认只能把 `PendingReconciliation` 改为 Completed 并记录 actor/correlation；退款创建新账本 transaction；补偿创建新的 `grantOperationId`、`compensatesOperationId` 和幂等键。重新运行步骤 2 命令，预期中间态、重启恢复和补偿链测试通过。

### 任务 4：交付商店、兑换码、成就与在线奖励

**文件：**

- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Commerce/CommerceModels.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Commerce/ICommerceStore.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Commerce/CommerceUseCases.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Rewards/AchievementAndOnlineRewardUseCases.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Commerce/SqliteCommerceStore.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Inbound/Rewards/RewardEvidenceRuntime.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/CommerceRewardUseCaseTests.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/SqliteCommerceConcurrencyTests.cs`

**固定规则：** 商品库存 `null` 表示无限，否则为非负 `long`；每玩家限制为可空正整数。兑换码由服务端生成 `XXXX-XXXX-XXXX-XXXX`，规范化为去连字符的大写 ASCII，SQLite 只保存 SHA-256 digest 和末四位 masked prefix。成就规则只允许 `Level`、`ZombieKills`、`PlayerKills`、`Deaths` 四个已批准统计键；在线奖励只消费第一/三波可确认会话与累计观察证据。

- [x] **步骤 1：写购买和兑换并发失败测试**

  覆盖商品禁用、账户冻结、有限/无限库存、每玩家限制、并发最后一件仅一单 Reserved、支付与库存同事务预留、grant 成功后 capture、游戏副作用前失败释放资金并回补库存、未知结果保留预留；兑换过期/禁用/全局次数/单玩家次数、并发同码同玩家只有一条权威结果，API 查询永不返回 digest 或明文。

- [x] **步骤 2：写成就与在线资格失败测试**

  覆盖固定规则进度单调、稳定 eligibility key、防事件重投；在线会话开放边界、gap 相交、关服恢复与手动补发，gap 时规则按配置 `Paused` 或 `Incomplete`，绝不把未知时间计入阈值。

- [x] **步骤 3a：接入奖励证据运行时并补充回归覆盖**

  `RewardEvidenceRuntime` 已接入生产路径，只订阅第一/三波已有的标量事件/会话入口并投递第二波持久工作；已补充对应回归覆盖，不新增静态事件总线或逐事件 `Task.Run`。

- [ ] **步骤 3b：确认 RED 后实现原子预留**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~CommerceRewardUseCaseTests|FullyQualifiedName~SqliteCommerceConcurrencyTests"
  ```

  购买、兑换、成就、在线奖励最终都调用任务 3 的 `StartGrantOperationUseCase`。

- [ ] **步骤 4：原子预留转 GREEN**

  重新运行步骤 3 命令。预期购买/兑换并发、资格重投、重启恢复和 ResultUnknown 全部通过，且不存在负库存或重复 grant。

### 任务 5：交付家、城市、好友、返回点与费用传送

**文件：**

- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Community/CommunityModels.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Community/ICommunityStore.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Community/ICommunityGameGateway.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Community/TeleportUseCases.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Community/SqliteCommunityStore.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Community/SevenDaysCommunityGameGateway.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/CommunityTeleportUseCaseTests.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/SevenDaysCommunityGameGatewayTests.cs`

**固定接口：**

```csharp
public interface ICommunityGameGateway
{
    Task<TeleportActionResult> TeleportAsync(TeleportActionCommand command, CancellationToken cancellationToken);
}

public sealed class TeleportActionCommand
{
    public string OperationId { get; }
    public string CrossplatformId { get; }
    public int ExpectedEntityId { get; }
    public string ExpectedWorldId { get; }
    public WorldPosition Destination { get; }
}
```

- [x] **步骤 1：写家/城市/好友/返回点和规则失败测试**

  覆盖命名、每玩家家数量、同世界、启用城市、好友邀请/接受/拒绝/移除、目标在线且明确允许、独立/全局冷却、世界边界、血月、费用、死亡/未生成/身份变化。返回点只在一次已确认传送前保存出发坐标，失败、断线、死亡和未知动作都不能覆盖。

- [ ] **步骤 2：确认 RED 后实现 Application 预检与原子意图**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~CommunityTeleportUseCaseTests|FullyQualifiedName~SevenDaysCommunityGameGatewayTests"
  ```

  Application 固定目标后创建 `teleport_operations`，按需调用 `IEconomyLedgerStore.TryReserve`。SevenDays Adapter 在 `GameThreadDispatcher` 内重新匹配 `ClientInfo.entityId` 与 `CrossplatformId.CombinedString`、世界、边界和状态，使用 `NetPackageTeleportPlayer.Setup(destination, viewDirection, false)` 发送固定坐标包；不调用控制台 teleport。

- [ ] **步骤 3：完成扣款、冷却和未知状态后转 GREEN**

  只有 Adapter 确认成功才 capture 费用并更新冷却/返回点；副作用前失败 release；超时、断线或结果不可证明进入 `PendingReconciliation`。重新运行步骤 2 命令，预期目标替换、血月、并发冷却、退款和未知结果测试通过。

### 任务 6：交付持久投票、登记式游戏命令和动作结果分离

**文件：**

- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Community/VoteUseCases.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Community/GameCommandConsumers.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Community/SqliteVoteStore.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Inbound/Community/CommunityCommandRuntime.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Community/CommunityVoteActionAdapter.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/CommunityVoteUseCaseTests.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/CommunityGameCommandTests.cs`

**固定命令：** `bal`（别名 `balance/money`）、`pay`（`transfer/send`）、`moneytop`（`baltop/ecotop`）、`daily`（`claim`）、`shop`、`buy`、`redeem`、`homes`、`sethome`、`delhome`、`home`、`cities`、`city`、`tpa`、`tpaccept`、`tpreject`、`back`、`votekick`、`voterestart`。投票命令只接受启动参数或 `yes|y|no|n`。

- [x] **步骤 1：写资格、投票和唯一结算失败测试**

  覆盖启停、发起/参与资格、固定发起者与目标、持续时间、阈值百分比 `1..100`、最小参与数、发起者/目标/全局冷却、互斥 scope、每轮每人一票、配置允许时最多变更一次、截止与并发结算只有一个 winner。启动时保存合格玩家快照，不按截止时名称或 entity ID 重新推导身份。

- [x] **步骤 2：写命令路由失败测试**

  每个命令必须调用一个实际启用的 Application consumer；未知、未启用、参数非法和权限拒绝只私发稳定结果码。测试断言命令处理器不接收/执行 SQL、路径、通用 JSON、脚本或控制台原文。

- [ ] **步骤 3：确认 RED 后实现投票与动作接线**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~CommunityVoteUseCaseTests|FullyQualifiedName~CommunityGameCommandTests"
  ```

  踢出投票通过后创建现有 `KickPlayerUseCase` 的固定身份动作意图；重启投票通过后创建第二波 `ScheduledRestart` 持久作业。`Passed` 只表示结算通过，后续分别写 `ActionSucceeded/ActionFailed/ActionResultUnknown`，不得把投票结果当作动作成功。

- [ ] **步骤 4：转 GREEN**

  重新运行步骤 3 命令。预期重复结算、重启恢复、目标离线、已存在重启、动作失败和未知结果均保留专用状态。

### 任务 7：串行合流 runtime、恢复扫描和依赖注入

**文件：**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Runtime/Community/EconomyCommunityRuntime.cs`
- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- 修改：`backend/src/Core/LSTY.SevenDPanel.Application/LSTY.SevenDPanel.Application.csproj`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/LSTY.SevenDPanel.Adapters.Persistence.Sqlite.csproj`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/DependencyInjectionTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/DependencyRulesTests.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/EconomyCommunityRuntimeTests.cs`

- [ ] **步骤 1：写组合与生命周期失败测试**

  断言 Domain 无产品引用；Application 只依赖 Domain；Web 不引用 Persistence/SevenDays；同一 Store 实例映射到各端口；启动先 migration，再恢复 `Reserved/Dispatching/Open` 记录，再接收命令；关闭先注销命令/事件生产者，再停止第二波 consumer，最后停止 inner runtime。恢复扫描只能查询状态，不重放 `PendingReconciliation`。

- [ ] **步骤 2：确认 RED 后注册对象图**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~EconomyCommunityRuntimeTests|FullyQualifiedName~DependencyInjectionTests|FullyQualifiedName~DependencyRulesTests"
  ```

  在 `PanelServiceProviderFactory` 显式注册四个 Store、用例、gateway、命令 consumer 和一个 `EconomyCommunityRuntime` 装饰层；复用第二波队列/作业与第三波动作，禁止新增 service locator、程序集扫描或第二套后台队列。

- [ ] **步骤 3：转 GREEN 并验证关闭顺序**

  重新运行步骤 2 命令。预期 `ValidateOnBuild/ValidateScopes` 通过，两个停止分支都尝试且异常聚合，晚到事件不创建新意图。

### 任务 8：串行交付 Owner-only HTTP、OpenAPI 和生成客户端

**文件：**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/CommerceController.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/CommerceHttpModels.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/RewardsController.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/RewardsHttpModels.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/AchievementsController.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/AchievementsHttpModels.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OnlineRewardsController.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OnlineRewardsHttpModels.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/CommunityController.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/CommunityHttpModels.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/CommerceRewardHttpTests.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/CommunityHttpTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostOpenApiSnapshotTests.cs`
- 修改：`frontend/apps/admin/openapi/7dpanel.v1.json`
- 修改：`frontend/apps/admin/src/shared/api/generated/`

**固定路由组：** `/api/v1/economy/accounts|transactions|leaderboard`，账户子路由 `freeze|adjust`；`/api/v1/reward-packages|grant-operations`，grant 子路由 `confirm|refund|compensate`；`/api/v1/shop/products|purchases`、`/api/v1/redeem-codes|redemptions`、`/api/v1/achievements/definitions|records`、`/api/v1/online-rewards/rules|records`；`/api/v1/community/teleport-settings|homes|cities|friendships|teleport-operations|vote-configurations|vote-rounds`。

- [x] **步骤 1：写 HTTP 角色和合同失败测试**

  对每组覆盖匿名 401、`Admin`/`Viewer` 403、`Owner` 200/201/202/204；非法金额/库存/规则/坐标/游标 400；缺失 404；版本/并发冲突 409；游戏未就绪 503。成功 DTO 使用 camelCase/UTC/nullable，列表使用 opaque keyset；兑换创建响应 `Cache-Control: no-store` 且只显示一次明文，后续 DTO 不含 digest、完整代码、SQL、路径或异常。

- [x] **步骤 2a：实现 Community 独立 DTO、Problem Details 和并发合同**

  Community 后端完整 API 合同已实现；Controller 只做 Owner 授权、DTO 映射和稳定错误码，不持有 SQLite transaction，不筛选游戏集合，也不把 HTTP 202 表述为完成。需要版本保护的更新在底层以 `expectedRowVersion` 执行原子条件更新。

- [ ] **步骤 2b：确认 RED 后完成其余资源组的独立 DTO 和 Problem Details**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~CommerceRewardHttpTests|FullyQualifiedName~CommunityHttpTests"
  ```

  其余资源组同样只由 Controller 完成 Owner 授权、DTO 映射和稳定错误码；不持有 SQLite transaction，不筛选游戏集合，也不把 HTTP 202 表述为完成。

- [ ] **步骤 3：刷新 OpenAPI 并生成客户端（此前基线已完成；当前合同刷新待执行）**

  ```powershell
  $env:SEVENDPANEL_UPDATE_ADMIN_OPENAPI_SNAPSHOT = "1"
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~OwinWebHostTests.Openapi_document_matches_admin_codegen_snapshot"
  Remove-Item Env:SEVENDPANEL_UPDATE_ADMIN_OPENAPI_SNAPSHOT
  Set-Location frontend/apps/admin
  pnpm api:gen
  Set-Location ../../..
  ```

  此前的 OpenAPI snapshot 与 `pnpm api:gen` 已完成，作为当前基线；新增 Daily 与 Community endpoint 后，仍须重新执行以上命令。刷新后预期：operationId 全局唯一，所有管理 operation 带 Bearer、Owner 403、Problem Details 和 keyset schema；生成目录只由 `pnpm api:gen` 修改，不手改。

- [ ] **步骤 4：转 GREEN**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~CommerceRewardHttpTests|FullyQualifiedName~CommunityHttpTests|FullyQualifiedName~OwinWebHostTests.Openapi_document_matches_admin_codegen_snapshot"
  ```

  预期：HTTP 合同与运行时 OpenAPI snapshot 测试全部通过，工作树中的 snapshot 和生成客户端对应同一份合同。

### 任务 9：并行实现四个 Admin Feature，再串行接入路由和双语

**文件：**

- 新建：`frontend/apps/admin/src/features/economy/api/economy.ts`
- 新建：`frontend/apps/admin/src/features/economy/model/useEconomy.ts`
- 新建：`frontend/apps/admin/src/features/economy/ui/EconomyAccountsView.vue`
- 新建：`frontend/apps/admin/src/features/economy/ui/EconomyTransactionsView.vue`
- 新建：`frontend/apps/admin/src/features/economy/index.ts`
- 新建：`frontend/apps/admin/src/features/rewards/api/rewards.ts`
- 新建：`frontend/apps/admin/src/features/rewards/model/useRewards.ts`
- 新建：`frontend/apps/admin/src/features/rewards/ui/RewardPackagesView.vue`
- 新建：`frontend/apps/admin/src/features/rewards/ui/RewardOperationsView.vue`
- 新建：`frontend/apps/admin/src/features/rewards/index.ts`
- 新建：`frontend/apps/admin/src/features/commerce/api/commerce.ts`
- 新建：`frontend/apps/admin/src/features/commerce/model/useCommerce.ts`
- 新建：`frontend/apps/admin/src/features/commerce/ui/ShopProductsView.vue`
- 新建：`frontend/apps/admin/src/features/commerce/ui/RedeemCodesView.vue`
- 新建：`frontend/apps/admin/src/features/commerce/ui/AchievementOnlineRewardsView.vue`
- 新建：`frontend/apps/admin/src/features/commerce/index.ts`
- 新建：`frontend/apps/admin/src/features/community/api/community.ts`
- 新建：`frontend/apps/admin/src/features/community/model/useCommunity.ts`
- 新建：`frontend/apps/admin/src/features/community/ui/TeleportSettingsView.vue`
- 新建：`frontend/apps/admin/src/features/community/ui/CitiesView.vue`
- 新建：`frontend/apps/admin/src/features/community/ui/VoteConfigurationView.vue`
- 新建：`frontend/apps/admin/src/features/community/index.ts`
- 新建：上述四个 Feature 中与每个 `api/model/ui` 文件同目录的 `.test.ts`
- 新建：`frontend/apps/admin/src/pages/economy/accounts.vue`
- 新建：`frontend/apps/admin/src/pages/economy/transactions.vue`
- 新建：`frontend/apps/admin/src/pages/economy/reward-packages.vue`
- 新建：`frontend/apps/admin/src/pages/economy/reward-operations.vue`
- 新建：`frontend/apps/admin/src/pages/economy/shop.vue`
- 新建：`frontend/apps/admin/src/pages/economy/redeem-codes.vue`
- 新建：`frontend/apps/admin/src/pages/economy/achievement-online-rewards.vue`
- 新建：`frontend/apps/admin/src/pages/community/teleport.vue`
- 新建：`frontend/apps/admin/src/pages/community/cities.vue`
- 新建：`frontend/apps/admin/src/pages/community/votes.vue`
- 修改：`frontend/apps/admin/src/features/game-resources/index.ts`
- 新建：`frontend/apps/admin/src/features/game-resources/ui/GameResourcePicker.vue`
- 修改：`frontend/apps/admin/src/app/AppShell.vue`
- 修改：`frontend/apps/admin/src/app/AppShell.test.ts`
- 修改：`frontend/apps/admin/src/app/router.test.ts`
- 修改：`frontend/apps/admin/src/app/i18n/locales/zh-CN.json`
- 修改：`frontend/apps/admin/src/app/i18n/locales/en.json`
- 修改：`frontend/apps/admin/src/app/i18n/messages.test.ts`

- [ ] **步骤 1：写 parser、composable 和组件失败测试并确认 RED**

  四个 API parser 拒绝额外敏感字段、非法 `long`、非法状态、无效 UTC/cursor；金额以十进制字符串跨 JSON 边界并在 Feature 内解析为 `bigint`，使用 `Intl.NumberFormat` 按语言显示但不转浮点。composable 使用 readonly `shallowRef`、single-flight、AbortController 和最后成功页 Stale；Mutation 无自动重试和乐观成功。

  ```powershell
  Set-Location frontend/apps/admin
  pnpm test -- src/features/economy src/features/rewards src/features/commerce src/features/community src/features/game-resources/ui/GameResourcePicker.test.ts
  Set-Location ../../..
  ```

  预期：先因四个 Feature、`GameResourcePicker` 及其行为缺失而失败，且失败断言覆盖 parser、并发请求和页面状态，不以单纯导入失败代替行为 RED。

- [ ] **步骤 2：实现表单和确认边界**

  使用 `UForm/UFormField/UInputNumber/USelect/USwitch/UTable/UModal/USlideover`；表单 DTO 与响应模型分离。奖励包物品通过 `GameResourcePicker` 选择并保存 `internalName/kind/catalogVersion`；该选择器是游戏资源 Feature 的公开 API，不共享 `useGameResources` 页面状态。桌面表格从 `md` 显示，窄屏使用单列条目。

- [ ] **步骤 3：运行四个 Feature 聚焦测试转 GREEN**

  ```powershell
  Set-Location frontend/apps/admin
  pnpm test -- src/features/economy src/features/rewards src/features/commerce src/features/community src/features/game-resources/ui/GameResourcePicker.test.ts
  Set-Location ../../..
  ```

  预期：Loading/Fresh/Empty/Partial/Stale/Forbidden/Queued/Running/Succeeded/Failed/Unknown 均有黑盒断言；确认对话框固定显示目标、金额/库存/坐标/规则和后果。

- [x] **步骤 4：串行接入 Owner-only 路由、导航、搜索和双语**

  所有新 route meta 固定 `{ requiresAuth: true, roles: ['Owner'] }`；`AppShell` 增加“经济与奖励”“商店与兑换”“传送与投票”分组并复用同一 child 集合给侧栏和 Dashboard Search。`Admin`/`Viewer` 无入口且深链接被 guard 拒绝；服务端仍独立 403。两个 locale 的 `economy.*`、`rewards.*`、`commerce.*`、`community.*` key 完全一致且不含 HTML。

- [ ] **步骤 5：运行路由和双语聚焦测试**

  ```powershell
  Set-Location frontend/apps/admin
  pnpm test -- src/app/AppShell.test.ts src/app/router.test.ts src/app/i18n/messages.test.ts src/pages/economy src/pages/community
  Set-Location ../../..
  ```

  预期：Owner 可达全部十个页面，Admin/Viewer 无导航且守卫拒绝，`zh-CN`/`en` 文案和窄屏组件断言通过。

### 任务 10：执行一次聚合门禁、一次真实窄验证并提升 Current 文档

**文件：**

- 修改：`docs/architecture.md`
- 修改：`docs/design.md`
- 修改：`docs/test.md`
- 修改：`docs/architecture/legacy-feature-parity-target-blueprint.md`
- 修改：`docs/architecture/backend-target-blueprint.md`
- 修改：`docs/architecture/admin-frontend-target-blueprint.md`
- 按实际命令或入口变化评估后修改：`backend/README.md`
- 按实际命令或入口变化评估后修改：`frontend/apps/admin/README.md`
- 按实际聚合入口变化评估后修改：`README.md`
- 更新：本计划的完成勾选与实际验证结果

- [ ] **步骤 1：运行一次后端受影响聚合门禁**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~Economy|FullyQualifiedName~Reward|FullyQualifiedName~Commerce|FullyQualifiedName~Community|FullyQualifiedName~DependencyInjectionTests|FullyQualifiedName~DependencyRulesTests|FullyQualifiedName~Openapi_document_matches_admin_codegen_snapshot"
  ```

  预期：Domain、`011` SQLite migration/并发、Application、SevenDays、runtime/DI 和 HTTP/OpenAPI 受影响集合全部通过；这是稳定后的唯一后端聚合运行。

- [ ] **步骤 2：运行一次 Admin 受影响聚合门禁**

  ```powershell
  Set-Location frontend/apps/admin
  pnpm test -- src/features/economy src/features/rewards src/features/commerce src/features/community src/app/AppShell.test.ts src/app/router.test.ts src/app/i18n/messages.test.ts src/pages/economy src/pages/community
  pnpm typecheck
  pnpm exec eslint src/features/economy src/features/rewards src/features/commerce src/features/community src/pages/economy src/pages/community src/app/AppShell.vue
  pnpm build
  Set-Location ../../..
  ```

  预期：聚焦 Vitest、类型检查、定向 lint 和生产构建通过；不重复全量 Vitest/lint，不运行 Playwright。

- [ ] **步骤 3：执行规格要求的一次真实 7DTD 窄验证**

  在受控 `v3.0.1-b4` 测试服只执行：一笔有限库存购买并确认物品与账本；一次收费传送并确认身份/到达/返回点；一次投票通过并区分投票终态与动作终态；一次在物品发放后切断结果观察以确认 `PendingReconciliation` 且不会自动重发。不得执行 publish、浏览器 smoke、完整重置或破坏性世界操作。

- [x] **步骤 4：按实际代码和验证提升 Current 文档**

  `docs/architecture.md` 记录已实现的账本/Store/状态机、前序动作复用、runtime/DI、HTTP 和 Admin 边界；`docs/design.md` 记录实际导航、表单、确认、keyset 与窄屏；`docs/test.md` 记录命令、数量、真实窄验证和未执行门禁。三份 Target blueprint 只把已验证条目标为采用并保留证据缺口；README 仅在真实入口或所属命令变化时更新，不复制测试策略。

- [ ] **步骤 5：完成无 Git 的计划自查**

  ```powershell
  $plan = 'docs/superpowers/plans/2026-07-26-legacy-parity-economy-community.md'
  (Select-String -LiteralPath $plan -Pattern '\]\(\.\./specs/.*-design\.md\)').Count
  ```

  预期：primary dated design spec Markdown 链接计数为 `1`，全文没有未定稿标记或省略式步骤。逐节核对规格的“经济账户与双重记账、商品/奖励包/发放、兑换/成就/在线奖励、家/城市/好友/返回点、社区投票、游戏命令/Admin、权限审计、非目标、精简验证、完成条件”均映射到任务 1～10；所有状态名、接口参数、表名和路由前后一致。

## 完成标准

- 每笔余额变化有平衡不可变分录；玩家不可透支，冻结、调整、转账、排行榜和 keyset 交易查询并发安全。
- 奖励包、商店、兑换、成就和在线奖励以稳定资格/幂等键驱动；失败、未知、退款和补偿不会重复扣款、回补库存或发放。
- 家、城市、好友和返回点传送在 Application 与游戏主线程双重重验身份、世界、边界、冷却、血月、费用和玩家状态。
- 踢出/重启投票持久保存资格、选票、结算和后续动作结果；通过不等于动作成功，结算不会重复。
- 游戏命令只注册同波真实消费者；Admin/API 仅 Owner 可见，玩家不登录网页，任何入口都不接受 SQL、路径、控制台原文、脚本或万能 JSON。
- 后端/Admin 各一次受影响聚合门禁及规格要求的真实窄验证达到任务 10 记录结果；未运行的 Playwright、publish 与额外 smoke 明确记录。
- Git 提交、推送或其他历史操作仍需用户另行显式授权。
