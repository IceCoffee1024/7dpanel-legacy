---
state: Current
document_role: Implementation Plan
primary_spec: ../specs/2026-07-24-admin-overview-dashboard-design.md
last_updated: "2026-07-25"
---

# Admin 综合概览第一阶段实施计划

> **面向智能体执行者：** 实施时必须使用 `superpowers:subagent-driven-development`（推荐）或 `superpowers:executing-plans`，逐任务执行并在每个任务后评审。以下步骤使用复选框跟踪。

**对应规格：** [Admin 综合概览、主机身份与脚本重启三阶段设计规格](../specs/2026-07-24-admin-overview-dashboard-design.md)

**目标：** 交付综合首页第一阶段闭环：认证 Overview 快照、游戏与主机基础状态、Owner-only 主机身份和公网地址、全部固定磁盘卷、近期活动、只读重启策略，以及立即启动平台脚本和固定关服操作。

**架构：** Application 拥有不可变 Overview 合同、聚合与服务器操作用例；SevenDays Adapter 只在受控游戏线程复制游戏字段；Hosting 提供 Windows/Linux 主机采样、公网解析和脚本进程启动；SQLite 保存近期活动与服务器操作审计；Web Adapter 负责角色、HTTP DTO 和 Problem Details。Admin 使用路由局部 composable 组合快照和操作状态，不把服务器状态放入 Pinia，也不复制旧 Dashboard 的路由或组件结构。

**技术栈：** .NET Framework `4.8`、C# `11.0`、ASP.NET Web API 2、Katana、Microsoft.Extensions.DependencyInjection、Dapper、DbUp、Microsoft.Data.Sqlite、xUnit v3、7DTD Dedicated Server `v3.0.1-b4`、Vue `3.5.40`、TypeScript `6.0.3`、Nuxt UI `4.10.0`、VueUse、Vite、Vitest、Vue Test Utils、pnpm `11.13.1`。

## 实施范围

- 本计划只实施规格第一阶段，并让页面在第一阶段结束时可独立运行。
- 第二阶段的 3 秒趋势、离线历史玩家、血月、僵尸/动物、Chunk、Chunk GameObjects、Chunk Observed Entities、Entity、Item 和 `maxMemoryConsumption` 不在本计划内。
- 第三阶段的网卡明细/速率、指标历史和自动重启计划执行不在本计划内；第一阶段只读返回现有配置中的安全重启策略摘要和 `nextRunAtUtc`。
- 第一阶段不安装新 npm 或 NuGet 包，不创建通用指标框架，不引入任意命令入口，不保证脚本执行完成或服务器重启成功。

## 全局约束

- 保留匿名 `GET /health` 与 `GET /api/v1/health` 的三字段响应，不把 Overview 字段并入健康接口。
- `GET /api/v1/overview` 允许 `Owner,Admin,Viewer`；设备 ID、系统用户名、公网地址和卷根路径只向 `Owner` 返回，其他角色得到结构化裁剪状态。
- 游戏标题、存档、世界、托管堆和运行时长必须分别使用 `gameTitle`、`saveGameName`、`worldName`、`managedHeapBytes`、`worldSessionUptimeSeconds` 和 `processUptimeSeconds`；不得恢复旧 `gameName`、`mapName`、`unityHeapBytes` 或 `serverUptimeSeconds`。
- `GamePrefs.ServerIP` 只映射游戏连接地址；公网 IPv4/IPv6 只能来自显式配置或已启用的 HTTPS 检测器。
- Windows `ullTotalVirtual/ullAvailVirtual` 返回 `secondaryMemory.kind = virtualAddressSpace`；Linux `SwapTotal/SwapFree` 返回 `kind = swap`，不得生成跨平台统一虚拟内存使用率。
- 所有容量使用整数字节，时间使用 UTC ISO 8601 或明确秒数；前端只负责本地化和单位显示。
- `Process.Start` 只能使用服务端规范化后的固定配置；请求体只能是 `{ "confirmed": true }`，不能接收脚本路径、命令、参数或环境变量。
- 重启接口只在脚本进程创建后返回 `202 Accepted` 和 `restart_script_started`；不等待、不探测新进程、不回写最终状态。
- 关闭接口使用固定专用 Gateway，不复用浏览器可提交任意命令的接口；重启与关闭分别授权、确认、审计和反馈。
- 每个生产行为先写可编译的失败测试，再写最小实现。当前任务只运行定向测试；全部任务稳定后再运行一次聚合门禁。
- 本计划不授权 `git commit`、`git push`、`git reset`、`git revert`、发布、真实 7DTD 操作或浏览器 smoke。

