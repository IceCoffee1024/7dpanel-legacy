# Admin 健康概览与 OWIN 静态托管实施计划

> **面向智能体执行者：** 实施时必须使用 `superpowers:subagent-driven-development`（推荐）或 `superpowers:executing-plans`。以下步骤使用复选框跟踪。

**对应规格：** [Admin 健康概览与 OWIN 静态托管设计规格](../specs/2026-07-19-admin-health-slice-design.md)

**目标：** 将 Admin Overview 连接到真实的 `GET /api/v1/health` 契约，并在生产环境由 OWIN 提供构建后的 Admin 应用。

**架构：** Admin 应用拥有类型安全的同源 API Client 和 `useServerHealth` composable。开发环境通过 `.env.local` 配置 Vite proxy；生产环境使用相对 `/api/v1`。OWIN 提供静态资源，并保证 API 路由优先于静态文件和 SPA fallback。

**技术栈：** Vue 3、TypeScript、Vite、Vue Router、Nuxt UI、pnpm、ASP.NET Web API 2、Katana OWIN、.NET Framework 4.8。

## 全局约束

- 健康响应保持 `{ status: "ok", product: "7DPanel", version: "0.1.0" }`。
- `status: "ok"` 表示 7DPanel HTTP Host 存活，不表示 7DTD 已完成 `GameStartDone`。
- OWIN 在 `InitMod` 注册关闭事件后立即启动；游戏依赖能力仍以 `GameStartDone` 作为就绪边界。
- Web API JSON 属性统一使用 camelCase，测试必须区分大小写。
- 不引入全局 Store 或查询缓存库。
- 不伪造玩家、备份、身份或其他运营数据。
- 生产环境使用相对 `/api/v1`；只有开发环境可以配置 proxy 目标。
- `Stale` 是基于最后成功采样时间的客户端状态。
- API 路由必须先于静态文件和 SPA fallback。
- 保持现有 Bootstrap、Hosting、Adapter 依赖方向。
- 发布资源根目录固定为 `<ModDirectory>/wwwroot`；`Publish-Mod.ps1` 负责把已构建的 `frontend/apps/admin/dist` 复制到该目录。

---

### 任务 1：建立类型安全的健康 API Client 和 composable

**文件：**

- 新建：`frontend/apps/admin/src/api/serverHealth.ts`
- 新建：`frontend/apps/admin/src/composables/useServerHealth.ts`
- 修改：`frontend/apps/admin/src/pages/index.vue`

**接口：**

- `ServerHealth` 包含 `status: 'ok'`、`product: string`、`version: string`。
- `fetchServerHealth(signal?: AbortSignal): Promise<ServerHealth>` 请求 `/api/v1/health`，并拒绝非 `2xx` 或结构不合法的响应。
- `useServerHealth()` 暴露 `state`、`data`、`error`、`lastSuccessfulAt`、`refresh` 和 `dispose`。

- [x] **步骤 1：先建立响应验证测试或纯函数断言**

  覆盖合法响应、非 `2xx`、错误 JSON、缺少 `product`、缺少 `version` 和取消请求。当前项目尚未建立浏览器测试运行器时，不为本切片额外引入第二套测试框架；使用现有 lint、typecheck、build 门禁，并保持验证器为可独立测试的纯函数。

- [x] **步骤 2：实现独立于 Vue 的 API Client**

  使用 `fetch('/api/v1/health', { signal })`，将网络错误和 HTTP 错误映射为统一可序列化错误，并在返回前验证 JSON。

- [x] **步骤 3：实现 `useServerHealth`**

  只使用本地 Vue refs。刷新前取消旧请求；首次请求进入 `loading`；后续失败保留最后一次有效数据；没有成功采样时进入 `offline`；超过约定阈值后进入 `stale`。

- [x] **步骤 4：实现 Overview 状态呈现**

  用产品名、版本、最后成功采样时间以及 loading、fresh、stale、offline/error 状态替换当前未连接内容。保持现有 AppShell 和响应式布局边界。

- [x] **步骤 5：运行 Admin 检查**

  在 `frontend/apps/admin` 执行：

  ```powershell
  pnpm lint
  pnpm typecheck
  pnpm build
  ```

  预期：命令全部成功，生成的 `dist` 包含更新后的 Overview。

- [x] **步骤 6：提交前端切片**

  ```powershell
  git add frontend/apps/admin
  git commit -m "feat(admin): add server health overview"
  ```

### 任务 2：增加开发期 proxy 和本地配置

