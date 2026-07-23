---
state: Current
last_updated: "2026-07-23"
---

# 7DPanel 测试策略

## 范围与可追踪性

本文档定义首版产品合同、[界面设计](design.md)和[系统架构](architecture.md)风险的验证方式。当前后端解决方案包含六个产品项目和一个迁移保护测试项目；启用 C# `11.0`、Nullable Reference Types 和 Implicit Usings。无 Basic/API Key 认证变更后的 Release 聚合构建和全量 xUnit 已有 331 项通过，覆盖 password grant、8 小时默认 Access Token、Access Token/API Key 严格分流、当前用户/角色重建、API Key REST/OpenAPI、Basic 拒绝、SSE 周期复验、事件投影、输入验证与依赖边界；当前工作树也以显式 `SevenDaysReferenceRoot` 完成 Release 还原、构建和全量 xUnit，331 项通过，覆盖 password grant 成功响应的服务端 `username`/`role` 与 Admin 文档 CSP。Admin 的 Auth codec/Repository/Store、身份响应解析、登录/账号菜单和路由定向自动化已通过；完整 `pnpm test` 为 23 个文件、267 项通过，lint、typecheck 和 Vite 8 生产构建均通过。该 Vitest 输出仍包含 happy-dom teardown 的 `AbortError`、fork worker 终止超时和外部连接 `ETIMEDOUT`，根因未确认，不能视为干净的测试基础设施。当前 Owner Playwright suite 已有 8 项场景并通过测试发现，覆盖两种浏览器会话、身份显示、损坏记录和清理合同；由于没有受控 OWIN 环境变量，它尚未执行，历史的 6 项内存会话 Edge 结果不能替代本实现。2026-07-23 的远程 Windows 7DTD `v3.0.1-b4` 闭环 smoke 验证了合并提交 `a98ad6b` 的公开 Swagger UI/OpenAPI Unity Mono 加载与访问、Telnet 原生异步队列、真实 SQLite 写锁期间命令 fail-open、解锁后的 `store_failure` gap 恢复、正常关服排空、进程/监听释放和重复启停；该动态命令与 Swagger 证据发生在移除 Basic 前，不能证明当前认证版本的真实进程行为。当前认证版本的事件投影真实 Join/Save/Disconnect、Owner 踢出、浏览器持久会话、自动归档和 Linux 真实进程仍未验证。完整用户管理、其他状态变更游戏动作和其他产品能力仍未实现。以下未落地内容仍是目标测试策略和发布门槛，不代表测试已经通过。

### 产品需求追踪

| 需求 | 关键场景 | 主要测试层级 | 必须保留的证据 |
|---|---|---|---|
| `CAP-01` | Mod 内嵌宿主启动；自动识别当前服务端；状态采样时间与新鲜度；离线、不可用和过期状态不得显示为正常 | 单元、OWIN/API 集成、真实进程 smoke、浏览器 E2E | API 响应、页面断言、服务端日志和测试报告 |
| `CAP-02` | 在线玩家身份快照；踢出、禁言、封禁和传送；动态内置/第三方控制台命令；HTTP 有界 FIFO 与主线程串行；日志过滤；成功、失败、拒绝和审计缺口 | 单元、SQLite 集成、OWIN/API 集成、真实进程、E2E、安全 | 游戏内结果、API 结果、队列顺序、Harmony 覆盖和对应审计记录 |
| `CAP-03` | 手动与计划备份状态机；保存提交完成后快照；校验失败、损坏归档、磁盘空间不足；重启恢复；中断后回滚 | 单元、SQLite 集成、真实进程、故障注入、恢复演练 | 备份清单与校验和、状态迁移、恢复后存档验证和回滚证据 |
| `CAP-04` | 即时公告；进服欢迎、周期提醒和血月提醒；同一触发只执行一次；重启后的调度恢复；失败可见 | 单元、SQLite 集成、真实进程、E2E | 游戏内公告、任务执行记录和审计记录 |
| `CAP-05` | 配置凭据按固定 `Subject=owner` 同步唯一持久 `Owner`；password grant 验证当前用户并返回 `username`/`role`；默认 8 小时 Header-only Access Token、两种显式浏览器会话、到期和撤销；用户 API Key 一次性显示、当前角色继承、到期和撤销；SSE 周期复验；未来 `Admin`/`Viewer` 管理与审计 | 单元、SQLite 集成、OWIN/API 集成、真实进程、E2E、安全 | migration/Store 报告、Authorization Header、会话记录和清理、Token/API Key 生命周期、连接关闭、权限矩阵和审计记录 |
| `NFR-01` | 无产品方云服务、无外网条件下部署 Mod、打开面板并完成全部 P0 核心流程 | Windows/Linux 真实进程、离线验收 | 发布物清单、网络隔离记录和验收报告 |
| `NFR-02` | 超时、断线、过期状态、重复提交、任务失败、服务关闭和结果未知；高风险操作确认 | 单元、API 集成、E2E、故障注入 | 状态转换、错误码、页面状态和审计关联标识 |
| `NFR-03` | `zh-CN`/`en` 浏览器语言匹配与回退；登录前后切换和持久化；多表单 Valibot 内置错误；Nuxt UI 文案；日期数字格式；稳定服务端错误码映射；技术标识保持原样 | 单元、组件、浏览器 E2E | 两种语言页面断言、缺失键报告、格式化结果、切换前后表单状态和错误码映射报告 |
| `NFR-04` | 当前过渡默认凭据和明文 HTTP 可用；配置只引导固定持久 Owner；受保护资源保持认证；错误/过期/撤销/QueryString Token 被拒绝且不泄漏；用户管理落地后移除过渡默认值 | 单元、SQLite/OWIN/API 集成、真实进程、安全 | 配置与用户同步、Authorization Header、401/403/429、SSE 关闭、日志与发布物扫描 |
| `NFR-05` | 服务器本地文件读取者属于受信任运维边界；不要求配置、SQLite、备份和服务端日志静态保密；网络、API、前端资产、产品日志和版本库仍不得意外披露凭据或有效 Token | 安全审查、API 集成、发布物与版本库扫描 | 威胁模型审查结论、认证/授权结果、敏感值扫描和发布清单 |

### 架构风险追踪