## 文件结构锁定

```text
backend/src/Core/LSTY.SevenDPanel.Application/
|-- Overview/
|   |-- AvailabilityState.cs
|   |-- OverviewAudience.cs
|   |-- OverviewSnapshot.cs
|   |-- GameOverviewSnapshot.cs
|   |-- HostOverviewSnapshot.cs
|   |-- HostStorageVolume.cs
|   |-- RestartPolicySummary.cs
|   |-- RecentActivityItem.cs
|   |-- OverviewAttention.cs
|   |-- GetOverviewUseCase.cs
|   |-- OverviewAttentionEvaluator.cs
|   |-- IGameOverviewQuery.cs
|   |-- IHostOverviewQuery.cs
|   |-- IRestartPolicyQuery.cs
|   |-- IRecentActivityQuery.cs
|   `-- IRecentActivityWriter.cs
`-- ServerOperations/
    |-- RestartServerUseCase.cs
    |-- ShutdownServerUseCase.cs
    |-- ServerOperationResult.cs
    |-- ServerOperationExceptions.cs
    |-- IRestartScriptLauncher.cs
    |-- IShutdownServerGateway.cs
    `-- IServerOperationAuditTrail.cs

backend/src/Runtime/LSTY.SevenDPanel.Hosting/
|-- Platform/HostOverviewQuery.cs
|-- Platform/HostCpuSampler.cs
|-- Platform/HostMemorySampler.cs
|-- Platform/HostStorageSampler.cs
|-- Platform/PublicNetworkAddressResolver.cs
|-- Platform/DeviceIdentityProvider.cs
|-- Platform/WindowsHostPlatformAdapter.cs
|-- Platform/LinuxHostPlatformAdapter.cs
`-- ServerOperations/RestartScriptLauncher.cs

backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/
|-- Outbound/Overview/SevenDaysGameOverviewQuery.cs
|-- Outbound/ServerOperations/SevenDaysShutdownServerGateway.cs
`-- Inbound/Activity/SevenDaysRecentActivityRecorder.cs

backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/
|-- Migrations/004_OverviewActivityAndServerOperations.sql
|-- SqliteRecentActivityStore.cs
`-- SqliteServerOperationAuditTrail.cs

backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/
|-- OverviewController.cs
|-- OverviewHttpModels.cs
|-- ServerOperationsController.cs
`-- ServerOperationHttpModels.cs

