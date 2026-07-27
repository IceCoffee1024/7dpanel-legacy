---
state: Current
document_role: Implementation Plan
primary_spec: ../specs/2026-07-26-legacy-parity-automation-integrations-design.md
last_updated: "2026-07-26"
---

# 旧版本功能对齐第五波：事件自动化、Discord 与 GeoIP 实施计划

## 当前执行记录（2026-07-26）

- 收口口径：本次只依据 CodeGraph 核对到的当前代码和此前已保存的执行结果更新状态；本次未运行命令、测试或 Git。步骤只要仍包含未执行验证、未实现子项或真实环境动作，就保持未勾选。
- 已存在代码：`012_AutomationIntegrations.sql`；Automation Domain/Application/SQLite、trigger runtime、execution/recovery 和类型化 action dispatcher；Discord store、官方 API client 和持久 delivery worker；GeoIP policy/store/provider/join adapter；Web Controllers/OpenAPI；生产 DI/runtime；Admin 实际使用 `features/automation`、`features/discord`、`features/geoip` 和 `/automation`、`/integrations/discord`、`/integrations/geoip`，任务 9～11 的文件清单已按当前路径校正。
- 当前生产合同：Automation execution 查询由 SQLite Store 实现并被 HTTP Controller 消费；Discord delivery/binding 查询由生产 Store、HTTP API 和 Admin composable 消费。Admin 的 `unavailable` 状态只表示一次请求或可选能力失败，不再作为后端合同缺失证据。
- 已保存执行证据：Automation HTTP `11/11`、Discord HTTP `15/15`、对应 Admin parser `3/3` 曾通过；Bootstrap Release build 为 `0 warning`、`0 error`；`pnpm api:schema` 为 `1/1`，`pnpm api:gen` 成功。Bootstrap build 不替代第五波后端聚合测试。
- 未完成代码边界：尚无 Discord Gateway 网络连接和 interaction Ed25519 transport；奖励、成就、在线奖励 evidence runtime 尚未接入生产观察源。
- 未验证边界：Admin 最终 typecheck 以及 AppShell/router/i18n 聚焦组合已通过，Automation/Discord/GeoIP parser 也有 `3/3` 证据；三组 Feature 页面组件的完整聚焦组合、GeoIP 真实加入决策、Discord sandbox、MaxMind、7DTD、Playwright、publish 均无通过证据。

> **面向智能体执行者：** 必须使用 `superpowers:subagent-driven-development`（推荐）或 `superpowers:executing-plans`，按任务顺序实施并用 checkbox（`- [ ]`）跟踪。任务 1、2、8、12 是串行合流点；任务 3～7 在其依赖合同稳定后可分派，任务 9～11 可并行开发后交由任务 12 统一验证。

**对应规格：** [旧版本功能对齐第五波：事件自动化、Discord 与 GeoIP 设计规格](../specs/2026-07-26-legacy-parity-automation-integrations-design.md)

**目标：** 交付只消费既有类型化能力的固定自动化规则、无副作用 dry-run、可恢复幂等执行、Discord Webhook/Bot/绑定/Slash/聊天桥与 GeoIP 加入策略，使 `CAP-04`、`CAP-05`、`CAP-11` 和 `NFR-01`～`NFR-05` 具有可查询、可诊断且不泄露 Secret 的完整纵向证据。

**架构：** Domain 只拥有条件三值逻辑、冷却和并发不变量；Application 拥有规则、执行、Discord 与 GeoIP 用例和类型化端口；SQLite 保存配置、trigger snapshot、逐动作结果、delivery、绑定、缓存和决策；SevenDays Adapter 只在回调复制标量并非阻塞投递；第二波创建的 Local Adapter 负责 Discord 官方 API、签名、Gateway 和 MaxMind 本地/外部查询；Web 与 Admin 只映射安全 DTO 和结构化表单，不新增第五个 Adapter 项目。

**技术栈：** C# `11.0`、.NET Framework `4.8`、Microsoft.Data.Sqlite/Dapper/DbUp、System.Threading.Channels、System.Text.Json、MaxMind.GeoIP2 `6.1.0`、Chaos.NaCl.Standard `1.0.0`、ASP.NET Web API 2/Katana、Discord API v10、Vue `3.5`、TypeScript `6.0`、Nuxt UI `4`、Valibot、Vite `8`、Vitest、Hey API、pnpm `11`。

**权威边界：** 产品合同见[产品需求](../../PRD.md)，当前 UI 见[产品设计](../../design.md)，当前实现见[系统架构](../../architecture.md)，验证策略见[测试策略](../../test.md)；目标方向分别见[旧版本功能对齐目标蓝图](../../architecture/legacy-feature-parity-target-blueprint.md)、[后端目标架构蓝图](../../architecture/backend-target-blueprint.md)和[Admin 前端目标架构蓝图](../../architecture/admin-frontend-target-blueprint.md)。这些 Current/Target 文档不是第二份 primary dated spec。

## 全局执行约束