| 风险 | 验证要求 |
|---|---|
| 游戏与 Mod 的 JSON 程序集冲突 | 在官方 `v3.0.1-b4` 进程内确认实际加载游戏提供的 `Newtonsoft.Json 13.0.2`，完成序列化与 Web API 路由 smoke test，并确保发布物不包含另一份同名程序集。 |
| 内存加载的 Mod 程序集缺少物理位置 | 源码规则必须证明 Bootstrap 保存当前 `ModInstance` 后，只通过游戏 `0_TFP_Harmony` 应用 `AssemblyLocationPatch`，验证自身 `Assembly.Location` 非空后才组合 SQLite/OWIN；补丁不得覆盖已有位置或其他 Mod 的程序集。发布物不得包含 `0Harmony.dll`，真实进程日志必须在 migration 前出现补丁成功记录，并由后续标准 Batteries 建库证明依赖解析可用。 |
| 参考资料越界进入产品发布物 | 验证发布组装输入和最终清单不包含 `7dtd-reference/`、反编译源码、共享参考 assets 或未声明的 runtime 文件；构建阶段若读取参考程序集，必须有单独的输入记录。 |
| 多项目依赖或发布边界漂移 | `DependencyRulesTests` 校验项目引用白名单、Inbound/Outbound 不得交叉引用，以及只有 Bootstrap 可以实现 `IModApi`；发布检查确认 Bootstrap、Application、Hosting、Web、SevenDays 和 Persistence 六个产品 DLL 齐全。 |
| 运行时 API 文档漂移或扩散依赖 | 真实 Katana Host 必须验证公开 `/swagger` 与 `/swagger/v1/swagger.json`、全部 Controller/OWIN token 路由、唯一 Bearer scheme、API Key 管理、SSE、Problem Details、SPA fallback 隔离和无业务端口副作用。依赖规则只允许 Web Adapter 直接引用 `NSwag.AspNet.Owin 14.7.1`，禁止 `NSwag.Annotations` 和其他产品层直接引用 NSwag/NJsonSchema/Namotion；发布门禁必须要求实际运行时闭包并继续排除游戏提供的 `Newtonsoft.Json.dll`。 |
| 产品版本来源漂移 | 健康端点必须返回 `ProductInfo.Version`；测试校验该值与 Bootstrap 的 `ModInfo.xml` 一致，不允许从 Adapter 当前执行程序集推断产品版本。 |
| OWIN 生命周期泄漏 | 在同一测试主机上重复启动、正常关服和再次启动服务端；确认端口可重新绑定、后台线程和计时器退出、请求在 draining 后被拒绝。 |
| 主线程调度拖慢游戏 | 动态命令容量 32 FIFO、在线玩家 Query 和踢出 Application gate 必须保持职责独立；排队取消/启动超时不得执行委托，执行开始后不得伪造取消或超时。踢出同一时刻只允许一个请求在审计和动作链路中运行；真实进程仍需建立帧预算和典型负载基线。 |
| 动态命令队列破坏顺序或无界增长 | 确定性测试必须验证 HTTP 命令有界 FIFO 的容量、严格接收顺序、每请求独立结果、队满拒绝、开始前取消、开始后真实结果、单项异常隔离、停止时排空边界，以及它不会替换或消费 7DTD 原生异步队列。 |
| 全局命令审计改变游戏行为或静默缺失 | 源码与真实进程测试必须证明 Harmony 只观察最终 `SdtdConsole.executeCommand`，不改变命令注册、同步返回或原生队列；内置、第三方 Mod、HTTP 和至少一个非 HTTP 标准入口产生完整原文审计。SQLite 锁定、队满或消费者失败时原命令继续执行，同时告警并记录可识别的审计缺口。绕过 `SdtdConsole` 的直接游戏 API 调用明确不计入覆盖率。 |
| 在线玩家字段、事件或投影边界漂移 | 自动化必须验证 Join membership、Save 不可变复制、Disconnect 身份条件删除、排序、逐玩家观察时间、可空跨平台身份、停止拒写与清空、旧 observation 不被服务端年龄策略拒绝、Owner-only、就绪短路和字段白名单；只有真实测试玩家 smoke 才能证明 `v3.0.1-b4` 事件注册与字段兼容，任何游戏活对象不得进入投影或离开事件回调。 |
| 后台命令已接收但无人消费或错误分发 | 集成测试必须覆盖生产者投递、唯一 Consumer 组件读取、有界执行槽、显式 Dispatcher 到唯一 Use Case、长任务与短任务并发、单项失败隔离、停止生产、完成写端、截止时间内排空及未处理项的明确结果。 |
| 组合运行时启停顺序漂移 | 使用记录型 `IModRuntime` 和可控日志订阅验证 `ConsoleLogService` 先启动、先停止；已接受日志、一次 `game-ready` 和 `server-stopping` 共用 sequence 且停止时排空，日志排空超时仍会尝试 `ModHost.Stop` 并聚合失败；`ModHost` 不包含具体队列、数据库或重试逻辑。 |
| 游戏日志回调拖慢或递归 | 单元测试证明回调只创建 entry 并一次非阻塞投递，窗口 append 不在回调线程执行；源码复核禁止等待、I/O、逐条 `Task.Run` 和回调内 `Log.*`，官方进程验证真实 delegate 订阅/注销。 |
| 日志突发导致无界内存或静默丢失 | 强制阻塞 consumer 的确定性测试验证容量、即时 `TryWrite` 拒绝、丢弃计数和 high-water；真实进程命令突发验证 Mono 兼容、全部已接受项排空与停止摘要。 |
| 配置引导身份或生产 SSE 被误当作完整 `CAP-05` | 默认和缺失配置必须产生批准的 `admin` / `password` 与明文 HTTP 过渡边界，并只同步固定 `owner`；password grant 必须读取 SQLite 并只在成功响应返回服务端 `username`/`role`，Access Token/API Key 只能来自 Header，Basic/Cookie/QueryString 凭据不得建立身份。严格版本化的浏览器会话只允许默认标签页或显式保持登录两种位置，损坏、到期、登出、401 和跨标签页删除必须清理；CSP 与敏感信息检查必须补偿同源 Storage 的 XSS 风险。限流、统一 401/403/429/503、8 小时默认 Token、API Key 一次性值/当前角色继承/撤销/到期、SSE 周期复验、订阅上限、Welcome、replay/gap 和断开释放必须有自动化。完整角色/用户管理仍按未实现处理，产品不采用 Cookie、CSRF Token 或 refresh token。 |
| Channels/SQLite 与宿主程序集冲突 | 发布物必须包含 Channels、Tasks.Extensions `4.6.3`、Bcl AsyncInterfaces `10.0.10`、Unsafe `6.1.2`、Microsoft.Data.Sqlite、SQLitePCLRaw Batteries/core/dynamic provider、`SQLitePCLRaw.batteries_v2.dll.config`，以及 Mod 根目录中的五个 Framework64 宿主兼容程序集和 Windows/Linux x64 RID native SQLite。根目录不得保留 native `e_sqlite3.dll`，旧 System.Data.SQLite/SQLite.Interop 与游戏提供的 Newtonsoft.Json、LogLibrary 或 Unity 程序集不得发布；源码规则必须禁止自定义 loader、ResourceManager shim 和显式 provider 绑定。Release 必须零冲突警告，官方进程日志必须证明标准 Batteries 组合加载且无托管或 native 类型加载错误。 |
| 持久化端口泄漏 provider | 依赖测试禁止 Hosting/Web 引用 Dapper、DbUp、`SqliteConnection` 或数据库事务类型；只有 Persistence Adapter 实现 credential/token Store 并拥有连接与事务。 |
| SQLite 写竞争或审计静默丢失 | 当前身份与玩家动作集成测试验证 WAL、5 秒 default timeout、立即写事务、迁移幂等、Token 严格容量、短连接、Pending 插入、并发一次完成、数据库锁失败后恢复、遗留 Pending 启动恢复和连接池清理。高风险动作在审计意图持久化失败时不得进入游戏主线程，终态写入不可确认时不得伪造失败。 |
| 在线备份不一致 | 在保存提交完成后持续制造世界写入，验证归档清单、校验和及恢复后世界数据；若无法证明一致性，发布前必须采用架构文档所述维护窗口或文件系统快照方案。 |
| 恢复时文件已经打开 | 验证待恢复操作发生在世界文件打开前；通过文件占用和中途终止注入确认原存档、待恢复标记及回滚副本可恢复。 |
| SQLite 平台兼容 | 在 Windows x64 和 Linux x64 分别验证 Native 库加载、首次建库、全部迁移、CRUD、进程重启和数据库关闭。 |
| 异常终止留下半成品 | 在备份发布、待恢复标记写入、存档替换和迁移的关键阶段终止进程；下次启动必须识别并恢复到可解释状态。 |

首版不验证 PRD 明确排除的多服管理、玩家登录、积分经济与商城、复杂反作弊、Discord 集成和通用脚本平台。7DTD 自身功能、第三方反向代理的正确性以及 dnSpy 反编译工程能否重新构建，也不属于产品测试范围。

### 变更风险分级验证

验证强度由实际变化边界决定，不由文件数量决定。迭代期间使用最小定向检查；实现稳定后再执行一次适用的聚合门禁，避免每次局部编辑重复完整流程。

