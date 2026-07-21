---
state: Current
document_role: Design Spec
last_updated: "2026-07-20"
---

# Microsoft DI 请求作用域设计规格

## 上游与范围

本规格落实[后端目标架构蓝图](../../architecture/backend-target-blueprint.md)中已批准的组合根依赖注入方向，并改变[当前系统架构](../../architecture.md)中的 Bootstrap、OWIN/Web API 请求生命周期和开发 SSE 职责。用户已明确批准把 `Microsoft.Extensions.DependencyInjection` 作为长期容器，并以当前开发 SSE 作为首个 request-scoped 生产消费者。

本切片只改变内部依赖组装、请求作用域和 SSE 会话所有权，不改变产品能力、公开生产 API、Admin 交互或认证合同，因此不修改 `docs/PRD.md`、`docs/design.md` 或 `CHANGELOG.md`。

## 目标

- Bootstrap 成为唯一根 `ServiceProvider` 组合根。
- 每个 OWIN 请求创建一个 `IServiceScope`，并让 OWIN middleware、Web API Controller 和长连接响应共享同一 scope。
- 把 SSE replay/live 写出与订阅所有权提取为 Scoped `ConsoleLogSseSession`。
- 保持日志采集、Hub、OWIN 和 Provider 的确定停止顺序。
- 删除只服务单个 Controller 的 `ConsoleLogsDependencyResolver`。
- 不引入 Autofac、`[FromServices]`、Controller/Repository 反射扫描或 service locator 业务代码。

## 依赖兼容策略

固定游戏运行时 `v3.0.1-b4` 已提供：

- `Microsoft.Bcl.AsyncInterfaces.dll`，程序集版本 `6.0.0.0`；
- `System.Threading.Tasks.Extensions.dll`，程序集版本 `4.2.0.0`。

当前 Mod 已通过 `System.Threading.Channels 8.0.0` 发布并验证 `System.Threading.Tasks.Extensions 4.5.4`，其程序集版本为 `4.2.0.1`。MEDI `8.0.0` 和 `10.0.9` 会分别要求 Bcl AsyncInterfaces 8 和 10；直接采用会新增一个与游戏同名且主版本不同的程序集。

NuGet 官方 metadata 确认最高稳定 `6.0.x` implementation 为 `Microsoft.Extensions.DependencyInjection 6.0.2`，其 `.NETFramework4.6.1` 资产依赖 `Microsoft.Extensions.DependencyInjection.Abstractions 6.0.0`、Bcl AsyncInterfaces `6.0.0`、Unsafe `6.0.0` 和 Tasks Extensions `4.5.4`。因此 Bootstrap 固定 implementation `6.0.2`，Web Adapter 固定 Abstractions `6.0.0`。游戏 Bcl AsyncInterfaces 作为编译/运行宿主引用且不复制；若 restore 不能形成该边界，则停止实施并重新评估，而不是静默发布重复 DLL。

## 生产对象图与生命周期

```text
ModMain
  -> PanelServiceProviderFactory
       -> root ServiceProvider
            -> singleton ConsoleLogService / IConsoleLogStream
            -> singleton ModHost
            -> singleton ConsoleLogRuntime / IModRuntime
            -> scoped ConsoleLogSseSession
  -> ServiceProviderRuntime
       -> inner IModRuntime
       -> root ServiceProvider ownership
  -> SevenDaysGameLifecycleAdapter
```

启动顺序保持：

```text
build and validate provider
  -> resolve inner runtime
  -> register game lifecycle callbacks
  -> ConsoleLogService.Start
  -> ModHost.Start
  -> OWIN Start
```

停止顺序固定为：

```text
ConsoleLogService.Stop
  -> complete Hub and active subscriptions
  -> ModHost.Stop
  -> stop OWIN and finish request scopes
  -> root ServiceProvider.Dispose
```

`ServiceProviderRuntime` 是 Bootstrap 对根 Provider 所有权的专用适配器，不是通用组件注册表。即使 inner stop 失败，也必须尝试释放根 Provider，并聚合多个失败。Provider 释放导致单例再次 `Dispose` 时，现有运行时资源必须保持幂等。

## 服务生命周期

| 生命周期 | 服务 | 所有权 |
|---|---|---|
| Singleton | `PanelHostOptions` | Bootstrap 注册现有不可释放实例 |
| Singleton | `ConsoleLogService` | 根 Provider 创建并最终释放 |
| Singleton | `IConsoleLogStream` | 指向同一个 `ConsoleLogService.Stream` |
| Singleton | `ModHost`、`ConsoleLogRuntime`、`IModRuntime` | 根 Provider 创建；显式 runtime stop 先执行，Provider dispose 兜底 |
| Scoped | `ConsoleLogSseSession` | OWIN 请求 scope；连接结束、取消、异常或宿主停止时释放 |
| Controller | `HealthController`、`ConsoleLogsController` | Web API 负责 Controller 释放；构造依赖来自 request scope |
| 手工所有 | `OwinWebHost` | `ModHost` 工厂创建并释放 |
| 手工所有 | `SevenDaysGameLifecycleAdapter` | `ModMain` 创建并保存 |