frontend/apps/admin/src/features/server-status/
|-- api/overview.ts
|-- model/overview.ts
|-- model/useOverview.ts
|-- model/usePageVisibilityRefresh.ts
|-- ui/OverviewStatusSummary.vue
|-- ui/ServerInformationPanel.vue
|-- ui/HostPlatformPanel.vue
|-- ui/ResourceCapacityPanel.vue
|-- ui/AttentionPanel.vue
`-- ui/RecentActivityPanel.vue

frontend/apps/admin/src/features/server-operations/
|-- api/serverOperations.ts
|-- model/useRestartServer.ts
|-- model/useShutdownServer.ts
|-- ui/RestartPolicySummary.vue
|-- ui/RestartServerDialog.vue
|-- ui/ShutdownServerDialog.vue
`-- ui/QuickActionsPanel.vue
```

---

### 任务 1：先同步第一阶段权威合同

**文件：**

- 修改：`docs/PRD.md`
- 修改：`docs/design.md`
- 修改：`docs/architecture.md`
- 修改：`docs/architecture/backend-target-blueprint.md`
- 修改：`docs/architecture/admin-frontend-target-blueprint.md`
- 修改：`docs/test.md`

- [ ] **步骤 1：在 PRD 锁定用户结果和权限**

  在 `CAP-01` 增加认证综合快照、部分可用状态、游戏/主机/采样分离；在 `CAP-05` 增加 Owner-only 脚本启动和固定关服；在 `NFR-02` 增加敏感主机字段裁剪、命令输入禁止和“脚本启动不等于重启成功”。不要在 PRD 定义类名、文件名或采样实现。

- [ ] **步骤 2：在设计文档锁定首页信息顺序和状态**

  写入 `Loading/Fresh/Partial/Stale/Offline/RestartScriptStarted`，以及顶部状态、服务器信息、主机平台、资源容量、注意事项、近期活动、重启策略和快捷操作的文字层级。明确 Owner-only 字段和两个危险操作的独立确认。

- [ ] **步骤 3：在架构和目标蓝图锁定第一阶段边界**

  记录 Application 聚合、SevenDays 主线程快照、Hosting 平台采样、SQLite 活动/操作审计、Web 角色裁剪、Admin 局部状态和脚本 `Process.Start` 语义。依赖矩阵继续声明“无新增包”。

- [ ] **步骤 4：在测试策略增加第一阶段追踪**

  将 Overview 权限矩阵、Windows/Linux 平台采样、游戏主线程复制、脚本启动测试替身、前端诚实状态和危险操作文案映射到 `CAP-01`、`CAP-05`、`NFR-02`。

- [ ] **步骤 5：检查文档改动边界**

  ```powershell
  git diff --check -- docs/PRD.md docs/design.md docs/architecture.md docs/architecture/backend-target-blueprint.md docs/architecture/admin-frontend-target-blueprint.md docs/test.md
  ```

  预期：无空白错误；不更新 `CHANGELOG.md`，因为功能尚未发布。

### 任务 2：建立 Application Overview 合同与聚合用例

**文件：**

- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Overview/AvailabilityState.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Overview/OverviewAudience.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Overview/OverviewSnapshot.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Overview/GameOverviewSnapshot.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Overview/HostOverviewSnapshot.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Overview/HostStorageVolume.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Overview/RestartPolicySummary.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Overview/RecentActivityItem.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Overview/OverviewAttention.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Overview/GetOverviewUseCase.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Overview/OverviewAttentionEvaluator.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Overview/IGameOverviewQuery.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Overview/IHostOverviewQuery.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Overview/IRestartPolicyQuery.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Overview/IRecentActivityQuery.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Overview/IRecentActivityWriter.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/OverviewUseCaseTests.cs`

- [ ] **步骤 1：写 Overview 聚合 RED 测试**

  测试至少覆盖：全部来源可用、游戏来源失败但主机保留、Owner 包含敏感身份、Admin/Viewer 裁剪敏感身份、旧字段名不出现在模型中、注意事项稳定代码，以及所有来源时间不被聚合时间覆盖。

  ```csharp
  [Fact]
  public async Task Viewer_keeps_resource_data_without_sensitive_host_identity()
  {
      var useCase = OverviewFixture.CreateUseCase();
      var result = await useCase.ExecuteAsync(
          OverviewAudience.NonOwner,
          CancellationToken.None);

      Assert.Equal(AvailabilityState.Forbidden, result.Host.IdentityAvailability);
      Assert.Null(result.Host.DeviceId);
      Assert.Null(result.Host.CurrentSystemUser);
      Assert.Null(result.Host.PublicNetwork.Ipv4);
      Assert.NotNull(result.Host.Storage.Volumes);
  }
  ```