| 变更边界 | 迭代期默认检查 | 完成前默认检查 | 不自动触发 |
|---|---|---|---|
| 仅 Markdown | 语言、链接、角色和语义复核 | `git diff --check` 与 AI 文档审查 | 构建、7DTD、浏览器 |
| 单元测试或局部纯逻辑 | 对应测试类或项目 | 受影响项目测试；共享契约变化时再跑后端全量 | 发布与真实进程 |
| 共享后端契约、生命周期或依赖方向 | 定向构建与测试 | 一次 Release Rebuild 和后端全量测试 | 浏览器，除非 HTTP/UI 同时变化 |
| 游戏静态事件、游戏程序集、Mod 发布物或关服顺序 | 定向测试 | 发布、一次真实 7DTD 启动/日志/关服 smoke | 浏览器，除非 Web/UI 同时变化 |
| Web API、OWIN middleware 或流式 HTTP | Controller/OWIN 集成测试 | 受影响后端门禁；Mono 兼容无法由 Katana 证明时增加一次真实进程 smoke | 前端视觉检查 |
| Admin 前端行为或静态资源契约 | lint、typecheck、相关前端测试 | 生产构建和相关浏览器/E2E | 7DTD，除非生产 OWIN 托管契约同时变化 |
| 候选发布 | 各层既有自动化 | 完整发布、Windows/Linux 真实进程、浏览器和恢复门禁 | 无 |

同一边界在代码稳定后只需取得一份新鲜的完成证据。测试失败后的修复先重跑失败范围；只有修复可能影响其他共享边界时才再次扩大范围。

## 测试层级

### 单元测试

- 后端单元测试采用 xUnit v3。无共享状态的测试允许并行；占用固定端口、SQLite 文件、游戏进程或静态游戏状态的测试必须使用测试集合隔离或显式禁止并行。
- 当前自动化覆盖 `ModHost` 启停/就绪状态和并发终止竞态，生命周期 Adapter 的三个可执行回调与失败回滚，集中控制台日志服务、当前进程 `ServerEventLiveWindow`、每客户端 `ServerEventHub` 和组合运行时，Microsoft DI scope 隔离/复用/一次释放、Provider 验证与运行时先停后释放顺序；`GameThreadDispatcherTests` 确定性验证排队取消/启动超时阻止执行，以及执行开始后取消/超时仍等待真实结果，`ConsoleCommandTests` 验证 `version` 标准化和未支持命令不会进入 Gateway。
- 动态命令自动化覆盖命令原文与 actor 透传、FIFO、容量饱和、并发独立输出、开始前取消、开始后真实结果、未知命令和执行异常；Harmony observation 覆盖返回与抛错路径、连续调用隔离、tokenizer/observer fail-open、原始参数与输出快照、来源分类、审计投递失败不影响命令结果，并证明不会产生结构化命令 SSE。阻塞 Store 的确定性测试另验证审计排空超时后保留 Provider 所有权、解除阻塞后可重试完成，以及停止摘要报告未恢复 gap 数量。
- 在线玩家单元测试验证 Application 快照复制、逐玩家观察时间和用例转发，以及 Adapter 的 Join/Save/Disconnect 顺序、不可变字段复制、稳定排序、可空跨平台身份、身份条件删除、停止拒写与清空、失败 Save 保留旧 observation、任意年龄 observation 原样返回和缺少 observation 时不产生占位玩家；事件注册与真实字段兼容仍由官方进程 smoke 负责。
- 踢出用例测试验证确认、原因与身份前置校验，审计先于动作，专属 single-flight，成功/离线/身份变化/取消/超时/未知异常的终态映射，以及审计意图或完成不可用语义。SevenDays 动作测试验证所有游戏对象访问位于 Dispatcher 内、实体与双字段身份重验、原生调用恰好一次和 `ManualKick` 参数快照。
- 服务器事件自动化覆盖日志六字段、三类 replay 事件的共享 sequence、固定窗口淘汰、批次与 gap 边界、回调/consumer 线程隔离、队满即时拒绝与 high-water 上限、保序且只消费一次、单次 `game-ready`、停止 marker、消费后通过公开 stream 边界广播、单项失败继续、订阅失败、注销后拒绝、限时排空、超时仍停止内部运行时、停止摘要时序、多订阅者隔离、mailbox 溢出、订阅上限、空窗口游标和完成释放。生产静态 delegate 的精确映射由源码复核与真实进程验证，不通过额外 source/callback 接口伪装成单元测试结论。
- 认证单元和 SQLite 集成测试使用可控时间验证 password grant、PBKDF2-HMAC-SHA256 1000 次迭代、引导凭据同步、Access Token 最旧淘汰/到期/轮换撤销、API Key 格式/一次性值/只保存 SHA-256 摘要/到期/撤销/当前用户角色重建，以及每地址限流窗口和 bucket 容量；不通过真实等待模拟 Token 到期。
- 配置测试验证 `config.example.json`、`PanelHostConfig.CreateDefault()` 和缺失配置时生成的 `config.json` 同步启用引导 `admin` / `password`、480 分钟 Token 与 `allowInsecureHttp=true`；SQLite 测试验证每次启动只更新稳定 `owner` 且凭据变化撤销旧 Access Token。解析失败仍必须关闭认证，受保护 API 不得退化为匿名访问。
- 后续用户管理切片需要覆盖至少一个 `Owner` 的保留、角色权限矩阵、并发变更和凭据恢复；其他未落地能力仍需覆盖状态新鲜度、统一错误映射、幂等键、调度规则、后台工作项到唯一 Use Case 的映射、组合运行时启停顺序、备份/恢复状态机和会话策略。
- 使用虚拟时钟、确定性任务调度和游戏适配器替身；不把需要真实 Mono、SQLite 或文件系统的行为伪装成单元测试结论。

### SQLite 与文件系统集成测试

- 当前身份与玩家动作审计测试为每个用例使用唯一临时数据库和数据目录，运行真实 DbUp migration、WAL 初始化、5 秒 default timeout、能力型 Store 短事务、Store/connection factory 重建与连接池清理；审计测试覆盖完整 Pending 快照、一次合法终态、并发完成、锁竞争和跨实例启动恢复，身份数据库扫描验证密码和完整 Token secret 不以明文落盘。
- 对高频写入制造并发日志和审计负载，验证写入串行化、审计持久化确认、锁竞争恢复、队列饱和策略和日志丢弃计数；审计或作业状态不得静默丢失。
- 对持久后台作业验证先落盘再投递、进程中断后重新发现 `queued/running` 状态和幂等恢复；对瞬时游戏事件验证过载拒绝可观测。
- 在 SQLite 写入、待恢复文件标记和安全关服之间逐点注入失败，验证跨边界流程使用幂等状态与补偿恢复，而不是依赖虚假的全局事务。
- 备份测试使用可校验的小型世界夹具，覆盖临时目录、原子发布、损坏归档、只读目录、空间不足和回滚副本。
- 测试结束释放连接和文件句柄；失败时保留诊断产物路径，CI 完成采集后再清理。

### OWIN/Web API 集成测试