- 执行本计划所需的第一至第四波游戏事件/gap、持久 scheduler/Cron、公告、玩家动作/禁言、受限命令、经济调整和可恢复 grant 生产合同已经存在；奖励、成就、在线奖励 evidence runtime 尚未接入生产观察源，不用测试 Stub 或未接线 runtime 冒充生产证据。
- SQLite migration 编号为 `012_AutomationIntegrations.sql`；它只在 `011_EconomyCommunity.sql` 已存在且迁移账本无冲突时执行。迁移、Domain 类型、DI/runtime 顺序、OpenAPI snapshot 和生成客户端由主执行者串行合流。
- 旧 backend `277996d` 与旧 frontend `60fc816` 仅用于核对入口、字段和失败表现；不得复制其 JSON 规则引擎、代码、DTO、页面或数据库模型。新规则不保存表达式、SQL、URL 请求体、任意命令原文或脚本。
- `AutomationTriggerType` 固定为 `PlayerJoined`、`PlayerLeft`、`ChatMessage`、`Cron`、`BloodMoonPhaseEntered`；动作固定为 `BroadcastMessage`、`PrivateMessage`、`Announcement`、`GrantItem`、`GrantRewardPackage`、`AdjustEconomy`、`KickPlayer`、`MutePlayer`、`RestrictedCommand`、`DiscordMessage`。
- 条件字段采用显式目录和 `Known/Unknown` 三值；最大树深 `5`、节点 `64`、单字符串 `256`、集合 `50`。没有值的条件结果是 `Unknown` 且默认不匹配，不转为空字符串、`0` 或 `false`。
- Discord Slash 固定目录为 `/serverstatus`、`/listplayers`、`/help`、`/bind`、`/unbind`；远程管理 allow-list 只可选前三项，绑定命令不执行管理动作。Webhook 使用 `wait=true`，消息不产生 mention；HTTP Interaction 校验 Ed25519 签名、时间窗、应用、Guild、频道和成员权限。
- GeoIP provider 固定为 `LocalMmdb` 或 `MaxMindWebService`，不接受浏览器提交 URL/路径；本地 MMDB 路径只来自服务器自有配置，外部凭据写入专用 SQLite Secret 列。默认 `FailOpen`，任何改默认值的产品变更不在本计划内。
- Vue 页面全部使用 Composition API、`<script setup lang="ts">`、薄 route page、Feature composable 单一状态源和 props-down/events-up；不把三套配置复制进全局 Pinia，不提供 JSON/脚本编辑器，不用 snapshot-only 测试。
- 迭代只运行当前任务聚焦测试。任务 12 稳定后后端和 Admin 各执行一次受影响聚合门禁；不运行全量 Playwright。规格要求的真实证据合并为一次 Discord sandbox 往返和一次受控 GeoIP 加入测试，并只发布/启动一个候选周期。
- 本计划不授权 `git commit`、`git push`、`git reset` 或 `git revert`；所有检查点保持未提交，Git 历史操作必须由用户另行明确授权。

---

### 任务 1：锁定前置合同并建立自动化 Domain 不变量

**文件：**

- 新建：`backend/src/Core/LSTY.SevenDPanel.Domain/Automations/AutomationRule.cs`、`AutomationCondition.cs`、`AutomationExecutionPolicy.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/AutomationDomainTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/DependencyRulesTests.cs`

**固定类型：**

```csharp
public enum AutomationTruth { Matched, NotMatched, Unknown }
public enum AutomationConditionOperator { Equals, NotEquals, InSet, NumberRange, TimeWindow, PlayerGroup, Permission, Cooldown }
public enum AutomationCooldownScope { Rule, RulePlayer }
public enum AutomationConcurrencyPolicy { SkipIfRunning, QueueOne }
public enum AutomationFailurePolicy { StopOnFailure, Continue }
```

- [ ] **步骤 1：写 Domain RED**

  覆盖深度/节点/字符串/集合上限、`Unknown` 传播、数值闭区间、跨午夜时间窗口、规则/玩家冷却键、`SkipIfRunning` 跳过、`QueueOne` 最多保留一项和动作失败策略。运行：

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~AutomationDomainTests"
  ```

  预期：先因 Domain 类型和策略缺失失败；不得把编译失败当作行为 RED，先补抛出 `NotImplementedException` 的精确签名，再确认断言失败。

- [x] **步骤 2：实现最小纯 Domain 逻辑**

  `AutomationRule` 只保存 ID、版本、名称、启停、trigger、condition root、顺序 actions、cooldown duration/scope、concurrency、failure policy 和时间；不引用 Application、SQLite、Discord、7DTD 或 JSON 库。

- [ ] **步骤 3：转 GREEN 并验证依赖方向**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~AutomationDomainTests|FullyQualifiedName~DependencyRulesTests"
  ```

  预期：Domain 行为与项目依赖测试通过；没有通用事件总线、反射 registry 或脚本抽象。

### 任务 2：串行落地 migration 与能力专属 SQLite Store

**文件：**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Migrations/012_AutomationIntegrations.sql`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Automations/IAutomationStore.cs`、`Discord/IDiscordIntegrationStore.cs`、`GeoIp/IGeoIpAccessPolicyStore.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/SqliteAutomationStore.cs`、`SqliteDiscordIntegrationStore.cs`、`SqliteGeoIpAccessPolicyStore.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/SqliteAutomationIntegrationsTests.cs`

**关键 schema：**

