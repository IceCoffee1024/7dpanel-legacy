---
state: Current
document_role: Implementation Plan
primary_spec: ../specs/2026-07-21-online-player-query-design.md
last_updated: "2026-07-21"
---

# 在线玩家只读查询纵向切片实施计划

> **执行要求：** 使用测试驱动开发逐任务实施；每项生产行为必须先有一个因缺少对应行为而失败的测试。本文只实现[在线玩家只读查询纵向切片设计规格](../specs/2026-07-21-online-player-query-design.md)。

**目标：** 为当前唯一服主角色 `Owner` 提供 `GET /api/v1/players/online`，从 7DTD 游戏主线程返回精简、不可变且带捕获时间的在线玩家快照。

**架构：** Web Controller 处理 Owner 授权、game-readiness 和 Problem Details；Application 定义查询用例、端口与纯 BCL 快照；SevenDays Adapter 通过独立 single-flight 和现有 `GameThreadDispatcher` 在主线程读取活动游戏对象；Bootstrap 保持唯一组合根。

## 全局约束

- 不实现前端、玩家动作、`Admin`、`Viewer`、用户管理或通用权限系统。
- 响应只包含 `capturedAtUtc`、entity id、名称、原生/可选跨平台身份、ping、level 和 health。
- 不返回 IP、位置、封禁、战斗统计或离线历史；不写 SQLite、事件流、日志或缓存。
- 不解析控制台文本，不创建 Domain、Repository、通用查询总线、映射框架或共享 single-flight。
- Web、Application 和测试不得保存活动游戏对象或游戏可变集合。
- 只有连接真实测试玩家并验证字段后，才能宣称真实玩家字段兼容完成。
- 不执行 Git commit、push、reset、revert，不创建分支或 worktree。

## 任务 1：定义 Application 查询契约

**文件：**

- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Players/PlayerPlatformIdentity.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Players/PlayerSnapshot.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Players/OnlinePlayersSnapshot.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Players/IOnlinePlayerQuery.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Players/GetOnlinePlayersUseCase.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Players/OnlinePlayerQueryExceptions.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/OnlinePlayerQueryTests.cs`

- [x] 先写失败测试：用例只调用一次 Query 并原样返回；快照复制源集合；空集合非 null；构造器拒绝空名称和空平台身份。
- [x] 运行 `dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter FullyQualifiedName~OnlinePlayerQueryTests`，确认只因 Players 类型缺失而失败。
- [x] 实现不可变模型、`IOnlinePlayerQuery`、转发 cancellation token 的用例，以及 `OnlinePlayerQueryBusyException`、`OnlinePlayerSnapshotUnavailableException`。
- [x] 重跑同一测试，确认通过且无警告。

## 任务 2：实现 SevenDays 主线程查询

**文件：**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Players/SevenDaysOnlinePlayerQuery.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/SevenDaysOnlinePlayerQueryTests.cs`
- 复用：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Runtime/GameThreadDispatcher.cs`

**生产读取路径：**

```text
SingletonMonoBehaviour<ConnectionManager>.Instance.Clients.List
  -> ClientInfo.entityId / playerName / PlatformId / CrossplatformId / ping
  -> GameManager.Instance.World.Players.dict[entityId]
  -> EntityPlayer.Progression.Level / Health
  -> immutable Application snapshots
```

- [x] 先写失败测试：空列表、entity id 排序、可空跨平台身份、繁忙立即失败、成功与异常后释放门禁，以及捕获只在 Dispatcher 边界内调用。
- [x] 运行 SevenDays 查询定向测试，确认只因 Query 类型缺失而失败。
- [x] 实现 singleton 实例级原子 single-flight；使用 `GameThreadDispatcher.Enqueue` 和 5 秒启动截止时间。
- [x] 在同一主线程委托中复制全部批准字段，跳过缺少 `EntityPlayer` 的转换中连接，按 entity id 排序；基础设施不可用抛出稳定 Application 异常。
- [x] 重跑查询测试和 `GameThreadDispatcherTests`，确认通过。

## 任务 3：增加 Owner-only Web API

**文件：**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/PlayersController.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostTests.cs`