- 在进程内启动完整 OWIN 管道，验证路由、输入校验、Header 认证、授权、Token 到期与撤销、SSE 最多 15 秒周期复验、统一错误结构、静态资源和 draining 行为；产品不采用 Cookie 认证、CSRF Token 或 refresh token。
- `OwinWebHostTests` 在真实 Katana 主机中验证 `/` 和无扩展名路由返回 Admin `index.html`，哈希资源按静态文件返回，缺失资源保持 404；即使 `wwwroot/api/v1/health` 存在冲突文件，`/api/v1/health` 仍由 Web API 返回健康 JSON，未知 `/api/*` 返回统一 404 Problem Details。健康 JSON 必须精确使用 `status`、`product`、`version`，大小写不匹配即失败。
- 同一 Katana 测试类使用真实根 Provider 验证匿名/错误 Basic/错误 Bearer 的 401 与唯一 Bearer challenge、password grant 成功 `username`/`role` 与 `invalid_grant` 不泄漏身份、Admin 根/深链接/`index.html` 的精确 CSP 边界、拒绝 QueryString/Cookie Token、每分钟限流 429、Welcome 先于命名 replay、gap、无效游标 400、订阅容量的建流前 503，以及响应释放后 scoped session 与 Hub 订阅清理。它还覆盖 Access Token/API Key 建流与 API Key 到期、撤销、创建者禁用或角色变化后的停止写出。直接 handler 测试另验证 Web API 使用同一个 OWIN scope，non-owning wrapper 不提前释放实际 scope。15 秒 comment heartbeat 由源码复核，不为了等待间隔增加慢集成测试，也不把间隔暴露为服主配置。
- 当前 Katana 主机验证控制台命令匿名 401、Owner/Admin 原文与 actor 透传、畸形 JSON/字段类型错误的 400 `invalid_request_body`、成功绑定后空白命令的 `console_command_required`、游戏未就绪 503、队满/服务停止 503、主线程启动超时、取消和并发请求独立输出；普通未知异常必须经 Web API pass-through 到达外层 OWIN，在日志中保留原异常和同一 traceId，同时只向客户端返回不含内部消息的安全 500 `internal_server_error`。真实 `PushStreamContent` 写出阶段异常必须由仅覆盖不可处理 catch 的 Web API logger 记录同一 traceId，不能追加第二个 Problem Details，也不能与外层 OWIN 双重记录；真正的请求取消不生成错误日志。匿名非法 body 仍由认证优先返回 401。SevenDays Adapter 的可控 Dispatcher 测试验证严格 FIFO、等待容量和队满拒绝。无效请求、队满和未就绪路径不会执行命令；未知命令不再由 Web 白名单拒绝，而由 7DTD 返回真实输出。
- 同一 Katana 主机验证玩家查询匿名 401、Owner 空/多玩家 200、根对象仅含 `players`、每个玩家携带合法 UTC `observedAtUtc`、camelCase 字段白名单与排序、游戏未就绪时不调用 Query，以及旧 observation 仍以原时间成功返回；在线玩家端点不拥有 busy、主线程启动超时或投影 stale 503。当前持久认证只创建 `Owner`，非 Owner 403 随角色管理切片验证。
- 同一 Katana 主机验证 Owner 踢出请求的 JSON 类型错误使用 `invalid_request_body`，成功绑定后的字段、确认、原因和身份错误使用端点稳定码，并覆盖认证主体、成功白名单、离线、身份变化、busy、主线程超时、审计不可用和原生失败；无效请求不会创建审计或执行动作，请求取消不会被通用异常边界改写为 500。API Key 创建同样区分 JSON 类型错误的 `invalid_request_body` 与成功绑定后的名称、过期时间稳定错误码，并保留 Access Token 凭据优先级。当前持久认证只创建 `Owner`，真实非 Owner 身份链路仍随角色管理切片验证。
- 同一 Katana 主机验证匿名 OpenAPI JSON 和 Swagger UI、固定 JSON 路径、Controller 路由与手工 OWIN token operation、公开 operation、受保护 operation 的唯一 Bearer 要求、API Key 创建/列表/撤销契约、SSE `Last-Event-ID`/`text/event-stream`、共享 Problem Details schema、token OAuth JSON 例外，以及仅 token endpoint 的 429 Problem Details。token operation 还必须锁定 `Authentication` tag、password-grant form data、无 refresh token、成功与 OAuth 错误响应的必填字段、未处理异常的 500 Problem Details、大小写等价 path 冲突失败，以及在同 path 已有其他 HTTP method 时保留并追加 `POST`。`/swagger/missing` 必须保持 404 而不进入 Admin SPA，文档请求不得调用控制台 Gateway、在线玩家 Query、玩家 Action 或审计 Trail。
- `DependencyRulesTests` 以源码规则验证 Bootstrap 通过唯一 composition root 创建经过验证的 Provider、使用局部 candidate 调用 `RegisterAndStart` 后才发布字段，并保护 DI 包归属、发布清单、Adapter 方向和唯一 `IModApi`；`SevenDaysGameLifecycleAdapterTests` 通过事件 seam 执行 `GameStartDone`、`WorldShuttingDown` 和 `GameShutdown` 回调，验证订阅顺序、逆序回滚、异常保留及 Dispose 只拥有订阅。真实静态 `ModEvents` 注册仍由官方进程 smoke 验证。
- 使用同一配置凭据重复启动并改变用户名/密码，断言数据库始终只有固定 `Subject=owner` 的一个引导用户；凭据不变时保留 Access Token，变化时旧凭据和旧 Access Token 同时失效。
- Bearer SSE 使用可控时钟和可变用户状态验证事件持续到达也不能推迟复验；Access Token/API Key 到期、撤销、用户禁用或角色不再被允许后不得继续写出受保护事件。
- 动态命令和 Owner 踢出都通过 Application 端口进入 SevenDays Adapter；Controller 不直接引用或访问游戏活对象。命令通过专属有界 FIFO，踢出通过专属 single-flight；宿主停止期间已经开始的真实操作排空仍需官方进程证据。

### 真实 7DTD 进程测试

- 使用未修改的官方 7DTD Dedicated Server `v3.0.1-b4` 运行程序集执行最小 smoke suite。
- 验证 Mod 加载后，游戏提供的 `0_TFP_Harmony` 已先恢复当前 Mod 的 `Assembly.Location`，且 `GameStartDone` 前 HTTP Host 已可访问；`GameStartDone` 后不会出现重复监听。当前持久身份和只读命令切片必须验证 Microsoft.Data.Sqlite/SQLitePCLRaw、DI、Bcl/Unsafe 与 Application DLL 从 Mod 发布目录加载，数据库重置后的首次 migration 与重复启动、同一引导 `owner`、password grant 返回 `expires_in=28800`、跨进程 Access Token、API Key 创建/使用/撤销、Basic 401、Access Token/API Key SSE、Welcome、`game-ready`、`version` 主线程真实输出、真实日志、`server-stopping`、正常关服、端口释放和服主配置/数据库保留。未来用户管理切片再验证无发布默认凭据和恢复加密传输要求。
- 所有支持的游戏版本或依赖矩阵变更都必须重新执行；编译期 publicized 程序集不能代替官方运行时证据。

2026-07-19 的 Windows `v3.0.1-b4` 人工 smoke 已验证当前健康切片：服务端日志在启动后 `3.409` 秒记录 OWIN 启动，`StartGame done` 在 `66.397` 秒；`Test-HealthEndpoint.cmd -TimeoutSeconds 10` 返回 HTTP 200 和 `{"status":"ok","product":"7DPanel","version":"0.1.0"}`；正常关服后进程退出、18080 端口不再监听。该记录尚未自动化或归档为候选发布证据。

2026-07-20 使用 Vite `8.1.5`/Rolldown 重新构建并发布同一远程环境，健康端点、哈希资源、Overview 页面、颜色模式和正常关服均通过，停止后 listener 不可用。该记录尚未自动化或归档为候选发布证据。

2026-07-20 在引入 `ISevenDaysLifecycleEvents`、`GameStartDone` 就绪状态和生命周期竞态修复后再次执行完整远程流程。OWIN 在启动后 `8.576` 秒启动，`StartGame done` 在 `119.732` 秒出现；日志没有 `Error while executing ModEvent` 或调用程序集无法识别警告。`/health` 返回精确三字段 JSON，Chromium 中 Overview 为 Fresh，主要静态资源和 `/api/v1/health` 均为 200；`/favicon.ico` 仍是既有 404。正常关服记录 `7DPanel OWIN host stopped`，进程退出且 listener 不可用。该证据仍是开发期人工 smoke，尚未自动化或归档为候选发布证据。