```sql
CREATE TABLE automation_rules (rule_id TEXT PRIMARY KEY, version INTEGER NOT NULL, name TEXT NOT NULL, trigger_type TEXT NOT NULL, enabled INTEGER NOT NULL, cooldown_seconds INTEGER NOT NULL, cooldown_scope TEXT NOT NULL, concurrency_policy TEXT NOT NULL, failure_policy TEXT NOT NULL, created_utc INTEGER NOT NULL, updated_utc INTEGER NOT NULL);
CREATE TABLE automation_condition_nodes (node_id TEXT PRIMARY KEY, rule_id TEXT NOT NULL, parent_node_id TEXT NULL, ordinal INTEGER NOT NULL, node_kind TEXT NOT NULL, field_key TEXT NULL, operator TEXT NULL, scalar_value TEXT NULL, min_value INTEGER NULL, max_value INTEGER NULL, FOREIGN KEY(rule_id) REFERENCES automation_rules(rule_id) ON DELETE CASCADE);
CREATE TABLE automation_condition_set_values (node_id TEXT NOT NULL, ordinal INTEGER NOT NULL, value TEXT NOT NULL, PRIMARY KEY(node_id, ordinal), FOREIGN KEY(node_id) REFERENCES automation_condition_nodes(node_id) ON DELETE CASCADE);
CREATE TABLE automation_actions (action_id TEXT PRIMARY KEY, rule_id TEXT NOT NULL, ordinal INTEGER NOT NULL, action_type TEXT NOT NULL, target_kind TEXT NOT NULL, text_value TEXT NULL, reference_id TEXT NULL, amount INTEGER NULL, duration_seconds INTEGER NULL, FOREIGN KEY(rule_id) REFERENCES automation_rules(rule_id) ON DELETE CASCADE);
CREATE TABLE automation_triggers (trigger_id TEXT PRIMARY KEY, trigger_type TEXT NOT NULL, occurred_utc INTEGER NOT NULL, actor_crossplatform_id TEXT NULL, actor_entity_id INTEGER NULL, actor_group TEXT NULL, permission_level INTEGER NULL, chat_text TEXT NULL, scheduled_for_utc INTEGER NULL, blood_moon_phase TEXT NULL);
CREATE TABLE automation_trigger_gaps (trigger_id TEXT NOT NULL, gap_id TEXT NOT NULL, PRIMARY KEY(trigger_id, gap_id), FOREIGN KEY(trigger_id) REFERENCES automation_triggers(trigger_id) ON DELETE CASCADE);
CREATE TABLE automation_executions (execution_id TEXT PRIMARY KEY, rule_id TEXT NOT NULL, trigger_id TEXT NOT NULL, status TEXT NOT NULL, correlation_id TEXT NOT NULL, started_utc INTEGER NULL, completed_utc INTEGER NULL, error_code TEXT NULL, UNIQUE(rule_id, trigger_id));
CREATE TABLE automation_condition_results (execution_id TEXT NOT NULL, node_id TEXT NOT NULL, truth TEXT NOT NULL, value_summary TEXT NULL, PRIMARY KEY(execution_id, node_id), FOREIGN KEY(execution_id) REFERENCES automation_executions(execution_id) ON DELETE CASCADE);
CREATE TABLE automation_action_results (execution_id TEXT NOT NULL, ordinal INTEGER NOT NULL, action_type TEXT NOT NULL, status TEXT NOT NULL, consumer_idempotency_key TEXT NOT NULL, error_code TEXT NULL, started_utc INTEGER NOT NULL, completed_utc INTEGER NULL, PRIMARY KEY(execution_id, ordinal));
CREATE TABLE discord_settings (singleton_id INTEGER PRIMARY KEY CHECK(singleton_id=1), version INTEGER NOT NULL, enabled INTEGER NOT NULL, mode TEXT NOT NULL, application_id TEXT NULL, guild_id TEXT NULL, public_channel_id TEXT NULL, bridge_game_to_discord INTEGER NOT NULL, bridge_discord_to_game INTEGER NOT NULL, proxy_enabled INTEGER NOT NULL, proxy_uri TEXT NULL, updated_utc INTEGER NOT NULL);
CREATE TABLE discord_secrets (secret_key TEXT PRIMARY KEY, secret_value TEXT NOT NULL, fingerprint TEXT NOT NULL, updated_utc INTEGER NOT NULL);
CREATE TABLE discord_targets (target_key TEXT PRIMARY KEY, delivery_mode TEXT NOT NULL, channel_id TEXT NULL, enabled INTEGER NOT NULL);
CREATE TABLE discord_command_settings (command_key TEXT PRIMARY KEY, enabled INTEGER NOT NULL, remote_allowed INTEGER NOT NULL);
CREATE TABLE discord_deliveries (delivery_id TEXT PRIMARY KEY, business_key TEXT NOT NULL UNIQUE, target_key TEXT NOT NULL, status TEXT NOT NULL, content_text TEXT NULL, content_summary TEXT NOT NULL, next_attempt_utc INTEGER NULL, retry_count INTEGER NOT NULL, created_utc INTEGER NOT NULL, completed_utc INTEGER NULL);
CREATE TABLE discord_delivery_attempts (delivery_id TEXT NOT NULL, attempt_no INTEGER NOT NULL, status TEXT NOT NULL, started_utc INTEGER NOT NULL, completed_utc INTEGER NULL, error_code TEXT NULL, PRIMARY KEY(delivery_id, attempt_no), FOREIGN KEY(delivery_id) REFERENCES discord_deliveries(delivery_id) ON DELETE CASCADE);
CREATE TABLE discord_bindings (discord_subject TEXT PRIMARY KEY, crossplatform_id TEXT NOT NULL UNIQUE, active INTEGER NOT NULL, created_utc INTEGER NOT NULL, updated_utc INTEGER NOT NULL);
CREATE TABLE discord_binding_codes (code_id TEXT PRIMARY KEY, crossplatform_id TEXT NOT NULL, code_prefix TEXT NOT NULL, code_hash BLOB NOT NULL UNIQUE, expires_utc INTEGER NOT NULL, consumed_utc INTEGER NULL);
CREATE TABLE discord_interactions (interaction_id TEXT PRIMARY KEY, command_key TEXT NOT NULL, status TEXT NOT NULL, expires_utc INTEGER NOT NULL, completed_utc INTEGER NULL);
CREATE TABLE discord_interaction_secrets (interaction_id TEXT PRIMARY KEY, token_value TEXT NOT NULL, expires_utc INTEGER NOT NULL, FOREIGN KEY(interaction_id) REFERENCES discord_interactions(interaction_id) ON DELETE CASCADE);
CREATE TABLE discord_bridge_messages (bridge_message_id TEXT PRIMARY KEY, source TEXT NOT NULL, source_message_id TEXT NOT NULL, expires_utc INTEGER NOT NULL, UNIQUE(source, source_message_id));
CREATE TABLE geoip_settings (singleton_id INTEGER PRIMARY KEY CHECK(singleton_id=1), version INTEGER NOT NULL, enabled INTEGER NOT NULL, provider TEXT NOT NULL, failure_mode TEXT NOT NULL, bypass_admins INTEGER NOT NULL, rejection_message TEXT NOT NULL);
CREATE TABLE geoip_secrets (secret_key TEXT PRIMARY KEY, secret_value TEXT NOT NULL, fingerprint TEXT NOT NULL, updated_utc INTEGER NOT NULL);
CREATE TABLE geoip_network_rules (rule_id TEXT PRIMARY KEY, network_cidr TEXT NOT NULL UNIQUE, effect TEXT NOT NULL, ordinal INTEGER NOT NULL);
CREATE TABLE geoip_country_rules (country_code TEXT PRIMARY KEY, effect TEXT NOT NULL);
CREATE TABLE geoip_cache (canonical_ip TEXT PRIMARY KEY, lookup_status TEXT NOT NULL, country_code TEXT NULL, source TEXT NOT NULL, source_version TEXT NULL, queried_utc INTEGER NOT NULL, expires_utc INTEGER NOT NULL);
CREATE TABLE geoip_decisions (decision_id TEXT PRIMARY KEY, occurred_utc INTEGER NOT NULL, masked_ip TEXT NOT NULL, crossplatform_id TEXT NULL, decision TEXT NOT NULL, reason_code TEXT NOT NULL, lookup_status TEXT NOT NULL);
CREATE TABLE automation_integration_operation_audit (operation_id TEXT PRIMARY KEY, actor_subject TEXT NOT NULL, action TEXT NOT NULL, target_kind TEXT NOT NULL, target_id TEXT NULL, status TEXT NOT NULL, occurred_utc INTEGER NOT NULL, correlation_id TEXT NULL);
```