Controller 由 Web Adapter 的 resolver 使用 `ActivatorUtilities.CreateInstance` 创建，避免 Controller 同时被 MEDI 和 Web API 双重跟踪释放。业务与 Controller 只使用构造函数注入。

## OWIN 与 Web API scope bridge

OWIN 管线最前方的 `ScopedServiceProviderMiddleware` 为请求创建唯一 `IServiceScope`，保存到当前 OWIN environment，并在 `await Next.Invoke(context)` 完成后释放。这个 await 必须覆盖 `PushStreamContent` 的完整写出时间。

`OwinScopeBridgingHandler` 在 Web API message handler 管线中把同一个 scope 以 non-owning `IDependencyScope` 暴露到 `HttpPropertyKeys.DependencyScope`。实际 scope 只由 OWIN middleware 释放；handler 不重复释放。`MicrosoftDependencyResolver.BeginScope` 只作为非 OWIN 测试或回退路径，此时返回 owning wrapper。

根 resolver 的 `Dispose` 不释放根 Provider，因为根 Provider 由 `ServiceProviderRuntime` 独占。`HttpConfiguration` 或 OWIN Host 释放 resolver 时不能提前终止 Mod 级单例。

## SSE 请求作用域

`ConsoleLogsController` 只负责：

- 默认关闭检查；
- `Last-Event-ID` 输入校验；
- HTTP 状态、headers 和 `PushStreamContent` 组装。

Scoped `ConsoleLogSseSession` 负责：

- 从 Singleton `IConsoleLogStream` 创建一个订阅；
- replay、gap 和 replay/live sequence 去重；
- `console-log`、`gap`、`unavailable` 和 heartbeat 编码；
- 客户端取消、写失败、Hub complete 和 mailbox overflow；
- 幂等释放 `IConsoleLogSubscription` 与输出流。

Controller 返回响应后，回调只捕获 Scoped Session，不依赖已释放的 Controller。开发开关 false 时在创建 `PushStreamContent` 和订阅前返回 404；内部 Hub 仍可作为日志服务的一部分存在，但不暴露网络入口。

## 最小文件职责

新增：

- `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/ServiceProviderRuntime.cs`
- `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/DependencyInjection/MicrosoftDependencyResolver.cs`
- `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/DependencyInjection/ScopedServiceProviderMiddleware.cs`
- `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/DependencyInjection/OwinScopeBridgingHandler.cs`
- `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/ConsoleLogSseSession.cs`

不创建 `AppBuilderExtensions`、`OwinContextExtensions`、`FromServicesAttribute`、`FromServicesModelBinder` 或基于反射扫描的 `ServiceCollectionExtensions`。小型显式注册保留在 `PanelServiceProviderFactory`；只有形成真实分组后才提取 Feature 注册扩展。

## 验证标准

- Provider 构建启用 `ValidateOnBuild` 和 `ValidateScopes`。
- 同一 OWIN 请求中的 middleware、Controller 依赖和 SSE Session 使用同一 scope；不同请求使用不同 scope。
- 正常响应、异常、客户端取消和 SSE 断开均只释放 scope 一次。
- SSE 响应保持打开时 Session 未释放；响应结束后 Session 和 Hub 订阅均释放。
- 默认关闭返回 404 且不创建订阅。
- 关服严格执行 Hub complete、SSE 结束、OWIN stop、根 Provider dispose。
- 发布物包含 MEDI implementation/Abstractions 及经 restore 证明需要的托管依赖，不包含游戏提供的 Bcl AsyncInterfaces、Unsafe、LogLibrary、Unity 或 Newtonsoft.Json。
- Release Rebuild 零警告，后端全量测试通过。
- Windows `v3.0.1-b4` 真实进程验证程序集加载、健康端点、SSE、断开和正常关服。

## 文档影响

实现前由本规格和对应 implementation plan 记录批准设计。实现并验证后，把真实组件、生命周期和依赖矩阵提升到[当前系统架构](../../architecture.md)，把 scope/SSE/发布风险证据写入[测试策略](../../test.md)，把依赖和开发入口说明写入 `backend/README.md`，并把[后端目标架构蓝图](../../architecture/backend-target-blueprint.md)中的 DI 状态从已批准提升为已采用。