**文件：**

- 修改：`frontend/apps/admin/vite.config.ts`
- 新建：`frontend/apps/admin/.env.example`
- 保持未跟踪：`frontend/apps/admin/.env.local`
- 修改：`frontend/apps/admin/README.md`

- [x] **步骤 1：配置 Vite proxy**

  将 `/api` 代理到 `VITE_BACKEND_URL`，默认目标为 `http://127.0.0.1:18080`，不改写 `/api` 前缀。

- [x] **步骤 2：记录 `.env.local` 用法**

  提供本地默认值和远程开发目标示例，不提交凭据或机器特定路径。

- [x] **步骤 3：验证开发和生产 URL 分离**

  启动 Admin 开发服务器并确认浏览器请求仍为 `/api/v1/health`；确认上游地址只出现在 Vite proxy 行为中，而不进入生产 JavaScript。

- [x] **步骤 4：重新运行 Admin 检查**

  ```powershell
  pnpm lint
  pnpm typecheck
  pnpm build
  ```

### 任务 3：由 OWIN 提供 Admin 静态资源

**文件：**

- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OwinStartup.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/LSTY.SevenDPanel.Adapters.Web.csproj`
- 按需修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Outbound/Hosting/OwinWebHost.cs`
- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/ModMain.cs`
- 修改：`backend/scripts/Publish-Mod.ps1`
- 修改：`backend/scripts/README.md`

**接口：**

- `OwinStartup` 接收显式静态资源根目录。
- Web Adapter 增加 `Microsoft.Owin.StaticFiles` `4.2.3`，不引入其他静态文件服务器。
- 发布后的静态资源根目录为 `<ModDirectory>/wwwroot`。
- `/api/v1/health` 始终到达 `HealthController`。
- `/`、`/overview` 等客户端路由在资源存在时返回 `index.html`。
- 缺失静态资源返回普通 `404`，不暴露仓库路径。

- [x] **步骤 1：先扩展 OWIN 集成测试**

  在 `backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostTests.cs` 覆盖 `/api/v1/health`、`/`、`/overview`、缺失资源，以及静态路径与 API 路径冲突时的 API 优先级。

- [x] **步骤 2：实现显式资源根目录传递**

  Bootstrap 从 `modInstance.Path` 派生 `<ModDirectory>/wwwroot`，通过 `OwinStartup.Configure(app, assetRoot)` 传给 Web Adapter；运行时不得猜测仓库相对路径。资源目录缺失时保留 API 可用，但记录明确日志。

- [x] **步骤 3：配置静态资源和 SPA fallback**

  先注册 Web API，再注册静态文件，最后对浏览器文档路由做窄范围 fallback；`/api/*` 永远不能回退到 `index.html`。

- [x] **步骤 4：接入发布内容**

  `Publish-Mod.ps1` 在 `dotnet publish` 成功后检查 `frontend/apps/admin/dist/index.html`，清理并创建发布目录下的 `wwwroot`，复制 `dist` 内容并验证 `wwwroot/index.html` 和哈希资源存在。发布物包含四个产品 DLL，排除 `node_modules`、仓库源文件、`7dtd-reference` 和不属于发布策略的文件；不得清理服主的 `config.json` 或 `data/`。

- [x] **步骤 5：运行后端检查**

  在仓库根目录执行：

  ```powershell
  dotnet restore backend/7DPanel.sln
  dotnet build backend/7DPanel.sln --configuration Release --no-restore
  dotnet test backend/7DPanel.sln --configuration Release --no-build --no-restore
  ```

  预期：构建和测试成功，静态托管测试确认 API 优先级和 SPA fallback。

- [x] **步骤 6：提交托管切片**

  ```powershell
  git add backend
  git commit -m "feat(hosting): serve admin health slice from OWIN"
  ```

### 任务 4：验证发布后的浏览器到 Mod 链路

**使用文件：**

- 使用：`backend/scripts/Publish-Mod.cmd`
- 使用：`backend/scripts/Start-Server.cmd`
- 使用：`backend/scripts/Test-HealthEndpoint.cmd`
- 仅在建立新验证事实后修改：`docs/test.md`、`README.md`

- [x] **步骤 1：构建 Admin 生产资源**

  ```powershell
  cd frontend/apps/admin
  pnpm install --frozen-lockfile
  pnpm build
  ```

- [x] **步骤 2：生成并检查 Mod 发布目录**

  先执行 `frontend/apps/admin` 的 `pnpm build`，再执行现有发布包装脚本；确认 `<publishDir>/wwwroot/index.html` 和哈希资源存在，且不包含 `7dtd-reference` 内容。

- [x] **步骤 3：启动真实服务并验证 API**

  使用现有启动和健康检查脚本，确认 `/api/v1/health` 返回准确契约，服务端日志显示 OWIN 在 `GameStartDone` 前正常启动。

- [x] **步骤 4：验证浏览器路径**

  使用可用的 Chrome DevTools MCP 打开开发期 Vite URL，检查 accessibility snapshot、console、network 和窄视口。使用本地健康响应 stub 验证 `fresh`、`stale`、`offline` 状态；再打开真实发布后的 Mod URL，确认根页面、哈希资源和 `/api/v1/health` 均成功，Overview 显示产品、版本、成功采样时间和 Fresh，桌面与 `390x844` 窄视口无控制台错误。

- [x] **步骤 5：只记录已验证事实**

  只有发布链路真实验证后，才将结果提升到 `docs/architecture.md` 和 `docs/test.md`；除非批准的未来边界发生变化，不修改目标蓝图。

### 任务 5：修正真实进程暴露的生命周期与 JSON 契约

**文件：**

- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/ModMain.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Inbound/Lifecycle/SevenDaysGameLifecycleAdapter.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OwinStartup.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/DependencyRulesTests.cs`

- [x] **步骤 1：增加精确 JSON 契约失败测试**

  解析真实健康 JSON，精确断言 `status`、`product`、`version`，并拒绝 PascalCase 属性。

- [x] **步骤 2：增加生命周期失败测试**

  通过架构规则测试验证 Bootstrap 在 `InitMod` 中调用 `RegisterAndStart`，生命周期适配器不再注册 `GameStartDone`，并且先注册关闭事件再启动 Host；真实关闭行为继续由进程 smoke 验证。

- [x] **步骤 3：实现全局 camelCase**

  在 `OwinStartup.ConfigureApi` 配置 `JsonFormatter.SerializerSettings.ContractResolver`，统一使用 camelCase，不在单个 DTO 上重复字段属性。

- [x] **步骤 4：将 OWIN 启动移动到 `InitMod`**

  保持 OWIN 由 `ModHost` 持有；先注册关闭事件，再启动 Host。移除 `GameStartDone` 对 OWIN 的重复启动职责，不提前启用未来依赖游戏对象的能力。

- [x] **步骤 5：运行本地与真实进程验证**

  运行后端完整 build/tests，发布后重启真实 7DTD；确认 `GameStartDone` 前后只有一个监听器、健康响应为精确 camelCase、真实 Overview 显示 Fresh，最后优雅关服并确认端口释放。

## 完成标准

- Admin 开发模式通过 Vite proxy 访问真实健康端点。
- 生产 Admin 资源由 OWIN 从发布目录提供。
- `/api/v1/health` 保持 API 所有权，不被 SPA fallback 截获。
- Overview 区分 loading、fresh、stale 和 offline。
- Admin lint、typecheck、build 通过。
- 后端 build 和 tests 通过。
- 开发期浏览器检查确认前端开发代理链路，真实浏览器到 Mod 链路已由 7DTD 生产 URL 验证。
- OWIN 在 `GameStartDone` 前启动且不会重复监听，健康响应精确使用 camelCase，关服后进程和端口释放。

### 执行记录

- 2026-07-19：`pnpm lint`、`pnpm typecheck`、`pnpm build` 通过；后端 `dotnet restore`、Release build 和 21 项测试通过。
- 2026-07-19：真实 Katana 集成测试覆盖静态文件、SPA fallback、API 优先级和未知 API 404；`Publish-Mod.cmd` 验证 `wwwroot/index.html` 与哈希资源进入发布目录。
- 2026-07-19：开发期 Vite 链路以本地 stub 验证桌面和 `390x844` 窄视口的 offline、fresh、stale；真实 7DTD 生产 URL 另验证根页面、三个哈希资源和 `/api/v1/health` 均返回 200，Overview 显示 Fresh 且控制台无消息。
- 2026-07-19：真实服务日志显示 OWIN 在启动后 `3.409` 秒启动，早于 `StartGame done` 的 `66.397` 秒；健康辅助脚本返回精确 camelCase 契约；优雅关服后进程退出、18080 端口不再监听。
- 2026-07-19：经用户明确授权，前端切片与 OWIN 托管切片已分别提交；本次执行记录随文档同步独立提交。