- [ ] **步骤 2：运行 Application 测试并确认 RED**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj `
    --configuration Release `
    --filter "FullyQualifiedName~OverviewUseCaseTests"
  ```

  预期：因 Overview 类型和用例尚不存在而失败；补齐最小可编译签名后，正确 RED 为聚合或裁剪断言失败。

- [ ] **步骤 3：实现不可变模型和端口**

  端口保持小而固定：

  ```csharp
  public interface IGameOverviewQuery
  {
      Task<GameOverviewSnapshot> QueryAsync(CancellationToken cancellationToken);
  }

  public interface IHostOverviewQuery
  {
      Task<HostOverviewSnapshot> QueryAsync(CancellationToken cancellationToken);
  }

  public interface IRestartPolicyQuery
  {
      RestartPolicySummary Query();
  }

  public interface IRecentActivityQuery
  {
      Task<RecentActivitySnapshot> QueryAsync(int limit, CancellationToken cancellationToken);
  }
  ```

  `GameOverviewSnapshot` 使用批准后的 `gameTitle/saveGameName/worldName/worldSessionUptimeSeconds`；`HostOverviewSnapshot` 使用 `processUptimeSeconds/residentSetBytes/managedHeapBytes/secondaryMemory/storage`。所有集合在构造时复制为只读数组。

- [ ] **步骤 4：实现聚合和注意事项**

  `GetOverviewUseCase.ExecuteAsync(OverviewAudience, CancellationToken)` 独立捕获各来源失败，保留成功分区；`OverviewAttentionEvaluator` 只根据快照生成 `game_not_ready`、`game_snapshot_stale`、`disk_space_low`、`restart_script_not_configured`、`public_ip_unavailable` 等稳定代码，不返回异常原文。

- [ ] **步骤 5：运行定向测试转 GREEN**

  重复步骤 2 命令。预期：`OverviewUseCaseTests` 全部通过。

### 任务 3：增加配置、Windows/Linux 主机采样和公网解析

**文件：**

- 新建：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/PanelOverviewOptions.cs`
- 新建：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/RestartScriptOptions.cs`
- 新建：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/Platform/HostOverviewQuery.cs`
- 新建：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/Platform/HostCpuSampler.cs`
- 新建：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/Platform/HostMemorySampler.cs`
- 新建：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/Platform/HostStorageSampler.cs`
- 新建：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/Platform/PublicNetworkAddressResolver.cs`
- 新建：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/Platform/DeviceIdentityProvider.cs`
- 新建：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/Platform/WindowsHostPlatformAdapter.cs`
- 新建：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/Platform/LinuxHostPlatformAdapter.cs`
- 修改：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/PanelHostOptions.cs`
- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/Configuration/PanelHostConfig.cs`
- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/Configuration/PanelHostConfigurationLoader.cs`
- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/config.example.json`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/PanelHostOptionsTests.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/HostOverviewQueryTests.cs`

- [ ] **步骤 1：写配置与平台 RED 测试**

  覆盖配置默认值同步、相对脚本路径、数据目录、公网配置优先、自动检测关闭、HTTPS 检测超时、Windows/Linux 当前用户名、设备 ID 摘要、CPU 首样本为 `null`、物理内存、第二类内存 `kind`、全部固定卷、主数据卷和单卷失败隔离。

- [ ] **步骤 2：运行定向测试确认 RED**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj `
    --configuration Release `
    --filter "FullyQualifiedName~PanelHostOptionsTests|FullyQualifiedName~HostOverviewQueryTests"
  ```

  预期：新配置和采样类型缺失或行为断言失败。

- [ ] **步骤 3：扩展安全配置模型**

  `config.example.json` 增加 `overview.publicNetwork` 和 `restart`：

  ```json
  {
    "overview": {
      "publicNetwork": {
        "ipv4": null,
        "ipv6": null,
        "autoDetectEnabled": false,
        "detectionEndpoint": null
      }
    },
    "restart": {
      "windowsScript": "scripts/restart-server.cmd",
      "linuxScript": "scripts/restart-server.sh",
      "workingDirectory": "."
    }
  }
  ```

  Loader 只接受 HTTPS 检测地址，规范化相对 Mod 路径但不要求脚本在面板启动时存在；无效 Overview/Restart 子配置回退其自身安全默认值，不关闭认证或整个面板。

- [ ] **步骤 4：实现平台采样**

  Windows CPU 使用 `GetSystemTimes`，Linux 使用 `/proc/stat`；相邻样本由单例采样器计算。Windows 内存使用 `GlobalMemoryStatusEx`，Linux 使用 `/proc/meminfo`。磁盘逐个读取 `DriveInfo`，过滤非固定卷和 Overlay，逐卷捕获未就绪/权限异常并标记主数据卷。

  `DeviceIdentityProvider` 使用 Windows `MachineGuid` 或 Linux machine-id 加产品命名空间做 SHA-256，返回 `7dp_device_` 前缀；不得返回 Unity 原始设备 ID。

- [ ] **步骤 5：实现公网解析和缓存**

  优先使用已验证配置值；仅在显式启用时访问配置的 HTTPS 端点。检测总超时 3 秒，成功缓存 20 分钟，失败不阻塞主机其他字段。测试注入 `HttpMessageHandler` 和时钟，不访问真实网络。

- [ ] **步骤 6：运行定向测试转 GREEN**

  重复步骤 2 命令。预期：配置和 Host 测试全部通过。

### 任务 4：在 SevenDays Adapter 复制第一阶段游戏快照

**文件：**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Overview/SevenDaysGameOverviewQuery.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/SevenDaysGameOverviewQueryTests.cs`