2026-07-20 在日志实现收缩为 `ConsoleLogService` 和 `ConsoleLogRuntime` 后重新执行当前二进制的完整远程流程。发布清单包含 `System.Threading.Channels.dll` 与 `System.Threading.Tasks.Extensions.dll`，不包含游戏提供的 `Assembly-CSharp.dll`、`LogLibrary.dll`、`UnityEngine.CoreModule.dll`、`Newtonsoft.Json.dll` 或 `System.Runtime.CompilerServices.Unsafe.dll`；服务端日志确认 Channels 从 Mod 目录加载，没有 `FileNotFoundException`、`TypeLoadException` 或 ModEvent 执行错误。健康端点返回 HTTP 200 和精确三字段 JSON；Chrome DevTools MCP 验证 Overview 为 Fresh，入口、三个哈希资源和 `/api/v1/health` 正常，唯一控制台错误仍是既有 `/favicon.ico` 404。正常关服摘要为 `accepted=185`、`consumed=185`、`droppedFull=0`、`rejectedStopping=0`、`consumerFailures=0`、`highWater=3`，随后 OWIN 停止、进程退出且 listener 不可用。该证据是开发期人工 smoke，尚未归档为候选发布证据；本轮真实负载没有触发容量饱和，饱和与超时分支由确定性自动化覆盖。

2026-07-20 在增加默认关闭的开发 SSE 后执行远程流程。首次启动中 7DPanel 在 `3.562s` 成功启动 OWIN 并在 7DTD 因无法连接 EOS backend 自行退出时完成 `accepted=37`、`consumed=37` 和 OWIN 释放；该外部平台失败被保留，未用重跑隐藏。第二次连续执行启动、健康和 SSE probe：`/health` 返回 HTTP 200 与精确三字段 JSON，`/api/v1/dev/console-logs/stream` 返回 `text/event-stream`，首个 `console-log` event 的 SSE id 与 JSON sequence 均为 1。初始化日志包含未认证风险警告；Telnet 正常关服后摘要为 `accepted=62`、`consumed=62`、`droppedFull=0`、`rejectedStopping=0`、`consumerFailures=0`、`highWater=3`，OWIN 停止、进程退出且 listener 不可用。服主原始 `config.json` 在 smoke 后按 SHA-256 比对逐字节恢复。该证据仍是开发期人工 smoke，不包含浏览器视觉检查，也未归档为候选发布证据。

2026-07-20 在采用 Microsoft DI 请求作用域后执行同一远程流程。Release Rebuild 为零警告，后端全量 87 项自动化通过；发布物包含 DI implementation/Abstractions、Channels 和 Tasks.Extensions，不包含游戏提供的 Bcl AsyncInterfaces、Unsafe、Newtonsoft.Json、LogLibrary 或 Unity 程序集。服务端日志确认 DI 两个程序集从 Mod 目录加载，初始化输出未认证开发 SSE 风险警告；`/health` 返回 HTTP 200 与精确三字段 JSON，SSE 返回连续的 `id` 和 camelCase `console-log` 数据。主动断开后 Telnet 正常关服，摘要为 `accepted=130`、`consumed=130`、`droppedFull=0`、`rejectedStopping=0`、`consumerFailures=0`、`highWater=3`，随后 OWIN 停止、进程退出且 listener 不可用。服主原始 `config.json` 在 smoke 后按 SHA-256 比对逐字节恢复；本轮按风险分级未执行无关浏览器视觉检查，证据仍是开发期人工 smoke，未归档为候选发布证据。

2026-07-21 首次执行认证生产 SSE smoke 时，`Microsoft.Owin.Security.OAuth.dll` 已从 Mod 目录加载，但 `OAuthBearerAuthenticationMiddleware` 在 self-host 默认创建 `DpapiDataProtector`，Unity Mono 抛出 `PlatformNotSupportedException`，OWIN 未启动且 90 秒健康门禁失败。该失败未用重跑隐藏：服务端随后正常关服，服主配置按原始 SHA-256 恢复；实现改为对 access token、authorization code 和 refresh token 显式提供拒绝自包含票据的 format，并增加一个调用 data protection 即抛错的 Katana 回归测试，确保唯一有效 Bearer 仍来自有界进程内 provider。

以下 2026-07-21 的认证 smoke 发生在移除 Basic 前，仅保留为 OAuth/OWIN/关闭行为的历史基线，不能证明当前 Access Token/API Key 认证版本。修复后的当时产品二进制先以 Release Rebuild 零警告和 117 项全量自动化重新发布；随后增加无 BOM/replay-live 去重证据测试，并把 OWIN 顺序对齐为“限流、request scope、OAuth/Basic/Bearer”，最终零警告门禁为 118 项，精确最终二进制再次完成真实流程。Windows `v3.0.1-b4` 日志确认 OAuth DLL 加载、OWIN 启动、`StartGame done` 和正常停止，未出现 `FileNotFoundException`、`TypeLoadException`、ModEvent 或 OWIN 启动错误。password grant 成功；Basic 与 Bearer 连接均以 Welcome 开始，并在 157 个事件中观察到 `console-log` 与 `game-ready`，最后 sequence 为 156；保持到关服的连接收到 `server-stopping` sequence 183。Chrome DevTools 强制刷新后入口、三个哈希资源和 `/api/v1/health` 均为 200，Overview 为 Fresh；唯一控制台错误仍是既有 `/favicon.ico` 404。关服摘要为 `accepted=181`、`consumed=181`、`droppedFull=0`、`rejectedStopping=0`、`consumerFailures=0`、`highWater=3`，随后进程退出、listener 不可用，临时凭据/备份被删除，服主 `config.json` 恢复后与原始 SHA-256 一致。该证据仍是开发期人工 smoke，未归档为候选发布证据。

2026-07-21 在持久 Token 切片中使用精确的 Microsoft.Data.Sqlite `10.0.9`、SQLitePCLRaw `2.1.11`、Bcl AsyncInterfaces `10.0.10` 和 Unsafe `6.1.2` 重新执行 Windows `v3.0.1-b4` 流程。该记录发生在当前认证变更前，30 分钟 Token、Basic 与旧 SQLite 发布布局只构成历史兼容证据。前几轮分别暴露了 Mod 根目录 native DLL 扫描、RuntimeInformation/ResourceManager facade 和默认 Batteries RID 探测的 Unity Mono 差异；最终发布物排除 `SQLitePCLRaw.batteries_v2.dll`，Persistence Adapter 从 RID 子目录加载 RuntimeInformation 与 `e_sqlite3.dll` 并显式绑定 dynamic provider。最终日志确认 Dapper、DbUp、Microsoft.Data.Sqlite、SQLitePCLRaw core/provider、DI、Bcl/Unsafe 均从 Mod 目录加载，`001_Authentication.sql` 首次执行成功，后续启动重复检查成功，OWIN 在 `GameStartDone` 前可用。`data/7dpanel.db` 创建成功；password grant 返回 30 分钟不透明 Bearer，受保护 SSE 以 Welcome 开始；正常停止并启动新的 7DTD 进程后，同一 Token 仍被接受并再次收到 Welcome。首次成功启动后测试机曾因外部 EOS `NoConnection` 在约 45 秒自行退出，该失败未归因于 Mod；后续两轮均通过脚本正常关服，临时 Token 文件已删除，服主旧版三字段 `config.json` 未修改。本轮没有前端变化，按风险分级未重复浏览器检查；证据仍是开发期人工 smoke，未归档为候选发布证据。

随后当前实现按已验证旧项目的兼容布局改回 Microsoft.Data.Sqlite 标准 bundle：删除 `SqliteRuntimeLoader`、`RuntimeInformationResourceManagerShim`、`SQLite3Provider_dynamic_cdecl.Setup` 和 `raw.SetProvider`，升级 SQLitePCLRaw bundle/native 到 `2.1.12`，发布五个 Framework64 宿主兼容程序集、标准 Batteries 与 Linux `dllmap`，并显式布置 Windows/Linux x64 native asset。为处理 7DTD 从内存加载 Mod 程序集时 `Assembly.Location` 为空的宿主差异，Bootstrap 还以 `Private=false` 引用游戏 `0_TFP_Harmony 2.13.0.0`，在 SQLite 组合前只应用当前 Mod 的位置补丁；7DPanel 发布物拒绝 `0Harmony.dll`。`dotnet restore` 无安全警告，定向依赖规则测试和本地 `Publish-Mod.ps1` 已通过，发布清单也独立确认必需项存在、禁止项缺失。完成这些本地验证时，旧项目的 Unity Mono 成功运行仍只能作为兼容参考，本仓库当前二进制尚未执行真实进程 smoke。