- [x] **步骤 1：写迁移和 Store RED**

  覆盖迁移重复执行、外键/检查约束、`(ruleId, triggerId)` 幂等竞争、版本冲突、逐条件/动作更新、delivery 与 attempt 原子更新、`Sending` 重启转 `ResultUnknown`、interaction token 到期清除、桥接去环、绑定码一次消费、专用 Discord/GeoIP Secret、规范 IP cache 和 keyset 查询。`automation_trigger_gaps` 只引用第一波不可变 gap ID，不复制 gap 正文。

- [ ] **步骤 2：运行 RED**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~SqliteAutomationIntegrationsTests"
  ```

  预期：因 migration/table/Store 缺失失败，且 `011_EconomyCommunity.sql` 前置检查明确通过。

- [ ] **步骤 3：实现参数化 Store 并转 GREEN**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~SqliteAutomationIntegrationsTests|FullyQualifiedName~Sqlite"
  ```

  预期：聚焦 SQLite 测试通过；Secret 只由专用写/读取方法访问，任何查询 DTO 不含 `secret_value`。`012` migration 在建表后重新创建第一波 `unified_audit_projection`，把规则/集成配置操作、自动化执行、Discord delivery 和 GeoIP 决策的稳定摘要加入查询，不复制 trigger/chat 正文、Secret、IP 或外部响应。

### 任务 3：交付规则 CRUD、静态验证与无副作用 dry-run

**文件：**

- 新建 Application 合同/目录：`backend/src/Core/LSTY.SevenDPanel.Application/Automations/AutomationModels.cs`、`backend/src/Core/LSTY.SevenDPanel.Application/Automations/AutomationFieldCatalog.cs`
- 新建验证/求值：`backend/src/Core/LSTY.SevenDPanel.Application/Automations/AutomationRuleValidator.cs`、`backend/src/Core/LSTY.SevenDPanel.Application/Automations/AutomationConditionEvaluator.cs`
- 新建 CRUD/dry-run：`backend/src/Core/LSTY.SevenDPanel.Application/Automations/AutomationRuleUseCases.cs`、`backend/src/Core/LSTY.SevenDPanel.Application/Automations/DryRunAutomationRuleUseCase.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/AutomationRuleUseCaseTests.cs`

**固定接口：**

```csharp
public sealed class DryRunAutomationRuleUseCase
{
    public AutomationDryRunResult Execute(AutomationRuleDraft rule, AutomationTriggerSnapshot snapshot, AuthenticatedActor actor);
}

public interface IAutomationDependencyCatalog
{
    AutomationDependencyState Resolve(AutomationAction action);
}
```

- [x] **步骤 1：写 validation/dry-run RED**

  覆盖五种 trigger 的批准字段、非法跨 trigger 字段、十种动作的类型化参数、依赖停用、Owner 权限、树边界、`Unknown` 轨迹、目标解析、最近真实/用户提供 snapshot，以及 dry-run 后规则/执行/delivery/经济/grant 行数均不变。

- [ ] **步骤 2：确认 RED**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~AutomationRuleUseCaseTests"
  ```

  预期：断言落在字段目录、权限、依赖、三值判断或副作用计数，不依赖真实 7DTD/Discord。

- [x] **步骤 3：实现结构化合同**

  Web DTO 到 `AutomationRuleDraft` 的映射必须逐类型构造；消息仅为纯文本，目标只可为 `TriggerPlayer`、固定稳定玩家、全局或批准 Discord target；`RestrictedCommand` 只保存固定 catalog key，不保存命令文本/参数串。

- [ ] **步骤 4：转 GREEN**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~AutomationRuleUseCaseTests|FullyQualifiedName~AutomationDomainTests"
  ```

  预期：规则 CRUD/版本冲突、验证和 dry-run 全部通过，dry-run 没有任何业务副作用。

### 任务 4：交付 trigger ingress、幂等执行、恢复和类型化动作调度

**文件：**

- 新建 Application ingress/执行：`backend/src/Core/LSTY.SevenDPanel.Application/Automations/IAutomationTriggerIngress.cs`、`backend/src/Core/LSTY.SevenDPanel.Application/Automations/AutomationExecutionEngine.cs`、`backend/src/Core/LSTY.SevenDPanel.Application/Automations/AutomationActionDispatcher.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Inbound/Automations/AutomationTriggerRuntime.cs`
- 修改事件来源：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Runtime/GameEvents/GameEventWriteService.cs`（第一波）、`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Inbound/Chat/SevenDaysChatMessageCoordinator.cs`
- 修改调度来源：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/Schedules/BackgroundScheduler.cs` 与第二波血月公告类型化入口
- 新建测试：`backend/tests/LSTY.SevenDPanel.Tests/AutomationExecutionTests.cs`、`backend/tests/LSTY.SevenDPanel.Tests/AutomationTriggerRuntimeTests.cs`

**固定执行键：** `executionId = SHA-256(ruleId + "\n" + triggerId)`；`consumerIdempotencyKey = executionId + ":" + actionOrdinal`。

- [x] **步骤 1：写执行 RED**

  覆盖回调只复制标量并 `TryWrite`、triggerId 稳定、gap ID 关联、重复 trigger 只一条执行、全局/玩家冷却、两种并发策略、顺序动作、两种失败策略、能力停用/目标失效、已完成动作不回滚，以及重启只重投消费者声明幂等且尚未开始的动作。

