---
state: Current
document_role: Change Record Index
last_updated: "2026-08-04"
---

# 变更记录入口

本文档是 `docs/superpowers/` 的唯一入口索引，不拥有产品目标、当前设计、系统架构或测试策略事实。当前事实分别以[产品需求](../PRD.md)、[产品设计](../design.md)、[系统架构](../architecture.md)和[测试策略](../test.md)为准；本索引只说明变更记录的阅读顺序和生命周期状态。

## 阅读顺序

1. 先阅读[产品需求](../PRD.md)、[产品设计](../design.md)、[系统架构](../architecture.md)和[测试策略](../test.md)，确认当前合同与证据边界。
2. 阅读[持续复杂度收敛设计规格](specs/2026-08-04-continuous-complexity-convergence-design.md)，了解本轮范围、边界和停止条件。
3. 阅读[持续复杂度收敛实施计划](plans/2026-08-04-continuous-complexity-convergence.md)，按 Wave 和 Task 查找执行责任与验证命令。
4. 需要实现证据时阅读对应的 `.superpowers/sdd/continuous-complexity-convergence/` 报告；报告不能替代 Current 文档或聚合验证结论。
5. 需要历史背景时，再打开下方已完成、保留证据或被取代的记录。

## 活动记录

当前没有未关闭的专项执行计划；产品、设计、架构和测试的 Current 事实分别以本索引顶部链接的权威文档为准。

## 已完成或保留证据

- [旧版本功能对齐六波次设计记录](specs/2026-07-26-legacy-parity-evidence-foundation-design.md)及其同日期的 `plans/` 记录：已交付切片的设计和执行证据入口；真实游戏、外部服务、恢复和发布边界不因记录存在而自动完成。
- [游戏资源目录设计记录](specs/2026-07-26-legacy-parity-game-resource-catalog-design.md)与[实施计划](plans/2026-07-26-legacy-parity-game-resource-catalog.md)：游戏资源目录切片的历史证据入口。
- [持续复杂度收敛设计规格](specs/2026-08-04-continuous-complexity-convergence-design.md)与[实施计划](plans/2026-08-04-continuous-complexity-convergence.md)：2026-08-04 已完成 Wave 0 至 Wave 4 的执行与事实提升；后端保留两个已知聚合失败，真实 OWIN 因环境变量缺失未执行，具体当前结果以[测试策略](../test.md)为准。

## 被取代记录

- [能力收口与模块化所有权设计规格](specs/2026-08-03-capability-closure-and-modular-ownership-design.md)与[实施计划](plans/2026-08-03-capability-closure-and-modular-ownership.md)：其未完成工作已由持续复杂度收敛计划重新编排；原记录保留作为背景和历史证据。
- [Admin 信息架构重构记录](specs/2026-08-03-admin-information-architecture-refactor-design.md)：固定入口密度、文档索引和后续 Wave 的协调入口已由本轮 Task 11 接替；原导航实现与验证记录仍保留。

历史 spec、plan 和报告文件不在本索引中逐项复制，也不会因本索引创建而删除或成为 Current 事实来源。