- [ ] **步骤 1：写字段语义和缓存 RED 测试**

  覆盖 `GamePrefs.GameName -> saveGameName`、`GamePrefs.GameWorld -> worldName`、固定 `gameTitle`、`Time.timeSinceLevelLoad -> worldSessionUptimeSeconds`、`GC.GetTotalMemory(false) -> managedHeapBytes`、进程 RSS、FPS、在线/历史玩家和游戏时间。测试还要证明没有 `mapName`、`unityHeapBytes` 或重复内存字段，并发查询共享 3 至 5 秒缓存。

- [ ] **步骤 2：运行 SevenDays 定向测试确认 RED**

  ```powershell
  $referenceRoot = (Resolve-Path '7dtd-reference').Path
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj `
    --configuration Release `
    --filter "FullyQualifiedName~SevenDaysGameOverviewQueryTests" `
    /p:SevenDaysReferenceRoot=$referenceRoot
  ```

  预期：Query 尚不存在或字段映射断言失败。

- [ ] **步骤 3：实现受控主线程复制**

  使用现有 `GameThreadDispatcher` 投递一次复制函数，在游戏线程内把需要的 `GamePrefs`、世界时间、玩家计数、FPS 和 Unity 会话时长复制到普通局部值；离开游戏线程后只持有 Application 模型。不得保存 `World`、`GameManager`、玩家集合或其他游戏活对象。

- [ ] **步骤 4：实现缓存和失败状态**

  单例 Query 以一个锁和一个共享任务实现 3 至 5 秒缓存；游戏未就绪、调度超时和字段不可用分别映射为结构化 availability。HTTP 请求取消只能取消等待者，不能破坏其他请求共享的正在执行快照。

- [ ] **步骤 5：运行定向测试转 GREEN**

  重复步骤 2 命令。预期：游戏字段、主线程边界和缓存测试全部通过。

### 任务 5：持久化近期活动和服务器操作审计

**文件：**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Migrations/004_OverviewActivityAndServerOperations.sql`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/SqliteRecentActivityStore.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/SqliteServerOperationAuditTrail.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Inbound/Activity/SevenDaysRecentActivityRecorder.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/Authentication/PanelOAuthAuthorizationServerProvider.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/SqliteOverviewActivityTests.cs`

- [ ] **步骤 1：写迁移、保留和脱敏 RED 测试**

  测试 migration 004 幂等升级、活动倒序最多 8 条、总数/最近读取时间、保留裁剪，以及操作审计 `Pending/Started/Failed`。断言活动中不出现密码、Token、API Key、玩家 IP、脚本路径、命令行或控制台输出。

- [ ] **步骤 2：运行 SQLite 定向测试确认 RED**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj `
    --configuration Release `
    --filter "FullyQualifiedName~SqliteOverviewActivityTests"
  ```

- [ ] **步骤 3：创建固定用途表和 Store**

  migration 创建 `recent_activity` 与 `server_operation_audit`，使用稳定字符串类型和 UTC 文本时间。Store 只接受 Application 批准的活动类型，不提供任意 payload 或运行时注册接口；读取只返回 `messageKey/messageArgs` 安全摘要。

- [ ] **步骤 4：接入登录和玩家事件**

  OAuth 成功发 Token 后写 `panel_login_succeeded`，只记录操作者 subject/用户名的安全参数。`SevenDaysRecentActivityRecorder` 订阅 `PlayerJoinedGame` 与 `PlayerDisconnected`，只复制玩家显示名和发生时间，不保存 IP/ClientInfo。写入失败只记录固定服务端告警，不阻止登录或游戏事件。

- [ ] **步骤 5：运行定向测试转 GREEN**

  重复步骤 2 命令。预期：迁移、查询、保留、脱敏和失败隔离测试通过。

### 任务 6：实现脚本重启和固定关服用例

**文件：**

- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/ServerOperations/RestartServerUseCase.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/ServerOperations/ShutdownServerUseCase.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/ServerOperations/ServerOperationResult.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/ServerOperations/ServerOperationExceptions.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/ServerOperations/IRestartScriptLauncher.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/ServerOperations/IShutdownServerGateway.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/ServerOperations/IServerOperationAuditTrail.cs`
- 新建：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/ServerOperations/RestartScriptLauncher.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/ServerOperations/SevenDaysShutdownServerGateway.cs`
- 修改：`backend/scripts/README.md`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/ServerOperationsTests.cs`

- [ ] **步骤 1：写重启/关闭 RED 测试**

  覆盖确认必需、Owner 由 Web 层保证、脚本未配置/不存在、Windows `cmd.exe /d /s /c`、Linux `/bin/sh`、立即返回、single-flight、启动异常、审计失败、固定 `shutdown`、两种操作独立结果和不接受命令文本。

- [ ] **步骤 2：运行定向测试确认 RED**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj `
    --configuration Release `
    --filter "FullyQualifiedName~ServerOperationsTests"
  ```

- [ ] **步骤 3：实现重启用例和 Launcher**

  ```csharp
  public interface IRestartScriptLauncher
  {
      DateTimeOffset StartConfiguredScript();
  }

  public sealed class RestartServerUseCase
  {
      public Task<ServerOperationResult> ExecuteAsync(
          string actorSubject,
          bool confirmed,
          CancellationToken cancellationToken);
  }
  ```

  Launcher 使用 `UseShellExecute = false`，不重定向 stdin/stdout/stderr，不等待退出；`Process.Start` 返回非空后立即释放句柄。测试通过注入进程启动 delegate 和临时无副作用脚本验证参数，不真正重启服务器。

- [ ] **步骤 4：实现固定关闭 Gateway**

  `SevenDaysShutdownServerGateway` 内部固定执行 `shutdown`，不接受命令参数；通过现有游戏线程/控制台服务边界执行。关闭用例拥有独立确认、审计和错误类型，不能返回 `restart_script_started`。

- [ ] **步骤 5：记录操作活动并转 GREEN**

  操作意图先写审计，再启动脚本或固定关服；完成后写 `Started/Failed` 和近期活动安全摘要。重复步骤 2 命令，预期全部通过。

### 任务 7：暴露认证 HTTP 边界并接入依赖注入

**文件：**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OverviewController.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OverviewHttpModels.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/ServerOperationsController.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/ServerOperationHttpModels.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/HttpRoutes.cs`
- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/OverviewHttpTests.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/ServerOperationsHttpTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/DependencyInjectionTests.cs`

- [ ] **步骤 1：写 HTTP 权限和响应 RED 测试**

  覆盖 Overview 未认证 401、三角色 200、Owner 敏感字段、Admin/Viewer 裁剪、部分来源仍 200；重启/关闭未认证 401、非 Owner 403、未确认 400、脚本已启动 202、固定关服结果和 Problem Details 稳定代码。

- [ ] **步骤 2：运行 Web 定向测试确认 RED**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj `
    --configuration Release `
    --filter "FullyQualifiedName~OverviewHttpTests|FullyQualifiedName~ServerOperationsHttpTests|FullyQualifiedName~DependencyInjectionTests"
  ```

- [ ] **步骤 3：实现显式 HTTP DTO 映射**

  `OverviewController` 使用 `[Authorize(Roles = "Owner,Admin,Viewer")]` 和 `api/v1/overview`；从 role claim 构造 `OverviewAudience`。HTTP DTO 明确声明 camelCase 序列化所需属性，不直接序列化游戏 Adapter 或平台内部模型。

  `ServerOperationsController` 使用 `[Authorize(Roles = "Owner")]`，只接受：

  ```csharp
  public sealed class ConfirmedServerOperationRequest
  {
      public bool Confirmed { get; set; }
  }
  ```

- [ ] **步骤 4：完成 DI 生命周期**

  平台采样器、公网解析器、游戏 Query、SQLite Store、操作用例和活动 Recorder 使用单例；Controller 保持请求作用域解析。运行时启动/停止 `SevenDaysRecentActivityRecorder`，确保订阅在 OWIN/数据库可用后建立并在 Provider dispose 前解除。

- [ ] **步骤 5：运行定向测试转 GREEN**

  重复步骤 2 命令。预期：HTTP、角色矩阵、错误码和 DI 验证通过。

### 任务 8：实现 Admin 类型化 API 和页面局部状态

**文件：**

- 新建：`frontend/apps/admin/src/features/server-status/api/overview.ts`
- 新建：`frontend/apps/admin/src/features/server-status/api/overview.test.ts`
- 新建：`frontend/apps/admin/src/features/server-status/model/overview.ts`
- 新建：`frontend/apps/admin/src/features/server-status/model/useOverview.ts`
- 新建：`frontend/apps/admin/src/features/server-status/model/useOverview.test.ts`
- 新建：`frontend/apps/admin/src/features/server-status/model/usePageVisibilityRefresh.ts`
- 新建：`frontend/apps/admin/src/features/server-operations/api/serverOperations.ts`
- 新建：`frontend/apps/admin/src/features/server-operations/api/serverOperations.test.ts`
- 新建：`frontend/apps/admin/src/features/server-operations/model/useRestartServer.ts`
- 新建：`frontend/apps/admin/src/features/server-operations/model/useShutdownServer.ts`

- [ ] **步骤 1：写 API 解析和状态 RED 测试**

  严格覆盖完整响应、分区 availability、Owner 可空敏感字段、字节/时间类型、未知枚举拒绝、请求取消、401 会话失效、失败保留最后成功值、手动刷新、页面隐藏暂停和恢复立即刷新。

- [ ] **步骤 2：运行前端定向测试确认 RED**

  ```powershell
  Set-Location frontend/apps/admin
  pnpm test:unit --run `
    src/features/server-status/api/overview.test.ts `
    src/features/server-status/model/useOverview.test.ts `
    src/features/server-operations/api/serverOperations.test.ts
  ```