- [ ] **步骤 2：运行 RED**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~AutomationExecutionTests|FullyQualifiedName~AutomationTriggerRuntimeTests"
  ```

  预期：失败定位在幂等、并发、恢复或回调非阻塞行为。

- [x] **步骤 3：接入既有消费者**

  `AutomationActionDispatcher` 显式依赖公告/私聊、第三波 item action、第四波 grant/经济、第一波 mute、既有 kick、受限命令和 Discord outbox；经济加发放只调用第四波 grant operation，不在引擎内开跨能力事务或补偿。

- [ ] **步骤 4：转 GREEN 并验证停止顺序**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~AutomationExecutionTests|FullyQualifiedName~AutomationTriggerRuntimeTests|FullyQualifiedName~GameThreadDispatcherTests"
  ```

  预期：先停止四类生产者，再完成 ingress，最后有界排空执行；未知非幂等动作进入 `ResultUnknown` 并等待人工核对，不自动重放。

### 任务 5：交付 Discord 配置、Secret 摘要与持久 delivery worker

**文件：**

- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/LSTY.SevenDPanel.Adapters.Local.csproj`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj`
- 新建 Application 合同/用例：`backend/src/Core/LSTY.SevenDPanel.Application/Discord/DiscordModels.cs`、`backend/src/Core/LSTY.SevenDPanel.Application/Discord/DiscordConfigurationUseCases.cs`、`backend/src/Core/LSTY.SevenDPanel.Application/Discord/DiscordDeliveryUseCases.cs`
- 新建 REST/delivery：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/Discord/DiscordApiClient.cs`、`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/Discord/DiscordDeliveryWorker.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/DiscordDeliveryTests.cs`

**固定 delivery 状态：** `Pending`、`Sending`、`RetryScheduled`、`Succeeded`、`Failed`、`ResultUnknown`、`Cancelled`；自动重试最多 `5` 次，基础退避 `2s`、上限 `5m`，Discord `429` 优先采用 `Retry-After`。

- [x] **步骤 1：写配置、脱敏和 delivery RED**

  覆盖 Webhook/Bot 两模式、具名目标/频道映射、代理安全摘要、乐观版本、停用保留数据、`?wait=true`、200/204、429、认证失败、连接失败、超时、`Sending` 重启、人工重试 attempt 递增且 business key 不变、日志/DTO/异常无 Token/Webhook URL/代理密码/响应正文。

- [ ] **步骤 2：确认 RED**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~DiscordDeliveryTests"
  ```

  预期：使用 fake `HttpMessageHandler` 与 fake clock，完全离线失败。

- [ ] **步骤 3：实现官方 API v10 Adapter**

  Webhook 内容限制 `1..2000`，`allowed_mentions.parse=[]`；Bot REST 使用 `Authorization: Bot` Header；持久 `content_text` 只供 worker 投递且终态后清空，API/日志只暴露 `content_summary`。仅已明确拒绝/限流的尝试自动重试，响应可能未知时写 `ResultUnknown`；停用先停止 Gateway/新 delivery，再有界排空已接受项。

- [ ] **步骤 4：转 GREEN**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~DiscordDeliveryTests|FullyQualifiedName~DependencyRulesTests"
  ```

  测试项目沿用 Local Adapter `ProjectReference`。预期：delivery/Secret/项目依赖测试通过；Local Adapter 只引用 Application/Domain/Hosting，不引用 Web、SevenDays 或 Bootstrap。

### 任务 6：交付 Bot 入站、聊天去环、绑定、Slash 与固定远程命令

**文件：**

- 新建 Application 绑定/目录/入站：`backend/src/Core/LSTY.SevenDPanel.Application/Discord/DiscordBindingUseCases.cs`、`backend/src/Core/LSTY.SevenDPanel.Application/Discord/DiscordCommandCatalog.cs`、`backend/src/Core/LSTY.SevenDPanel.Application/Discord/DiscordInboundUseCases.cs`
- 新建 Gateway/签名/路由/桥接 Adapter：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/Discord/DiscordGatewayClient.cs`、`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/Discord/DiscordInteractionSignatureVerifier.cs`、`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/Discord/DiscordInboundRouter.cs`、`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/Discord/DiscordChatBridge.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Inbound/Chat/DiscordBindingGameCommand.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/DiscordInboundTests.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/DiscordBindingTests.cs`

**固定目录：**

```csharp
public enum DiscordCommandKey { ServerStatus, ListPlayers, Help, Bind, Unbind }
public sealed class DiscordCommandCatalog
{
    public DiscordCommandDefinition Resolve(DiscordCommandKey key);
}
```

- [ ] **步骤 1：写签名/权限/绑定 RED**

  覆盖 Discord PING、Ed25519 有效/无效签名、时间戳超过 `5m`、错误 application/guild/channel、成员权限不足、allow-list 外命令、固定参数 schema、3 秒内 deferred response、专用 secret row 保存并在 15 分钟到期/完成后删除 interaction token；绑定码 `10m`、只显示一次、只存 SHA-256/prefix、并发兑换仅一次、Owner/本人解绑。

- [x] **步骤 2：写聊天桥和去环 RED**

  覆盖游戏到 Discord 只转普通允许频道纯文本；Discord 到游戏只接受 Bot Gateway/签名入口、批准 public channel 和非 Bot 成员；`discord_bridge_messages` 持久保存稳定 `bridgeMessageId` 与来源消息键，到期清理，重启后的重复/回声仍被拒绝且不阻断原始聊天。

- [ ] **步骤 3：运行 RED 并实现最小目录**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~DiscordInboundTests|FullyQualifiedName~DiscordBindingTests"
  ```

  `ServerStatus`/`ListPlayers` 调用既有只读用例，`Help` 只列当前启用目录；`Bind`/`Unbind` 只处理稳定玩家身份。任何输入都不能落到 `ExecuteConsoleCommandUseCase`、网页控制台或 shell。

- [ ] **步骤 4：转 GREEN**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~DiscordInboundTests|FullyQualifiedName~DiscordBindingTests|FullyQualifiedName~SevenDaysChat"
  ```

  预期：签名、目录、绑定和桥接测试通过；Secret、完整 interaction token、消息正文不进入审计摘要。

### 任务 7：交付 GeoIP provider、缓存优先加入策略与诊断

**文件：**