2026-07-23 在隔离 worktree 将 DbUp Core 升级到 `6.1.1`，并将 Microsoft.Data.Sqlite、Microsoft DI implementation/Abstractions 与 System.Threading.Channels 统一升级到 `10.0.10`。官方 nupkg 检查确认 DI、Abstractions 和 Channels 的 net462 资产声明 Microsoft.Bcl.AsyncInterfaces `10.0.10` 与 System.Threading.Tasks.Extensions `4.6.3` 最低依赖，当前 assets 与发布闭包解析为这组已验证版本；Microsoft.Data.Sqlite 仍解析当前 SQLitePCLRaw `2.1.12` 标准 bundle。升级后 `dotnet restore`、零警告 net48 Release Rebuild、320 项后端自动化和隔离 `Publish-Mod.ps1` 均通过；发布物包含 DbUp `6.1.1.0`、Microsoft.Data.Sqlite `10.0.10.0`、DI/Abstractions/Channels `10.0.0.10` 和 Bcl AsyncInterfaces `10.0.0.10`，保留双平台 native SQLite，并继续排除游戏提供的 Newtonsoft.Json、Harmony、LogLibrary、Unity 及旧 System.Data.SQLite/SQLite.Interop。与 `v3.0.1-b4` runtime 的同名扫描只发现既有 Framework64 兼容程序集，五个升级包没有游戏同名 DLL；该本地证据尚不能替代升级后二进制的 Windows/Linux 7DTD Mono 真实进程 smoke。

同日将隔离分支产物发布到远程 Windows `v3.0.1-b4`，执行 `Stop-Server.ps1 -> Publish-Mod.ps1 -> Start-Server.ps1 -> health/OpenAPI -> Stop-Server.ps1 -> unavailable check`。升级发布物成功启动，`/health` 返回精确三字段 HTTP 200，公开 `/swagger/v1/swagger.json` 返回 OpenAPI `3.0.0` 并包含 health path；随后 7DTD 进程退出且 health listener 不可达。首次停止调用在游戏已经接受关服后未能于 5 秒内匹配 Telnet acknowledgement，因此脚本以失败退出并留下启动任务为 Running；独立状态检查确认远程进程数为 0、health 已拒绝连接，第二次调用走无进程清理分支并将启动任务停止。当前最新 output log 的典型 `FileNotFoundException`、`TypeLoadException`、`MissingMethodException` 和 assembly load 错误扫描为 0，但没有输出五个升级程序集的逐项加载行；因此该证据只证明升级发布闭包可完成 Windows Mono 启动、公开 HTTP 和进程释放，不替代认证存储、命令、SQLite 写锁恢复、关服 acknowledgement 时序或 Linux 真实进程门禁。

同日当前二进制按 `Publish-Mod.cmd -> Start-Server.cmd -> Test-HealthEndpoint.cmd -> authenticated SSE -> Stop-Server.cmd -> unavailable check` 完成远程 Windows `v3.0.1-b4` smoke。该记录也发生在移除 Basic、改为默认 8 小时 Access Token 并加入 API Key 前。日志确认 `0Harmony` 从游戏 `0_TFP_Harmony` 加载，位置补丁在 `3.071s` 记录成功，database upgrade 在 `3.388s` 开始且无新 migration，OWIN 在 `7.775s` 启动，`StartGame done` 在 `65.392s` 出现。健康端点返回精确三字段 HTTP 200；password grant 成功，Basic/Bearer SSE 都返回 200 Welcome，Bearer 从游标 0 回放 161 个事件并包含 `console-log`、`game-ready`，关服连接收到 `server-stopping` id 190。停止摘要为 `accepted=188`、`consumed=188`、`droppedFull=0`、`rejectedStopping=0`、`consumerFailures=0`、`highWater=1`；随后 OWIN 停止、进程数为 0、健康端点不可达，兼容性错误扫描为 0。测试 Token 明文只存在于已结束的验证进程，数据库仅保存约 30 分钟到期的摘要；未直接修改服主配置或数据库清理记录。本轮没有前端变化，按风险分级未重复浏览器检查。

同日发布旧在线玩家查询切片后，在同一 Windows `v3.0.1-b4` 测试服连接一个受控玩家执行开发期人工 smoke。该记录发生在移除 Basic 前：`/health` 与 Owner Basic 认证的 `GET /api/v1/players/online` 均返回 HTTP 200，响应包含恰好一个玩家；根对象、玩家对象和平台身份对象均只出现批准字段，玩家名和主平台身份非空，响应中未发现 IP、位置、封禁、战斗统计或离线历史字段。随后 `Stop-Server.ps1` 收到 Telnet 正常关服确认并观察到远端进程停止，`Test-HealthEndpoint.ps1 -ExpectUnavailable` 确认 listener 不可达。该证据验证了已被事件投影替换的当时游戏字段映射、请求时主线程快照、Owner API 和停止释放路径的真实进程兼容性；本轮未执行空服务器分支、浏览器检查或候选发布归档，也不证明当前 `PlayerJoinedGame`、`SavePlayerData`、`PlayerDisconnected` 投影生命周期或当前认证版本，这些门禁仍由既有自动化和后续适用 smoke 分别承担。

2026-07-21 在引入 Application 控制台命令切片后重新执行 `Publish-Mod.cmd -> Start-Server.cmd -> authenticated command -> Stop-Server.cmd -> unavailable check`。发布门禁确认 Bootstrap、Application、Hosting、Web、SevenDays 和 Persistence 六个产品 DLL，7DTD 日志确认 `LSTY.SevenDPanel.Application.dll` 与其余当前程序集从 Mod 目录加载。第一轮启动因外部 EOS backend `NoConnection` 在 `GameStartDone` 前自行退出，7DPanel 仍排空 52 个已接受事件并释放 OWIN；该失败未归因于 Mod，也未用重跑隐藏。重试后命令端点在加载期连续 9 次返回 503 `game_not_ready`，游戏就绪后 `POST /api/v1/console/commands` 返回 HTTP 200、`command=version` 和 5 行真实输出，首行为 `Game version: V 3.0.1 (b4) Compatibility Version: V 3.0.1`。Telnet 正常关服后健康端点不可达；服主旧三字段 `config.json` 保持 71 字节且 SHA-256 为 `9130C9804B03BCC762FA5E7D91C2983214E6F08174B23EE7E007016F4E106A9B`。本轮没有前端变化，按风险分级未执行浏览器检查。

2026-07-23 在合并提交 `a98ad6b` 上执行 Windows `v3.0.1-b4` 闭环 smoke。发布物中的 Namotion.Reflection、NJsonSchema 和 NSwag 程序集均由 Unity Mono 从 Mod 目录加载；匿名 `/swagger` 返回 UI，`/swagger/v1/swagger.json` 返回有效 OpenAPI 3 文档并包含 `/api/v1/auth/token` 与 `/api/v1/console/commands`。认证 HTTP `version` 返回真实游戏输出并形成 `source=7dpanel-http` 审计；Telnet `version` 返回结果且形成 `source=network`、空 actor、`completion_kind=Completed` 审计，证明未替换的 `SdtdConsole.ExecuteAsync` 队列仍由 `SdtdConsole.Update` 消费。对 `7dpanel.db` 持有 `BEGIN IMMEDIATE` 写锁超过 5 秒 default timeout 后，唯一测试命令仍返回游戏结果，失败审计未落盘且日志记录 fail-open 告警；释放锁后的恢复命令与 `reason=store_failure`、`dropped_count>=1` gap 成功持久化。正常关服摘要为 `accepted=9`、`consumed=8`、`droppedFull=0`、`rejectedStopping=0`、`consumerFailures=1`、`highWater=1`、`unrecoveredGaps=0`、`unrecoveredDropped=0`，随后 OWIN 停止、进程退出、健康端点不可达；再次启动后健康、Swagger 和命令审计仍正常，并再次完成正常关服。日志没有 `FileNotFoundException`、`TypeLoadException`、ModEvent 或 OWIN 启停错误。该证据是开发期人工 smoke，尚未自动归档或提升为候选发布证据；未执行真实玩家踢出、浏览器 E2E 或 Linux 进程门禁。