- [ ] **步骤 3：实现类型和严格解析器**

  `overview.ts` 定义 `AvailabilityState = 'available' | 'stale' | 'unavailable' | 'forbidden'`，显式解析每个对象和数组；禁止 `as OverviewResponse` 直接断言。错误统一为稳定 `OverviewError`，不把后端原始 detail 作为页面文本。

- [ ] **步骤 4：实现 `useOverview()`**

  状态固定为 `loading | fresh | partial | stale | offline`。默认 30 秒刷新；每次新请求取消旧请求；失败时保留最后快照和原 `sampledAtUtc`。`usePageVisibilityRefresh()` 只安装一个 `visibilitychange` 监听器，隐藏时停止定时器，恢复时立即采样。

- [ ] **步骤 5：实现危险操作 composable**

  重启和关闭分别拥有 `idle | confirming | submitting | accepted | failed`，各自锁定提交，分别调用固定端点。重启 202 只产生 `restart_script_started`；关闭按其明确结果映射，不复用重启文案。

- [ ] **步骤 6：运行前端定向测试转 GREEN**

  重复步骤 2 命令。预期：解析、刷新、取消、保留和操作状态测试通过。

### 任务 9：组合 Admin 综合首页和中文/英文文案

**文件：**

- 新建：`frontend/apps/admin/src/features/server-status/ui/OverviewStatusSummary.vue`
- 新建：`frontend/apps/admin/src/features/server-status/ui/ServerInformationPanel.vue`
- 新建：`frontend/apps/admin/src/features/server-status/ui/HostPlatformPanel.vue`
- 新建：`frontend/apps/admin/src/features/server-status/ui/ResourceCapacityPanel.vue`
- 新建：`frontend/apps/admin/src/features/server-status/ui/AttentionPanel.vue`
- 新建：`frontend/apps/admin/src/features/server-status/ui/RecentActivityPanel.vue`
- 新建：`frontend/apps/admin/src/features/server-operations/ui/RestartPolicySummary.vue`
- 新建：`frontend/apps/admin/src/features/server-operations/ui/RestartServerDialog.vue`
- 新建：`frontend/apps/admin/src/features/server-operations/ui/ShutdownServerDialog.vue`
- 新建：`frontend/apps/admin/src/features/server-operations/ui/QuickActionsPanel.vue`
- 修改：`frontend/apps/admin/src/pages/index.vue`
- 修改：`frontend/apps/admin/src/pages/index.test.ts`
- 修改：`frontend/apps/admin/src/app/i18n/locales/zh-CN.json`
- 修改：`frontend/apps/admin/src/app/i18n/locales/en.json`
- 修改：`frontend/apps/admin/src/app/i18n/messages.test.ts`