- 新建 Application 合同/策略/用例：`backend/src/Core/LSTY.SevenDPanel.Application/GeoIp/GeoIpModels.cs`、`backend/src/Core/LSTY.SevenDPanel.Application/GeoIp/GeoIpPolicyEvaluator.cs`、`backend/src/Core/LSTY.SevenDPanel.Application/GeoIp/GeoIpUseCases.cs`
- 新建本地/外部 provider 与刷新 worker：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/GeoIp/LocalMmdbGeoIpProvider.cs`、`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/GeoIp/MaxMindWebServiceGeoIpProvider.cs`、`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/GeoIp/GeoIpRefreshWorker.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Inbound/AccessPolicies/SevenDaysGeoIpJoinPolicyRuntime.cs`
- 修改主机配置：`backend/src/Bootstrap/LSTY.SevenDPanel/Configuration/PanelHostConfig.cs`、`backend/src/Runtime/LSTY.SevenDPanel.Hosting/PanelHostOptions.cs`、`backend/src/Bootstrap/LSTY.SevenDPanel/config.example.json`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/GeoIpAccessPolicyTests.cs`

**固定结果：** `GeoIpLookupStatus` 为 `Found`、`Unknown`、`Private`、`Invalid`、`Unavailable`；`GeoIpFailureMode` 为 `FailOpen`、`FailClosed`；决策优先级为 exact/CIDR deny → 原生管理员绕过 → exact/CIDR allow → country deny/allow → 失败策略。

- [x] **步骤 1：写规范化/策略 RED**

  覆盖 IPv4、IPv6、IPv4-mapped IPv6、CIDR 边界、无效/私网、deny 优先、仅可确认原生管理员绕过、国家规则、默认 FailOpen 高可见告警、FailClosed、拒绝文案不泄露 provider/规则/错误。

- [ ] **步骤 2：写 provider/cache/回调 RED**

  使用小型测试 MMDB fixture 和 fake web service 覆盖 metadata build epoch/source version、cache TTL、失败缓存、外部模式 cache miss 不发同步网络、后台刷新、队满、停用、数据版本摘要和 masked decision。加入回调断言在远程调用完成前已经返回决策。

- [ ] **步骤 3：运行 RED 并实现**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~GeoIpAccessPolicyTests"
  ```

  本地 MMDB 由启动时构造并读取 `DatabaseReader.Metadata`，路径只来自 `PanelHostOptions.GeoIpDatabasePath`；外部 MaxMind 凭据只进入专用 Secret Store，浏览器只能选择批准 provider。

- [ ] **步骤 4：转 GREEN**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~GeoIpAccessPolicyTests|FullyQualifiedName~SevenDaysPlayerAccessControlTests"
  ```

  预期：策略、缓存、provider 和类型化拒绝测试通过；外部不可用不阻塞游戏回调。

### 任务 8：串行合流 DI/runtime、HTTP/OpenAPI、生成客户端与发布闭包

**文件：**

- 修改 solution/Bootstrap/DI：`backend/7DPanel.sln`、`backend/src/Bootstrap/LSTY.SevenDPanel/LSTY.SevenDPanel.csproj`、`backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- 新建 Web Controller：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/AutomationsController.cs`、`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/DiscordIntegrationController.cs`、`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/GeoIpAccessPoliciesController.cs`
- 新建安全 DTO：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/AutomationHttpModels.cs`、`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/DiscordIntegrationHttpModels.cs`、`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/GeoIpHttpModels.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/AutomationIntegrationsHttpTests.cs`、`backend/tests/LSTY.SevenDPanel.Tests/DiscordIntegrationHttpTests.cs`、`backend/tests/LSTY.SevenDPanel.Tests/GeoIpHttpTests.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/AutomationIntegrationsPublishTests.cs`
- 修改 DI/OpenAPI 测试：`backend/tests/LSTY.SevenDPanel.Tests/DependencyInjectionTests.cs`、`backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostOpenApiSnapshotTests.cs`
- 修改 OpenAPI/生成合同：`frontend/apps/admin/openapi/7dpanel.v1.json`、`frontend/apps/admin/src/shared/api/generated/`
- 修改：`backend/scripts/Publish-Mod.ps1`

**固定路由：**

```text
GET /api/v1/automations
POST /api/v1/automations
GET /api/v1/automations/{ruleId}
PUT /api/v1/automations/{ruleId}
DELETE /api/v1/automations/{ruleId}
POST /api/v1/automations/validate
POST /api/v1/automations/dry-run
GET /api/v1/automations/executions
GET /api/v1/automations/executions/{executionId}
GET /api/v1/integrations/discord
PUT /api/v1/integrations/discord
POST /api/v1/integrations/discord/test
GET /api/v1/integrations/discord/deliveries
POST /api/v1/integrations/discord/deliveries/{deliveryId}/retry
GET /api/v1/integrations/discord/bindings
POST /api/v1/integrations/discord/binding-codes
DELETE /api/v1/integrations/discord/bindings/{discordSubject}
GET /api/v1/integrations/discord/commands
POST /api/v1/integrations/discord/interactions
GET /api/v1/access-policies/geoip
PUT /api/v1/access-policies/geoip
POST /api/v1/access-policies/geoip/test
GET /api/v1/access-policies/geoip/diagnostics
```

- [x] **步骤 1：写 HTTP/DI/OpenAPI RED**

  覆盖面板端点未认证 401、非 Owner 403、Interaction 不建面板会话但缺/错签名 401、版本冲突 409、非法结构 400、dry-run 200、异步 delivery 202、Secret 摘要、Problem Details 稳定 code、operationId 唯一、DTO 不含 secret/path/raw response。

- [ ] **步骤 2：实现 Controller 与生命周期组合**

  `AutomationTriggerRuntime -> DiscordGateway/GeoIpRefresh producer -> automation/delivery consumer -> inner runtime` 按启动依赖正序、停止反序组合；停用模块停止新生产但不删除配置、绑定、cache 或历史。

- [ ] **步骤 3：运行聚焦 RED/GREEN 并刷新合同**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~AutomationIntegrationsHttpTests|FullyQualifiedName~DiscordIntegrationHttpTests|FullyQualifiedName~GeoIpHttpTests|FullyQualifiedName~DependencyInjectionTests"
  $env:SEVENDPANEL_UPDATE_ADMIN_OPENAPI_SNAPSHOT = "1"
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~Openapi_document_matches_admin_codegen_snapshot"
  Remove-Item Env:SEVENDPANEL_UPDATE_ADMIN_OPENAPI_SNAPSHOT
  Set-Location frontend/apps/admin
  pnpm api:gen
  Set-Location ../../..
  ```

  预期：HTTP/DI/OpenAPI 聚焦测试通过；生成目录仅由 Hey API 修改。未提交生成基线不运行 `pnpm api:check`。