### 浏览器端到端测试

- 覆盖引导 `Owner` 登录、未来用户与角色管理、状态页、玩家危险操作确认、即时/定时公告、备份、恢复确认和审计检索。
- 覆盖 loading、empty、offline、stale、forbidden、failed、unknown 和 draining 状态，确保界面不会把未知结果渲染为成功。
- 当前 Admin 健康切片已用 Chromium 手工验证桌面和 `390x844` 窄视口。开发期 Vite 同源代理配合本地响应 stub 验证了 offline、fresh 和成功采样 60 秒后的 stale 状态；真实 7DTD 生产 URL 另验证 `/`、三个哈希资源和 `/api/v1/health` 均返回 200，Overview 显示“服务器运行正常”、`7DPanel`、`v0.1.0` 和成功采样时间；仅 `/favicon.ico` 保持既有 404。当前登录/玩家/API Key Playwright suite 共 8 项，覆盖真实登录、API Key 生命周期、玩家页面、深链接、默认标签页会话刷新与关闭、显式浏览器会话重启恢复、身份显示、登出/到期/损坏记录清理、Authorization Header、URL/Storage/Cookie/控制台泄漏和 `390x844` 布局。它尚未在当前认证实现的受控真实 OWIN 环境执行；历史 6 项内存会话 Edge 结果不能替代这些合同。
- 对全部 P0 流程和表单分别以 `zh-CN` 与 `en` 运行关键 E2E；覆盖受支持浏览器语言首访、不支持语言回退 `en`、未认证与已认证入口切换、刷新后偏好持久化，以及切换时当前路由、筛选和安全表单输入保持。
- 验证产品文案、Nuxt UI 内置文案、Valibot 内置错误、日期与数字格式和稳定服务端错误码映射始终使用同一当前语言；缺失键、空白文案、翻译键泄漏和原始服务端异常文本均使测试失败。
- 验证 Steam ID、玩家名、服务器名、IP、坐标、路径、日志原文、审计标识和协议标识在语言切换前后保持原值，并在 320 CSS 像素宽度下检查英文文本扩展不会遮挡关键操作。

### 安全、性能与恢复测试

- 安全测试覆盖登录限速、Bearer Token 固定、到期与撤销、越权访问、敏感字段泄漏、引导凭据变更、并发用户管理和路径穿越；产品不采用 Cookie 认证，因此不设置 CSRF Token 门禁。
- 性能测试记录 API 延迟、主线程任务耗时、帧预算、队列深度和丢弃/拒绝计数；负载必须包含日志突发和管理请求并发。
- 恢复演练从已校验备份发起，跨越真实关服与重启，验证恢复结果、审计、回滚以及失败后再次启动行为。

## 测试环境与数据

| 环境 | 用途 | 最低要求 |
|---|---|---|
| 开发/CI .NET 环境 | 单元、SQLite、文件系统和 OWIN/API 集成测试 | 能以 C# `11.0` 构建 `net48`，并启用 Nullable Reference Types 和 Implicit Usings；平台与具体镜像在工程初始化时锁定 |
| Admin 前端开发/CI Node.js 环境 | Admin lint、应用与 Node 配置 typecheck、Vite `8.1.5`/Rolldown 生产构建 | 推荐 Node.js `24+`；`package.json` 精确兼容范围为 `^20.19.0 || ^22.13.0 || >=24.0.0`，使用 `pnpm@11.13.1` 和冻结锁文件 |
| Windows x64 真实服务端 | 发布 smoke、E2E、性能和恢复演练 | 官方 7DTD Dedicated Server `v3.0.1-b4`、隔离端口、临时世界 |
| Linux x64 真实服务端 | Native 兼容、发布 smoke、E2E 和恢复演练 | 官方 `v3.0.1-b4` Mono 运行时、区分大小写文件系统、临时世界 |
| 浏览器矩阵 | 管理流程 E2E | Windows 开发环境默认使用系统 Microsoft Edge；可显式选择系统 Google Chrome 或 Playwright 固定 Chromium；至少覆盖一个 Chromium 稳定版，扩大支持范围前补充对应浏览器 |

- 测试账号固定覆盖 `Owner`、`Admin`、`Viewer` 和未认证用户，不创建首版范围外的 `Player`，不使用生产密码、持久有效 Token、Steam 身份或真实 IP。
- 玩家、日志、存档和备份夹具使用合成数据；涉及 Steam ID/IP 的场景采用明确保留的测试值。
- 真实进程测试每次创建独立实例目录、数据目录、端口和世界名，不指向服主的现有服务器。
- 故障注入必须可重复并记录注入点，例如磁盘写入失败、文件占用、进程终止、HTTP 断开或主线程队列饱和。
- 测试产物至少包括测试报告、服务端与 Mod 日志、版本/程序集清单、失败截图，以及恢复演练的校验结果；不得采集密码或有效会话值。

## 命令与自动化