- [ ] **步骤 1：写首页状态和权限 RED 测试**

  覆盖 Loading 骨架、Fresh 完整字段、Partial 分区状态、Stale 时间、Offline 重试、Owner 敏感字段、非 Owner 裁剪、Windows 虚拟地址空间/Linux Swap 标签、全部磁盘卷、近期活动空态/8 条限制、只读重启策略、两个独立确认框和正确成功文案。

- [ ] **步骤 2：运行页面定向测试确认 RED**

  ```powershell
  Set-Location frontend/apps/admin
  pnpm test:unit --run src/pages/index.test.ts src/app/i18n/messages.test.ts
  ```

- [ ] **步骤 3：实现信息组件**

  顶部只放面板/游戏状态、在线人数、FPS、世界会话时长、进程时长和游戏时间。服务器信息展示标题/存档/世界/版本/模式/难度/地区/语言/IP/端口；主机平台展示 OS/OS family/运行时/CPU/设备/公网；资源容量展示物理内存、第二类内存、RSS、托管堆、磁盘汇总和卷明细。

  不显示第二阶段/第三阶段字段，不复制 `worldName` 作为地图，不为没有上限的值构造百分比。

- [ ] **步骤 4：实现近期活动、策略和快捷操作**

  活动最多渲染 8 条安全摘要；`RestartPolicySummary` 显示启用、表达式、时区、警告、保存世界、模式、自定义命令是否配置、血月延迟配置、历史保留期和下一次时间，但不显示命令原文。重启和关闭按钮仅 Owner 可用，并分别打开确认框。

- [ ] **步骤 5：补齐双语文案并转 GREEN**

  所有状态、字段、单位、错误和操作反馈加入两份 locale；技术值不翻译。重复步骤 2 命令，预期页面和 locale 完整性测试通过。

### 任务 10：完成第一阶段文档回写和聚合门禁

**文件：**

- 修改：`README.md`（仅当新增仓库级聚合命令或链接）
- 修改：`backend/README.md`
- 修改：`frontend/apps/admin/README.md`（仅当模块命令发生变化）
- 修改：`docs/architecture.md`
- 修改：`docs/design.md`
- 修改：`docs/test.md`
- 修改：`docs/feature-progress.md`

- [ ] **步骤 1：运行后端聚合测试一次**

  ```powershell
  $referenceRoot = (Resolve-Path '7dtd-reference').Path
  dotnet test backend/7DPanel.sln `
    --configuration Release `
    /p:SevenDaysReferenceRoot=$referenceRoot
  ```

  预期：全部后端测试通过且测试数大于零。不运行 publish 或真实 7DTD。

- [ ] **步骤 2：运行 Admin 聚合门禁一次**

  ```powershell
  Set-Location frontend/apps/admin
  pnpm lint
  pnpm typecheck
  pnpm test:unit
  pnpm build
  ```

  预期：四个命令成功。不运行 Playwright 真实 OWIN smoke。

- [ ] **步骤 3：回写已验证的当前事实**

  只把实际完成且有测试证据的边界提升到 `docs/architecture.md`、`docs/design.md`、`docs/test.md` 和 `docs/feature-progress.md`。目标蓝图保留仍未实现的第二/第三阶段；README 只写最接近模块的稳定运行说明，不复制测试策略。

- [ ] **步骤 4：检查计划与规格覆盖**

  核对第一阶段每项至少落入一个任务：Overview、角色裁剪、游戏语义、Windows/Linux 主机采样、公网、全部固定卷、近期活动、重启策略、脚本启动、固定关服、Admin 状态和双语。确认第二/第三阶段没有被误写成已实现。

- [ ] **步骤 5：检查最终改动边界**

  ```powershell
  git diff --check
  git status --short
  ```

  预期：没有空白错误、生成物、凭据、机器路径或未经批准的依赖文件；保持未提交，等待用户决定后续 Git 操作。

## 执行完成条件

- 第一阶段 API、Admin 首页和危险操作形成可运行闭环。
- 三个角色读取 Overview 的权限和敏感字段裁剪有自动测试。
- Windows/Linux 平台语义在普通测试中使用可注入 Adapter 覆盖；不要求本计划执行时同时拥有两台真实主机。
- 重启测试只证明固定脚本进程被创建，关闭测试只证明固定 Gateway 被调用。
- 没有新增依赖，没有任意命令输入，没有脚本最终状态追踪。
- 当前架构、设计、测试和进度文档只记录已验证事实；第二/第三阶段仍保持目标状态。