- [ ] **步骤 4：验证新 DLL 的受控发布清单逻辑**

  `Publish-Mod.ps1` 必须包含现有 `LSTY.SevenDPanel.Adapters.Local.dll`、MaxMind/Chaos.NaCl 依赖并继续排除游戏程序集、旧 SQLite 和 `7dtd-reference/`。运行：

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~AutomationIntegrationsPublishTests"
  ```

  预期：受控发布清单测试通过；不执行真实 publish。

### 任务 9：实现 Admin 自动化规则、dry-run 与执行证据页面

**文件：**

- 新建 API/model：`frontend/apps/admin/src/features/automation/api/automation.ts`、`frontend/apps/admin/src/features/automation/api/automation.test.ts`、`frontend/apps/admin/src/features/automation/model/useAutomation.ts`
- 新建 view/出口/薄页面：`frontend/apps/admin/src/features/automation/ui/AutomationView.vue`、`frontend/apps/admin/src/features/automation/index.ts`、`frontend/apps/admin/src/pages/automation/index.vue`

- [ ] **步骤 1：写 parser/composable RED**

  严格解析生成 SDK 响应，拒绝未知 enum/额外 Secret/path 字段；覆盖 URL 可恢复筛选、single-flight/abort、版本冲突保留草稿、首次失败、刷新失败保留 Stale 记录、dry-run 来源选择和卸载清理。

- [ ] **步骤 2：写结构化 UI RED**

  通过可见标签和交互断言 trigger 改变时字段目录同步、All/Any/Not 树、节点/深度限制、顺序动作、依赖状态、Cron 下一次、冷却/并发/失败策略、dry-run 逐条件/目标/预计动作、执行逐动作结果；页面不存在 JSON、脚本、URL body、SQL 或控制台文本输入。

- [ ] **步骤 3：实现并转 GREEN**

  ```powershell
  Set-Location frontend/apps/admin
  pnpm test -- src/features/automation
  Set-Location ../../..
  ```

  预期：聚焦 Vitest 通过；`AutomationsView` 仅组合 composable 和聚焦子组件，桌面表格与窄屏单列均可操作。

### 任务 10：实现 Admin Discord 配置、delivery、绑定与命令目录页面

**文件：**

- 新建 API/model：`frontend/apps/admin/src/features/discord/api/discord.ts`、`frontend/apps/admin/src/features/discord/api/discord.test.ts`、`frontend/apps/admin/src/features/discord/model/useDiscord.ts`
- 新建 view/出口/薄页面：`frontend/apps/admin/src/features/discord/ui/DiscordView.vue`、`frontend/apps/admin/src/features/discord/index.ts`、`frontend/apps/admin/src/pages/integrations/discord.vue`

- [ ] **步骤 1：写安全状态 RED**

  覆盖 Webhook/Bot 模式切换、Secret 只显示 `isSet/fingerprint`、留空保持/显式清除、代理摘要、频道映射、桥接开关、远程 allow-list、连接测试、停用保留；响应体、DOM、错误、URL 和持久状态均无完整 Secret。

- [ ] **步骤 2：写 delivery/绑定/命令 RED**

  覆盖 Pending/RetryScheduled/Failed/ResultUnknown、人工重试、绑定码只显示一次、过期/已用、Owner 解绑、固定 Slash 目录/参数/权限以及桌面/窄屏诊断。所有异步动作不乐观显示成功。

- [ ] **步骤 3：实现并转 GREEN**

  ```powershell
  Set-Location frontend/apps/admin
  pnpm test -- src/features/discord
  Set-Location ../../..
  ```

  预期：聚焦测试通过；表单使用 `UForm/UFormField` 和 Valibot，持久告警使用 `UAlert`，详情/确认使用可访问 `USlideover/UModal`。

### 任务 11：实现 Admin GeoIP 持续失败策略、数据版本与诊断页面并接入导航

**文件：**

- 新建 API/model：`frontend/apps/admin/src/features/geoip/api/geoip.ts`、`frontend/apps/admin/src/features/geoip/api/geoip.test.ts`、`frontend/apps/admin/src/features/geoip/model/useGeoIp.ts`
- 新建 view/出口/薄页面：`frontend/apps/admin/src/features/geoip/ui/GeoIpView.vue`、`frontend/apps/admin/src/features/geoip/index.ts`、`frontend/apps/admin/src/pages/integrations/geoip.vue`
- 修改导航/测试：`frontend/apps/admin/src/app/AppShell.vue`、`frontend/apps/admin/src/app/AppShell.test.ts`、`frontend/apps/admin/src/app/router.test.ts`
- 修改双语/测试：`frontend/apps/admin/src/app/i18n/locales/zh-CN.json`、`frontend/apps/admin/src/app/i18n/locales/en.json`、`frontend/apps/admin/src/app/i18n/messages.test.ts`

- [ ] **步骤 1：写 GeoIP UI RED**

  覆盖 LocalMmdb/MaxMindWebService、国家 allow/deny、规范 exact/CIDR、重复/冲突规则、管理员绕过、拒绝文案、测试查询、masked recent decisions、cache/source version，以及 `FailOpen/FailClosed` 始终在 navbar 下方可见；浏览器不出现本地路径、Token 或 provider 原始错误。

- [x] **步骤 2：写导航、权限和双语 RED**

  `/automation`、`/integrations/discord`、`/integrations/geoip` 都设 `requiresAuth: true` 与 `roles: ['Owner']`；侧栏和 Dashboard Search 在“自动化”及“集成与访问策略”分组显示，Admin/Viewer 无入口且直达由服务端/路由拒绝；`zh-CN`/`en` key 集相同。320 CSS 像素布局仍须随步骤 3 的最终聚焦测试验证。

- [ ] **步骤 3：实现并转 GREEN**

  ```powershell
  Set-Location frontend/apps/admin
  pnpm test -- src/features/geoip src/app/AppShell.test.ts src/app/router.test.ts src/app/i18n/messages.test.ts
  Set-Location ../../..
  ```

  预期：GeoIP、导航、角色、双语和窄屏聚焦测试通过；typecheck/lint 留到任务 12 各自唯一聚合门禁。

### 任务 12：执行一次聚合门禁、一次真实闭环并提升文档证据

**文件：**

- 修改 Current：`docs/architecture.md`、`docs/design.md`、`docs/test.md`
- 修改 Target：`docs/architecture/legacy-feature-parity-target-blueprint.md`、`docs/architecture/backend-target-blueprint.md`、`docs/architecture/admin-frontend-target-blueprint.md`
- 修改命令入口：`README.md`、`backend/README.md`、`frontend/apps/admin/README.md`
- 更新：本计划

- [ ] **步骤 1：只运行一次受影响后端聚合门禁**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~Automation|FullyQualifiedName~Discord|FullyQualifiedName~GeoIp|FullyQualifiedName~DependencyInjectionTests|FullyQualifiedName~DependencyRulesTests|FullyQualifiedName~Openapi_document_matches_admin_codegen_snapshot"
  ```

  预期：第五波 Domain、SQLite、Application、Integrations、SevenDays、HTTP/DI/OpenAPI 受影响集合全部通过；不再重复解决方案全量测试。

