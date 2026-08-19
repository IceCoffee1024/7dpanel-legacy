---
state: Active
document_role: Simplification Objective and Verification Record
last_updated: "2026-08-19"
owner: Coordinator
---

# 2026-08-19 并行迭代简化目标

## 背景与目标

上一轮六阶段专项完成了基线、复杂度盘点、`SIM-013` 低风险删除、三条 Golden Path、文档自动化和认知预算，但生产代码收敛范围仍然很小。本轮不以删除文件数量或“阶段完成”作为成功标准，而是针对已识别候选建立真实调用链、保护不变量、实施最小变更，并用聚焦和聚合验证证明理解成本或错误风险确实下降。

本轮只在本地 `main` 上执行，不创建、推送或删除远程 Git 分支，不把真实 7DTD、OWIN、Linux Mono、恢复 drill 或外部服务缺失证据伪装成本地通过。

## 范围与决策规则

| 候选 | 目标 | 风险 | 当前假设 | 决策状态 |
|---|---|---|---|---|
| `SIM-017` | generic worker 只能 claim 明确支持的 JobKind，Restore 不再被通用 worker 抢占 | A | 当前负向排除 Restore 的条件对未来 JobKind 不安全，应改为正向 allowlist | pending |
| `SIM-015` | 调查彩色聊天规范化是否存在可安全合并的同层重复 | B | Application、SQLite、runtime、frontend 可能各自守护独立信任边界 | pending |
| `SIM-016` | 调查彩色聊天读取用例是否只是可删除转发 | B | Application read boundary 可能仍有真实价值，不能按行数直接删除 | pending |
| `SIM-014` | 清理 overview fixture/兼容回退 | C | 只有所有生产和测试消费者迁移后才允许删除 | pending |

`retained` 是有效结果：如果代码保护独立边界，保留并记录证据，不为了产生删除 diff 而削弱边界。`blocked` 表示需要独立设计或真实环境，不得伪装成完成。

## 不可退化不变量

- Restore 的 marker、receipt、safety copy、rollback、授权、启动顺序、结果未知语义和 row-version CAS 不变。
- 通用 worker 不得 claim 自身没有执行 handler 的 JobKind；Restore 不得被通用 worker 转成 Running 或 `job_kind_not_wired`。
- 前端严格 parser、认证过期、abort、stale/offline、SSE 刷新、query ownership 和非乐观 authoritative 更新不变。
- 不修改 migration、API/OpenAPI、generated SDK、公共接口或复杂度阈值来制造绿色。
- 不把 skipped、filtered、更新 snapshot、降低断言或重分类 baseline 当成成功。
- 真实边界 blocker 继续保持 blocker。

## 基线记录

基线必须先于生产代码修改，记录以下命令的原始结果：

- `git status --short`、`git diff --check`；
- 后端 Release build/test；
- Admin lint/typecheck/Vitest/build/api:check；
- complexity budget、backend test taxonomy、frontend feature dependency；
- simplification documentation checker；
- 当前历史失败、环境缺口和本轮新增失败必须分开记录。

> 本节由协调者在基线命令完成后补充，不能用历史结果替代当前输出。

## 允许写集与并行代理台账

| Stream | 允许写入 | 禁止写入 | 迭代 | 聚焦结果 | 独立审查 |
|---|---|---|---:|---|---|
| Restore | `RegistrationSupport.cs`、Restore/worker 聚焦测试 | marker、receipt、startup、migration、OpenAPI、frontend | 0 | pending | pending |
| Chat | Chat Application/SQLite/runtime 聚焦测试，必要时同层最小代码 | Restore、overview、generated SDK/OpenAPI | 0 | pending | pending |
| Governance | 本文件和既有简化索引（仅事实确认后） | 所有生产代码、预算阈值、测试例外 | 0 | pending | pending |
| Overview | server-status/page/operation fixtures 与 fallback（A/B 审查后启动） | Restore、Chat、transport/parser 契约 | 0 | pending | pending |

代理不得自行提交、推送或改远程。每轮先 characterization/evidence，再最小变更，再返回 changed files、before/after、不变量和原始测试输出。每个 stream 最多两次修正迭代；发生写集冲突、需要迁移/API/公共接口/放宽测试或改变恢复语义时停止并标记 blocked。

## 验收标准

- 本文件包含可复现基线、每个 stream 的 ledger、每项最终决策和原始验证结果。
- `SIM-017` 有正向 allowlist 与 Restore 不被 claim 的回归覆盖，或记录明确 blocked 原因。
- `SIM-015`、`SIM-016` 各自根据调用者和信任边界记录 removed/simplified/retained。
- `SIM-014` 只有在 production/test consumer inventory 均为零时才删除，否则记录 retained 和复核触发条件。
- 聚焦和聚合门禁通过，或所有失败明确归类为既有/环境 blocker。
- 所有代码改动都能说明减少的调用路径、概念或风险，而不是只减少行数。

## 决策日志

| ID | 假设 | 证据 | 决策 | 复核触发 |
|---|---|---|---|---|
| `SIM-017` | 负向排除 Restore 不能表达 generic worker 能力边界 | pending | pending | 新增 JobKind 或 worker handler 变化 |
| `SIM-015` | 规范化重复可能跨越独立信任边界 | pending | pending | 新增 Store 生产调用者或持久化合同变化 |
| `SIM-016` | 一跳 read use case 可能保护 Application 边界 | pending | pending | Web/Application query 规则改变 |
| `SIM-014` | overview fixture fallback 只有在零消费者时可删除 | pending | pending | 新 wire shape 或 fixture 消费者变化 |
| Budget integrity | 数值门禁必须由当前可复现测量支撑 | pending | pending | 预算、项目边界或公共接口变化 |

## 提交策略

协调者统一创建本地职责提交：目标记录、Restore 正确性、Chat 证据/安全简化、条件式 Overview 清理、最终结果记录。禁止远程 Git 操作。