```text
GET /api/v1/players/online
Authorize Roles = Owner
200 { capturedAtUtc, players[] }
503 game_not_ready | online_player_query_busy |
    game_thread_timeout | online_player_snapshot_unavailable
```

- [x] 先写失败测试：匿名 401、Owner 空数组 200、多个玩家字段白名单与排序、game-not-ready/busy/timeout/unavailable 503；拒绝路径不调用 Query。
- [x] 运行 `OwinWebHostTests`，确认新路由 404 或缺少服务而失败，既有测试仍通过。
- [x] 实现 `[Authorize(Roles = "Owner")]` Controller、显式响应 DTO，并使用既有 Problem Details 映射稳定错误码。
- [x] 重跑全部 `OwinWebHostTests`，确认通过。

## 任务 4：接入 Bootstrap 和依赖门禁

**文件：**

- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/DependencyInjectionTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/DependencyRulesTests.cs`

- [x] 先写失败测试：具体 Query 与端口为同一 singleton，用例可解析；Application 玩家文件和项目引用边界不引入游戏/Adapter 依赖。
- [x] 运行 DI 与依赖规则定向测试，确认新增注册断言失败。
- [x] 注册 singleton `SevenDaysOnlinePlayerQuery`、映射到 `IOnlinePlayerQuery`，并注册 singleton `GetOnlinePlayersUseCase`；不增加配置、包、项目引用或生命周期抽象。
- [x] 重跑 DI、依赖规则、玩家查询与 OWIN 定向测试。

## 任务 5：同步 Current 文档

**文件：**

- 修改：`docs/architecture.md`
- 修改：`docs/test.md`
- 修改：`backend/README.md`
- 视现有摘要需要修改：`README.md`
- 更新：本计划

- [x] 从已实现代码和测试更新 Application 查询、主线程映射、Owner-only API、独立 single-flight、DI 和字段安全边界。
- [x] 更新 `CAP-01`/`CAP-02` 覆盖、自动化数量、玩家快照风险和真实进程证据；没有真实玩家时保留明确兼容缺口。
- [x] 更新模块入口摘要，不复制 API、测试策略或脚本命令的权威所有权。
- [x] 运行 lifecycle 审计、Markdown 诊断、链接检查和 `git diff --check`。

## 任务 6：聚合验证与适用 Smoke

- [x] 运行 `dotnet build backend/7DPanel.sln --configuration Release --no-restore --target:Rebuild`，要求零警告、零错误。
- [x] 运行 `dotnet test backend/7DPanel.sln --configuration Release --no-build --no-restore`，要求全部通过。
- [x] 只在发布前置条件齐备时检查发布物仍为六个产品 DLL，未增加游戏程序集副本或参考子模块内容。
- [ ] 空服务器真实进程验证 Owner 请求返回 200 空数组；受控玩家在线时的真实字段、安全白名单和兼容性已验证。
- [x] 如实记录无法执行的真实玩家或发布门禁，不伪造通过。

## 完成记录

- Application 查询测试：7 项通过。
- SevenDays Query 与 Dispatcher 定向测试：13 项通过，零警告；排序测试与平台字段失败分类均先观察到预期 RED。
- Katana `OwinWebHostTests`：31 项通过，覆盖匿名、Owner 空/多玩家、就绪短路和三类稳定 503。
- DI、依赖规则与完整纵向切片定向测试：62 项通过。
- Release Rebuild：2026-07-21 通过，零警告、零错误。
- 后端全量测试：2026-07-21 共 152 项通过，0 失败、0 跳过。
- 真实玩家 smoke：2026-07-21 已将当前 Mod 发布到 Windows `v3.0.1-b4` 测试服，在一个受控玩家在线时确认 `/health` 和 Owner Basic 认证的 `GET /api/v1/players/online` 均返回 HTTP 200，玩家数为 1；根、玩家和身份字段白名单通过，玩家名与主平台身份非空，未出现 IP、位置、封禁、战斗统计或离线历史字段。
- 同一流程随后通过 `Stop-Server.ps1` 收到 Telnet 正常关服确认并观察到远端进程停止，`Test-HealthEndpoint.ps1 -ExpectUnavailable` 确认 listener 不可达。
- 本轮未执行空服务器分支、浏览器检查或候选发布归档；这些仍保留为后续适用门禁。