---
state: Current
document_role: Implementation Plan
primary_spec: ../specs/2026-07-25-admin-generated-api-colada-design.md
last_updated: "2026-07-26"
---

# Admin 生成式 API Client 与 Pinia Colada 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 按[已批准设计规格](../specs/2026-07-25-admin-generated-api-colada-design.md)建立可重复 OpenAPI 生成链，迁移综合概览查询和重启 Mutation，并用认证 SSE 与 3 秒可见轮询满足实时状态要求。

**Architecture:** Katana 集成测试拥有运行时 OpenAPI 快照导出与漂移检查；Hey API 从提交的本地快照生成隔离的 Fetch SDK 和 Pinia Colada 定义。手写共享客户端适配认证、取消、普通请求超时和 Problem Details，应用级 `serverEvents` 使用生成 SDK 管理单一 SSE、replay 游标和协议事件；Feature composable 保留领域解析与状态机，REST 快照保持权威。

**Tech Stack:** NSwag 14.7.1、xUnit v3、Vue 3.5、Pinia 3、Pinia Colada 1.4.2、Hey API openapi-ts 0.94.0 内置 Fetch Client、Vitest、pnpm 11。

---

## 文件结构

- `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OpenApi/PanelOpenApiOperationProcessor.cs`：集中分配稳定 operationId。
- `backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostOpenApiSnapshotTests.cs`：真实 Katana 文档的 operationId 与快照契约。
- `frontend/apps/admin/openapi/7dpanel.v1.json`：受控生成输入。
- `frontend/apps/admin/openapi-ts.config.ts`：Hey API 插件和输出配置。
- `frontend/apps/admin/src/shared/api/generated/`：自动生成传输代码。
- `frontend/apps/admin/src/shared/api/generatedClient.ts`：认证、超时、取消和错误适配。
- `frontend/apps/admin/src/app/serverEvents.ts`：认证 SSE、重连、游标和协议事件发布。
- `frontend/apps/admin/src/app/serverState.ts`：认证生命周期、SSE 启停和受保护缓存清理。
- `frontend/apps/admin/src/features/server-status/model/useOverview.ts`：以生成 query 为传输来源并保持领域状态。
- `frontend/apps/admin/src/features/server-operations/model/useRestartServer.ts`：以生成 mutation 为传输来源并保持确认状态机。

## 任务 1：锁定后端生成契约

- [ ] 在 Katana 测试中先增加 operationId 唯一性与前端快照一致性断言，并确认因缺少稳定名称或快照而失败。
- [ ] 在集中 OpenAPI processor 中按当前 method/route 分配稳定 operationId，保留 `issueAccessToken`。
- [ ] 使用显式更新环境变量从同一 Katana Host 写入格式化 JSON 快照，再以普通模式确认测试通过。

运行：

```powershell
dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~OwinWebHostOpenApiSnapshotTests"
```

预期：operationId 与快照测试全部通过。

## 任务 2：建立生成链

- [ ] 在 Admin 中精确安装 `@hey-api/openapi-ts 0.94.0` 和 `@pinia/colada 1.4.2`；不安装已废弃的独立 `@hey-api/client-fetch`。
- [ ] 新增 `openapi-ts.config.ts`，启用 Fetch、SDK 与 Pinia Colada query/mutation options，并清理隔离输出目录。
- [ ] 增加 `api:schema`、`api:gen` 和 `api:check` 脚本，执行生成并确认第二次生成没有差异。

运行：

```powershell
pnpm api:gen
pnpm typecheck
```

预期：生成代码存在且 TypeScript 无错误。

## 任务 3：建立运行时客户端、插件与 SSE 边界

- [ ] 先为同源限制、Bearer Header、调用者取消、普通请求超时、SSE 超时豁免、Problem Details 和 401 回调编写失败测试。
- [ ] 实现 `generatedClient.ts`，只依赖显式会话提供者，不导入 Feature；`text/event-stream` 免除 10 秒超时但保留调用者取消。
- [ ] 先为 `serverEvents.ts` 编写失败测试，再使用生成 `serverEventsGet()` 实现单连接、命名事件、heartbeat、`Last-Event-ID` replay、gap、退避重连和停止。
- [ ] 先为 Pinia Colada 注册顺序、认证 SSE 生命周期和会话结束缓存/游标清理编写失败测试，再实现 `serverState.ts` 并接入 `main.ts`。

运行：

```powershell
pnpm test:unit -- src/shared/api/generatedClient.test.ts src/app/serverEvents.test.ts src/app/serverState.test.ts
```

预期：共享客户端、SSE 运行时和认证生命周期边界测试通过。

## 任务 4：迁移综合概览查询

- [ ] 扩展 `usePageVisibilityRefresh` 和 `useOverview` 测试，要求默认生产路径使用生成 query、页面可见时每 3 秒刷新、隐藏时暂停、恢复可见立即刷新、失败保留快照且 401 清理会话。
- [ ] 使用生成 query 定义替换默认手写请求调度；依赖注入测试路径和公开 `OverviewController` 保持兼容。
- [ ] 订阅 `game-ready`、`server-stopping` 和 `gap`，通过现有强制 `refetch()` 立即刷新；`welcome` 和 heartbeat 不直接改写快照。
- [ ] 保留 `parseOverview`、领域状态映射和显式刷新行为；SSE 不直接写 REST 快照。

运行：

```powershell
pnpm test:unit -- src/features/server-status/model/usePageVisibilityRefresh.test.ts src/features/server-status/model/useOverview.test.ts
```

预期：既有与新增概览状态、可见性和 SSE 触发测试全部通过。

## 任务 5：迁移重启 Mutation

- [ ] 扩展 `useRestartServer` 测试，要求默认生产路径使用生成 mutation、无自动重试、成功后精确失效概览查询。
- [ ] 使用生成 mutation 替换默认手写传输调用；依赖注入测试、确认、单飞和错误映射保持兼容。
- [ ] 保留严格 `RestartServerAccepted` 解析，成功语义仍为脚本已启动。

运行：

```powershell
pnpm test:unit -- src/features/server-operations/model/useRestartServer.test.ts
```

预期：重启状态机与查询失效测试全部通过。

## 任务 6：同步文档并完成验证

- [ ] 更新 `docs/architecture.md`、`docs/test.md` 和 `frontend/apps/admin/README.md`，把已验证事实与操作命令写入各自所有者。
- [ ] 从 Admin 目标蓝图中移除已实现候选措辞，只保留尚未迁移部分。
- [ ] 执行文档链接、占位符和简体中文复核。
- [ ] 执行一次聚焦后端测试，以及 Admin lint、typecheck、unit test 和 production build。

运行：

```powershell
pnpm lint
pnpm typecheck
pnpm test:unit
pnpm build
```

预期：命令成功；若存在既有噪声，记录真实输出，不扩大本任务修复范围。