当前可运行的后端门禁包括依赖还原、Release 构建和测试；仓库级聚合命令见
[README 的 Test and Checks](../README.md#test-and-checks)。

Admin 当前快速门禁包括 lint、typecheck、Vitest 和 Vite `8.1.5`/Rolldown 生产构建，开发/CI 基线为 Node.js `24+`；其中
typecheck 同时运行 `vue-tsc -p ./tsconfig.app.json` 和
`tsc -p ./tsconfig.node.json`，工作目录和精确命令见
[Admin 应用验证说明](../frontend/apps/admin/README.md#verification)。
真实 OWIN Playwright 是单独的环境门禁，环境变量名和执行入口也由该 Admin README 拥有；缺少任一前置变量时 suite 明确 skip，不能计作 smoke 通过。

发布前先完成 Admin 构建，再运行 `backend\scripts\Publish-Mod.cmd`；脚本会校验
`dist/index.html` 与哈希资源，并只替换发布目录中的 `wwwroot/`。

发布、启动、停止和健康检查脚本的环境变量、参数、WinRM 前置条件及行为统一见
[后端脚本指南](../backend/scripts/README.md)。这些脚本只提供可重复操作入口，
不能单独证明真实进程 smoke 通过。验证必须保留 Mod 加载、`GameStartDone` 前的
OWIN 启动、精确健康响应、正常关服、端口释放和再次启动成功的服务端日志与命令结果。

构建默认使用 `7dtd-reference/v3.0.1-b4`。兼容性验证需要切换版本或引用根目录时，使用 MSBuild `/p:SevenDaysGameVersion=...` 和 `/p:SevenDaysReferenceRoot=...` 显式覆盖。后续仍需补充：

- 玩家动作审计已具备自动化锁竞争与启动恢复证据；仍需验证迁移失败、真实关服期间动作终态，以及自动化真实游戏事件和控制台日志突发。只读 `version` 的 Windows 主线程往返已经通过。
- 动态控制台命令、容量 32 HTTP FIFO、最终执行点 Harmony observation 和容量 256 fail-open SQLite 命令审计已有单元、SQLite、Katana、生命周期与发布物自动化证据；`a98ad6b` 的 Windows `v3.0.1-b4` 人工 smoke 进一步验证内置/第三方命令、HTTP/非 HTTP 来源审计、完整原文参数、多行输出、并发输出隔离、原生异步队列、真实 SQLite 故障告警/gap 恢复、关服排空和重复启停。仍需自动归档该流程、补充真实容量饱和与 Linux 进程门禁，并在当前认证二进制上执行适用 smoke。
- 在受控真实 OWIN Owner 环境执行现有 Playwright suite，覆盖 8 小时 Token、API Key 创建/一次性显示/复制/使用/撤销与 Basic 401，并增加桌面/移动踢出确认、提交锁定、稳定反馈和成功刷新；本轮完整 Admin 单元/组件测试、lint、typecheck 和 Vite 8 生产构建已经运行，但先解决 happy-dom teardown 与外部连接噪声，再将该套件视为稳定的发布门禁。
- 在 Windows `v3.0.1-b4` 受控玩家上执行真实踢出，保存玩家收到的批准原因、原生 API 返回后约 0.5 秒断开、在线列表更新和 `player_action_audit` 对应终态；在此之前不得把自动化 `Succeeded` 解释为真实玩家已经离线。
- 自动化 Windows/Linux 发布物组装、六个产品 DLL 与内容校验，尤其检查游戏 Harmony/JSON/LogLibrary/Unity 排除、旧 System.Data.SQLite/SQLite.Interop 禁止项、标准 SQLitePCLRaw Batteries 及配置、五个 Framework64 宿主兼容程序集、固定新版 Bcl/Unsafe、Microsoft DI/Channels/OAuth、Dapper/DbUp/Microsoft.Data.Sqlite、NSwag/NJsonSchema/Namotion/System.Text.Json 和双平台 SQLite Native RID；当前 Windows 发布脚本已在隔离临时目录校验六个产品 DLL、Admin `wwwroot`、所需托管依赖、11 个 OpenAPI 运行时程序集、Windows/Linux x64 native、`0Harmony.dll` 与 Mod 根目录禁止项和其他禁止资产，并移除 `Newtonsoft.Json.dll`。项目引用、SQLite provider、Harmony 应用顺序、DI/NSwag 包归属和 Adapter 方向已有本地测试门禁。
- 在 CI 中复用发布物路径和清单校验，确认 `7dtd-reference/` 不会被复制或打包。
- 将 Windows `v3.0.1-b4` 真实进程 smoke 自动化并归档服务端日志，同时建立 Linux x64 对应基线。

当前仓库尚未提供自有的文档校验脚本或 CI 任务。开发环境中安装的外部技能可以用于临时审计，但其安装路径属于使用者环境，不是项目的权威命令，不在本文档中固化。待仓库提供可移植的检查入口后，由最接近的所属 README 登记精确命令，在根 `README.md` 同步聚合入口或链接，并由本文档记录其门禁地位。

CI 应按“快速测试 -> 平台集成 -> 真实进程/浏览器 -> 恢复演练”分层；快速测试用于每次变更，真实进程和恢复演练至少用于候选发布及依赖、游戏版本、生命周期或备份代码变更。

## 发布门槛

首版候选发布必须同时满足：

1. 所有 P0 能力 `CAP-01` 至 `CAP-05` 及 `NFR-01` 至 `NFR-04` 均有自动化或可重复人工验收结果，且没有未说明的失败。
2. 单元、SQLite/文件系统、OWIN/API、浏览器 E2E 测试全部通过；不允许通过重新运行隐藏失败。
3. Windows x64 和 Linux x64 官方 `v3.0.1-b4` 进程 smoke 全部通过，程序集和发布物清单符合兼容矩阵，且不包含 `7dtd-reference/` 内容。
4. 正常关服、重复启动、队列饱和、备份损坏、磁盘失败、恢复中断和进程异常终止场景均产生明确、可追踪且可恢复的状态。
5. 至少一次从已校验备份完成跨重启恢复，并验证世界数据、回滚副本和审计记录。
6. 安全测试不存在可绕过 Header 认证/授权、Token 生命周期、角色边界或敏感信息保护的高危/严重问题。
7. 主线程帧预算和队列容量已有可测阈值且测试通过；具体数值必须在实现性能基线建立后写回本文档，未定义阈值不能视为通过性能门槛。
8. 文档审计无 ERROR；WARNING 必须有适用性判断和明确处理结论。

不稳定测试按失败处理。只有确认是测试基础设施问题、记录责任人和修复期限，并证明不影响对应需求与架构风险时，才可由发布负责人批准一次性例外；不得删除或静默跳过测试来放行。

## 已知缺口

| 缺口 | 影响与处理 |
|---|---|
| Windows 动态命令闭环 smoke 尚未自动归档 | `a98ad6b` 的 Windows `v3.0.1-b4` 人工 smoke 已覆盖内置/第三方命令、HTTP/非 HTTP 审计、原文参数、多行输出、并发输出隔离、原生异步队列、真实 SQLite 写锁 fail-open/gap 恢复、正常关服排空和重复启停；但流程和日志仍需自动归档，真实容量饱和及 Linux 对应门禁仍未执行，当前认证二进制也尚未复验。 |
| Owner 踢出自动化已通过但真实游戏动作未执行 | 后端与既有 Admin 自动化覆盖类型化端口、主线程身份重验、审计状态、HTTP/Problem Details、固定目标确认和未知结果；本轮未启动真实服务器，也未对玩家执行踢出。Windows `v3.0.1-b4` 的拒绝原因、约 0.5 秒断开、在线列表更新、SQLite 审计一致性和关服竞态仍不可宣称通过。 |
| 当前浏览器持久会话与踢出真实门禁未执行 | 当前 8 项 Playwright 场景已经定义默认标签页会话、显式浏览器会话、身份显示、登出/到期/损坏记录清理和既有 API Key/玩家合同，但没有 `SEVENDPANEL_ADMIN_URL`、`PANEL_USERNAME`、`PANEL_PASSWORD` 的受控 OWIN 环境，只有测试发现证据。真实踢出、`390x844` 踢出对话框、CSP 控制台、键盘/遮罩关闭锁定或成功刷新也未执行，不能从组件或定向 Vitest 推导。 |
| Admin Vitest teardown 存在未定位噪声 | `pnpm test` 的 23 个文件、267 项断言通过，但输出仍出现 happy-dom `AbortError`、fork worker 终止超时和外部连接 `ETIMEDOUT`。API Key 视图测试已 mock 业务请求，当前未能确认噪声来源；在根因和稳定修复完成前，它不构成干净的 CI 或发布证据。 |
| 当前认证版本尚无 Swagger Unity Mono 复验证据 | 公开 JSON/UI、路由、安全、SSE、Problem Details、无业务副作用、依赖所有权和隔离发布布局已通过 Windows Katana/本地门禁；`a98ad6b` 已在 Windows `v3.0.1-b4` Unity Mono 加载 NSwag/NJsonSchema/Namotion 并访问 `/swagger`，但本轮未在当前认证发布物复验。Linux 支持仍需对应 Linux smoke。 |
| 静态 `ModEvents` wrapper 没有进程内自动化测试 | 可替换事件边界已经执行三个 Adapter 回调及失败路径，Windows 真实 smoke 也已越过 `GameStartDone` 并完成正常关服；但调用程序集识别和官方 delegate 兼容性仍依赖人工真实进程证据。 |
| 主线程每帧预算、队列容量和性能阈值未量化 | 无法客观判定性能门槛；先在官方 Windows/Linux 进程建立空载与典型负载基线，再由架构和测试文档共同记录决定值。 |
| 在线保存后的快照一致性尚未证明 | 可能生成校验通过但语义不一致的存档；必须在实现备份前完成持续写入故障测试，否则采用维护窗口或平台快照。 |
| `InitMod` 恢复时机尚无真实文件占用证据 | 可能无法在世界加载前替换存档；恢复实现前用 `v3.0.1-b4` 进程验证并记录调用时序。 |
| Linux x64 的 SQLite Native 只有本地发布布局和旧项目运行经验 | 当前仓库尚无 Linux 7DTD 进程证据；在声明 Linux 支持或首个候选发布前建立 Linux x64 进程内 smoke 基线。 |
| 浏览器支持范围尚未形成产品决定 | 首版暂以 Chromium 稳定版建立 E2E 基线；扩大公开支持范围时更新本节和发布门槛。 |