- [ ] **步骤 2：只运行一次受影响 Admin 聚合门禁**

  ```powershell
  Set-Location frontend/apps/admin
  pnpm test -- src/features/automation src/features/discord src/features/geoip src/app/AppShell.test.ts src/app/router.test.ts src/app/i18n/messages.test.ts
  pnpm typecheck
  pnpm exec eslint src/features/automation src/features/discord src/features/geoip src/pages/automation/index.vue src/pages/integrations/discord.vue src/pages/integrations/geoip.vue src/app/AppShell.vue
  pnpm build
  pnpm api:gen
  Set-Location ../../..
  ```

  预期：聚焦 Vitest、typecheck、定向 lint、生产构建成功；第二次生成不产生新的内容变化。默认不运行 Playwright 或全量 lint/Vitest。

- [ ] **步骤 3：执行规格要求的唯一真实周期**

  使用受控测试 Guild、测试频道、可丢弃测试玩家、非生产 MMDB/MaxMind sandbox 和已配置的 `backend/.env.local`：

  ```powershell
  backend\scripts\Publish-Mod.cmd
  backend\scripts\Start-Server.cmd
  backend\scripts\Test-HealthEndpoint.cmd -TimeoutSeconds 90
  backend\scripts\Stop-Server.cmd
  backend\scripts\Test-HealthEndpoint.cmd -ExpectUnavailable -TimeoutSeconds 5
  ```

  在该单次运行窗口内保存：Webhook/Bot 出站、游戏↔Discord 去环往返、有效/无效签名、`/serverstatus`、绑定码兑换与 allow-list 拒绝；GeoIP 先验证 FailOpen cache miss，再用测试 IP/CIDR 受控拒绝并移除规则确认可重连。不得对生产玩家做拒绝测试；任一真实失败如实保留，不通过重跑隐藏。

- [ ] **步骤 4：只按已验证事实提升 Current/Target 文档**

  `architecture.md` 记录新 Domain、`012` migration、Local Adapter 新消费者、生命周期、数据/Secret/线程边界；`design.md` 记录三页导航、结构化编辑、状态和窄屏；`test.md` 记录精确数量、真实证据与未运行门禁；三份 Target blueprint 只提升已采用项并保留第六波目标；README 更新聚合入口/发布清单，所属 README 保存精确命令。`docs/PRD.md` 不改能力决策，`CHANGELOG.md` 在未发布前保持不变。

- [ ] **步骤 5：完成计划与文档自查**

  ```powershell
  git diff --check
  git status --short
  ```

  同时逐节映射 primary spec：用户结果、规则模型、trigger、条件、动作、dry-run、幂等/恢复、Discord 配置/Secret、delivery/桥接、绑定/Slash、GeoIP、接口/Admin、权限/生命周期、非目标、精简验证和完成条件均指向任务；核对所有 enum、状态、ID、route 和文件路径前后一致，未完成标记与省略式任务描述扫描结果为空。

## 完成标准

- 五类固定 trigger 形成不可变 snapshot 和稳定 triggerId；规则先验证、可 dry-run、按 `(ruleId, triggerId)` 幂等执行并保存逐条件/逐动作/恢复证据。
- 冷却、`SkipIfRunning`、`QueueOne`、`StopOnFailure`、`Continue` 和 `ResultUnknown` 人工核对在重启与并发下保持一致；经济/发放组合只走第四波可恢复 grant。
- Discord Webhook/Bot 可独立启停，Secret 不回显，delivery 对失败/重试/未知诚实；聊天双向去环、绑定短码、签名、Guild/频道/成员权限、固定 Slash 与 allow-list 均有自动化和一次 sandbox 证据。
- GeoIP 对规范 IP、CIDR、国家、管理员绕过、本地/外部缓存和 `FailOpen/FailClosed` 给出确定顺序；加入回调不等待远程网络，诊断不泄露内部规则或 provider 错误正文。
- Admin 三页只向 Owner 提供结构化表单和可恢复状态，`zh-CN`/`en`、桌面/窄屏、权限与 Secret 安全通过聚焦验证；不存在 JSON/脚本/任意 HTTP/SQL/shell/控制台代理入口。
- 受影响后端/Admin 聚合门禁各只执行一次，真实 publish/7DTD 周期只执行一次，未执行的 Playwright、全量测试和跨平台 smoke 在 `docs/test.md` 如实记录。
- Current 文档只陈述代码和验证已证明的事实，Target 文档不冒充实现；工作区保持未提交，任何 Git 提交仍需用户显式授权。
