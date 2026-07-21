---
state: Current
document_role: Implementation Plan
primary_spec: ../specs/2026-07-20-microsoft-di-request-scope-design.md
last_updated: "2026-07-20"
---

# Microsoft DI 请求作用域实施计划

> 本计划只落实 [Microsoft DI 请求作用域设计规格](../specs/2026-07-20-microsoft-di-request-scope-design.md)。它不增加生产日志 API、认证、数据库或 Admin 页面。

## 实施约束

- Bootstrap 是唯一根 Provider 组合根。
- SSE Scoped Session 是本切片当前生产消费者，不增加空的通用 scope 抽象。
- OWIN scope 是正常请求的唯一实际所有者；Web API bridge 使用 non-owning wrapper。
- 不采用 Autofac、`[FromServices]`、业务 service locator 或反射批量注册。
- 先停止运行时和 OWIN，再释放根 Provider。
- 不执行 Git 提交。

## 任务 1：锁定宿主兼容依赖

- [x] 确认 implementation `6.0.2`/Abstractions `6.0.0` 的 restore metadata 与游戏 Bcl AsyncInterfaces 6、当前 Tasks Extensions 兼容。
- [x] Bootstrap 引用 implementation `6.0.2`，Web Adapter 引用 Abstractions `6.0.0`。
- [x] 将游戏 Bcl AsyncInterfaces 建模为排除 runtime 的编译依赖并禁止发布重复 DLL。
- [x] 发布检查要求 DI implementation/Abstractions 存在。

## 任务 2：实现 OWIN/Web API scope bridge

- [x] 新增 root resolver 与 owning/non-owning dependency scope。
- [x] 新增 OWIN request-scope middleware，并确保 scope 覆盖完整下游响应。
- [x] 新增 Web API bridging handler，把同一 OWIN scope 写入 request dependency scope。
- [x] Controller 通过 `ActivatorUtilities` 构造，避免容器与 Web API 双重拥有。
- [x] 删除 `OwinStartup` 内嵌的 `ConsoleLogsDependencyResolver`。

## 任务 3：建立 Bootstrap composition root

- [x] 新增显式 `PanelServiceProviderFactory`，启用 `ValidateOnBuild` 和 `ValidateScopes`。
- [x] 注册 Singleton `ConsoleLogService`、同一个 `IConsoleLogStream`、`ModHost` 和 `IModRuntime`。
- [x] 新增 `ServiceProviderRuntime`，聚合 inner stop 与根 Provider dispose 失败。
- [x] `ModMain` 只发布完成注册与启动的 candidate runtime/adapter，失败路径保持可恢复。

## 任务 4：把 SSE 变成 Scoped Session

- [x] 新增 Scoped `ConsoleLogSseSession`，迁移 replay/live、gap、heartbeat 和订阅清理。
- [x] `ConsoleLogsController` 只保留开关、Header、HTTP response 和 PushStream 组装。
- [x] 默认关闭在订阅前返回 404。
- [x] 连接结束、取消、overflow 和 Hub complete 都释放订阅。

## 任务 5：定向自动化验证

- [x] 覆盖 scope 隔离、共享、一次释放和 root resolver fallback。
- [x] 覆盖 OWIN middleware 与 Web API 使用同一个 scope。
- [x] 覆盖 SSE 打开期间 Session 存活，断开后 Session 与 Hub 订阅释放。
- [x] 覆盖 Provider 验证、启动失败清理和关服/Provider 释放顺序。
- [x] 覆盖依赖方向、发布清单和游戏程序集排除。

## 任务 6：聚合与真实进程验证

- [x] 稳定后执行一次 Release Rebuild 和后端全量测试。
- [x] 发布 Mod 并检查 DI 依赖清单及禁止的游戏程序集。
- [x] 执行真实 Windows `v3.0.1-b4` 健康、SSE、断开、关服和 listener 释放 smoke。
- [x] 恢复服主原始配置，不执行无关浏览器视觉检查。

## 任务 7：同步文档并复核

- [x] 更新 `backend/README.md`、当前架构、Target 蓝图和测试策略。
- [x] 当前文档只记录已验证事实，Target 蓝图保留尚未实现的生产认证边界。
- [x] 执行 AI 文档审查、链接/占位符检查、`git diff --check` 和工作区复核。
