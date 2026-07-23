---
state: Current
document_role: Implementation Plan
primary_spec: ../specs/2026-07-23-persistent-browser-session-design.md
last_updated: "2026-07-23"
---

# Admin 浏览器持久会话实施计划

> **面向智能体执行者：** 实施时必须使用 `superpowers:executing-plans`，按任务执行规格符合性与代码质量检查；用户已明确要求不使用子智能体。以下步骤使用复选框跟踪。

**对应规格：** [Admin 浏览器持久会话设计规格](../specs/2026-07-23-persistent-browser-session-design.md)

**目标：** 让未选择“保持登录”的 Admin 会话跨当前标签页刷新，让选择后的会话跨标签页关闭和浏览器重启恢复，并在账户菜单显示服务端确认的用户名和角色。

**架构：** Katana password grant 在标准 OAuth Token 响应中附加服务端身份元数据；Admin Auth Feature 增加严格、版本化的会话 codec 和浏览器 Storage Repository，由 Pinia Store 统一拥有恢复、保存、到期、登出和跨标签页同步。生产 OWIN 只为 Admin 文档响应添加最小 Content Security Policy，受保护 API 继续只接受 Header Bearer。

**技术栈：** .NET Framework 4.8、C# 11、ASP.NET Web API 2、Katana OWIN/OAuth 4.2.3、NSwag/NJsonSchema、xUnit v3、Vue 3 Composition API、TypeScript、Pinia 3、Vue Router、Nuxt UI 4、Vitest、Vue Test Utils、Playwright 1.61、pnpm 11。

## 全局约束

- 网站 Access Token 继续是不透明、可撤销、默认有效 8 小时的 `7dp_t_` Header-only Bearer；本变更不延长 Token、不增加 Cookie、CSRF Token、refresh token、JWT 或 silent refresh。
- password grant 成功响应必须返回服务端确认的 `username` 和 `role`；角色只允许 `Owner`、`Admin` 或 `Viewer`，前端不得用展示元数据替代服务端授权。
- Storage 记录键固定为 `7dpanel.auth.session.v1`，记录固定为 `{ version: 1, token, expiresAt, username, role }`；密码、Authorization Header 前缀、Subject、完整 API Key 和危险操作状态不得进入记录。
- 未选择“保持登录”只写 `sessionStorage`；选择后只写 `localStorage`。启动时有效 `localStorage` 优先，两处异常并存时删除 `sessionStorage`。
- 登出、到期、401、损坏记录和持久会话删除事件必须清除相关内存及 Storage；403、网络错误、5xx 和 SSE 断开不得清除仍可能有效的会话。
- Storage 不可用时登录降级为当前页面内存会话，并显示非阻断提示；不得把持久化失败改写为登录失败。
- `localStorage` 的同源脚本可读风险由生产 CSP、无第三方运行时脚本、无 `unsafe-eval`、依赖复核和泄漏测试补偿；不得增加客户端加密包装。
- 不安装通用 Pinia 持久化插件，不引入新的认证、存储、加密或状态管理依赖，不修改 `7dtd-reference/`。
- 每项生产行为必须先写失败测试并确认 RED，再做最小实现并复跑同一检查；实现稳定后只运行一次相应聚合门禁。
- 本计划不授权 `git commit`、`git push`、`git reset`、`git revert`、远程发布、启动或停止真实服务器。计划中的提交命令仅在用户另行授权后执行。

---

### 任务 1：扩展 password grant 身份响应

**文件：**

- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/Authentication/PanelOAuthAuthorizationServerProvider.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OpenApi/PanelOpenApiDocumentProcessor.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostTests.cs`

**接口：**

- 消费：`PanelClaimsIdentityFactory` 已写入的 `ClaimTypes.Name` 与 `ClaimTypes.Role`。
- 产出：`PanelOAuthAuthorizationServerProvider.TokenEndpoint(OAuthTokenEndpointContext)` 把身份加入 `AdditionalResponseParameters`；OpenAPI 200 schema 要求 `username`/`role`。

- [x] **步骤 1：写 password grant 与 OpenAPI 失败测试**

  在 `OwinWebHostTests` 的 token operation schema 断言中把必须字段改为五项，并在真实 Katana password grant 流程断言服务端身份：

  ```csharp
  Assert.Equal(
      new[] { "access_token", "expires_in", "role", "token_type", "username" },
      successSchema!["required"]!.Values<string>().OrderBy(value => value).ToArray());
  Assert.Equal("test-owner", (string?)tokenPayload["username"]);
  Assert.Equal("Owner", (string?)tokenPayload["role"]);
  ```

  在 `OAuth_invalid_grant_remains_an_oauth_protocol_response` 增加：

  ```csharp
  Assert.Null(errorPayload["username"]);
  Assert.Null(errorPayload["role"]);
  ```

- [x] **步骤 2：运行 RED**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj -c Release --filter "FullyQualifiedName~Openapi|FullyQualifiedName~Password_grant|FullyQualifiedName~OAuth_invalid_grant"
  ```

  预期：成功响应与 schema 缺少 `username`/`role`，新增断言失败；无编译错误。

- [x] **步骤 3：实现 Katana 响应元数据**

  在 Provider 中导入 `System.Security.Claims`，并实现 Katana 官方 `TokenEndpoint` 扩展点：

  ```csharp
  public override Task TokenEndpoint(OAuthTokenEndpointContext context)
  {
      if (context.Identity == null) return Task.CompletedTask;

      var username = context.Identity.FindFirst(ClaimTypes.Name)?.Value;
      var role = context.Identity.FindFirst(ClaimTypes.Role)?.Value;
      if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(role))
      {
          throw new InvalidOperationException(
              "The validated panel identity is missing its name or role claim.");
      }

      context.AdditionalResponseParameters["username"] = username;
      context.AdditionalResponseParameters["role"] = role;
      return Task.CompletedTask;
  }
  ```

  `OAuthAuthorizationServerHandler` 会在该回调完成后把 `AdditionalResponseParameters` 合并到最终 JSON；不要自行重写响应正文。

- [x] **步骤 4：更新 OpenAPI schema 并验证 GREEN**

  在 `CreateTokenResponseSchema()` 增加字符串属性和 required：

  ```csharp
  schema.Properties["username"] = CreateStringProperty("Authenticated user name.");
  var role = CreateStringProperty("Current panel role.");
  role.Enumeration.Add("Owner");
  role.Enumeration.Add("Admin");
  role.Enumeration.Add("Viewer");
  schema.Properties["role"] = role;
  schema.RequiredProperties.Add("username");
  schema.RequiredProperties.Add("role");
  ```

  复跑步骤 2，预期全部通过。

- [x] **步骤 5：提交任务 1（需用户授权）**

  ```powershell
  git add backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/Authentication/PanelOAuthAuthorizationServerProvider.cs backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OpenApi/PanelOpenApiDocumentProcessor.cs backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostTests.cs
  git commit -m "feat(auth): return authenticated user metadata"
  ```

### 任务 2：为 Admin 文档添加生产 CSP

**文件：**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/AdminDocumentSecurityHeadersMiddleware.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OwinStartup.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostTests.cs`

**接口：**

- 消费：`OwinStartup` 的 Admin asset 分支与 `ShouldUseSpaFallback` 路径所有权。
- 产出：Admin 根、无扩展名 History 路由和 `/index.html` 响应包含固定 `Content-Security-Policy`；API、Swagger 与静态资产不被错误加头。

- [x] **步骤 1：写真实 Katana 失败测试**

  扩展 `Admin_assets_spa_routes_and_api_precedence_run_in_real_katana_host`：

  ```csharp
  const string ExpectedCsp =
      "default-src 'self'; base-uri 'self'; object-src 'none'; " +
      "frame-ancestors 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
      "img-src 'self' data:; font-src 'self'; connect-src 'self'; form-action 'self'";

  Assert.Equal(ExpectedCsp, root.Headers.GetValues("Content-Security-Policy").Single());
  Assert.Equal(ExpectedCsp, deepLink.Headers.GetValues("Content-Security-Policy").Single());
  Assert.Equal(ExpectedCsp, index.Headers.GetValues("Content-Security-Policy").Single());
  Assert.False(asset.Headers.Contains("Content-Security-Policy"));
  Assert.False(api.Headers.Contains("Content-Security-Policy"));
  Assert.False(openApi.Headers.Contains("Content-Security-Policy"));
  ```

  同时断言 CSP 不含第三方来源、`unsafe-eval`、`http:` 或 `https:`。

- [x] **步骤 2：运行 RED**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj -c Release --filter FullyQualifiedName~Admin_assets_spa_routes
  ```

  预期：HTML 响应当前没有 `Content-Security-Policy`，断言失败。

- [x] **步骤 3：实现专用 Middleware**

  新文件只拥有生产 Admin 文档响应头：

  ```csharp
  internal sealed class AdminDocumentSecurityHeadersMiddleware : OwinMiddleware
  {
      internal const string ContentSecurityPolicy =
          "default-src 'self'; base-uri 'self'; object-src 'none'; " +
          "frame-ancestors 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
          "img-src 'self' data:; font-src 'self'; connect-src 'self'; form-action 'self'";

      public AdminDocumentSecurityHeadersMiddleware(OwinMiddleware next) : base(next) { }

      public override async Task Invoke(IOwinContext context)
      {
          if (IsAdminDocumentRequest(context.Request.Method, context.Request.Path.Value))
          {
              context.Response.Headers.Set("Content-Security-Policy", ContentSecurityPolicy);
          }
          await Next.Invoke(context);
      }

      private static bool IsAdminDocumentRequest(string method, string path)
      {
          if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
              && !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)) return false;
          return string.Equals(path, "/index.html", StringComparison.OrdinalIgnoreCase)
              || OwinStartup.ShouldUseSpaFallback(method, path);
      }
  }
  ```

  为同命名空间调用把 `ShouldUseSpaFallback` 从 `private` 改为 `internal`。只在 asset root 已存在后、SPA fallback 与 FileServer 之前注册：

  ```csharp
  app.Use<AdminDocumentSecurityHeadersMiddleware>();
  ```

- [x] **步骤 4：验证 GREEN 与回归边界**

  复跑步骤 2，再运行：

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj -c Release --filter "FullyQualifiedName~OwinWebHostTests"
  ```

  预期：Admin 文档 CSP 断言和 OWIN 全部集成测试通过。

- [x] **步骤 5：提交任务 2（需用户授权）**

  ```powershell
  git add backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/AdminDocumentSecurityHeadersMiddleware.cs backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OwinStartup.cs backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostTests.cs
  git commit -m "feat(web): add Admin document CSP"
  ```

### 任务 3：建立版本化会话 Codec 与 Storage Repository

**文件：**

- 新建：`frontend/apps/admin/src/features/auth/model/authSession.ts`
- 新建：`frontend/apps/admin/src/features/auth/model/authSession.test.ts`
- 新建：`frontend/apps/admin/src/features/auth/model/authSessionRepository.ts`
- 新建：`frontend/apps/admin/src/features/auth/model/authSessionRepository.test.ts`
- 修改：`frontend/apps/admin/src/features/auth/index.ts`

**接口：**

```ts
export type AuthRole = 'Owner' | 'Admin' | 'Viewer'
export type SessionPersistence = 'tab' | 'browser'

export interface AuthSession {
  token: string
  expiresAt: number
  username: string
  role: AuthRole
}

export interface AuthSessionRepository {
  restore(now: number): AuthSession | null
  save(session: AuthSession, persistence: SessionPersistence): boolean
  clear(): void
  subscribe(listener: (session: AuthSession | null) => void): () => void
}

export const AUTH_SESSION_STORAGE_KEY = '7dpanel.auth.session.v1'
export function parseAuthSession(value: string | null, now: number): AuthSession | null
export function serializeAuthSession(session: AuthSession): string
export function createBrowserAuthSessionRepository(options: {
  now: () => number
  getLocalStorage: () => Storage
  getSessionStorage: () => Storage
  eventTarget: Pick<Window, 'addEventListener' | 'removeEventListener'>
}): AuthSessionRepository
```

- [x] **步骤 1：写 Codec 失败测试**

  `authSession.test.ts` 覆盖合法值和严格拒绝：

  ```ts
  expect(parseAuthSession(JSON.stringify({
    version: 1,
    token: '7dp_t_id.secret',
    expiresAt: 2_000,
    username: 'admin',
    role: 'Owner',
  }), 1_000)).toEqual({
    token: '7dp_t_id.secret',
    expiresAt: 2_000,
    username: 'admin',
    role: 'Owner',
  })
  ```

  分别断言 `null`、损坏 JSON、额外/缺失字段、`version !== 1`、非 `7dp_t_` Token、空用户名、非有限整数/已到期时间和未知角色返回 `null`。

- [x] **步骤 2：运行 Codec RED**

  ```powershell
  Set-Location frontend/apps/admin
  pnpm exec vitest run src/features/auth/model/authSession.test.ts
  ```

  预期：模块不存在，测试失败。

- [x] **步骤 3：实现严格 Codec 并验证 GREEN**

  Codec 先 `JSON.parse`，要求 `Object.keys(record).sort()` 精确等于 `['expiresAt','role','token','username','version']`；要求 `version === 1`、`token.startsWith('7dp_t_')`、`Number.isSafeInteger(expiresAt)`、`expiresAt > now`、trim 后用户名非空且角色在固定集合。序列化时只生成五个批准字段。复跑步骤 2，预期通过。

- [x] **步骤 4：写 Repository 失败测试**

  使用内存 `Storage` fake 与可触发 `StorageEvent` 的 event target，覆盖：

  ```ts
  expect(repository.save(session, 'tab')).toBe(true)
  expect(sessionStorage.getItem(AUTH_SESSION_STORAGE_KEY)).not.toBeNull()
  expect(localStorage.getItem(AUTH_SESSION_STORAGE_KEY)).toBeNull()

  expect(repository.save(session, 'browser')).toBe(true)
  expect(localStorage.getItem(AUTH_SESSION_STORAGE_KEY)).not.toBeNull()
  expect(sessionStorage.getItem(AUTH_SESSION_STORAGE_KEY)).toBeNull()
  ```

  还要覆盖：有效 local 优先并删除 session、损坏 local 清理后恢复 session、两者过期均返回 null、读取/写入/删除抛错不向外传播、`save` 失败返回 false、只响应版本化键、持久记录事件替换会话、删除/无效事件清本标签页 session、unsubscribe 后不再通知。

- [x] **步骤 5：运行 Repository RED、实现并验证 GREEN**

  ```powershell
  pnpm exec vitest run src/features/auth/model/authSessionRepository.test.ts
  ```

  实现时获取 Storage 对象和每次 Storage 操作都分别放入 `try/catch`；`restore()` 按 local 后 session 顺序调用同一 codec；`save()` 先尽力清两处再写唯一目标；`storage` listener 只处理版本化认证键，并在能够取得 local Storage 时同时要求 `event.storageArea` 匹配。收到任意该键事件先删除当前标签页 session，再把合法 `newValue` 或 `null` 通知 Store。复跑命令，预期通过。

- [x] **步骤 6：导出稳定接口并验证任务 3（提交需用户授权）**

  从 `features/auth/index.ts` 只导出上述类型、常量、Codec、Repository 工厂，不导出测试 fake。运行：

  ```powershell
  pnpm exec vitest run src/features/auth/model/authSession.test.ts src/features/auth/model/authSessionRepository.test.ts
  pnpm typecheck
  ```

  预期全部 exit 0。经授权后提交：

  ```powershell
  git add frontend/apps/admin/src/features/auth
  git commit -m "feat(frontend): add browser auth session storage"
  ```

### 任务 4：让 Auth Store 拥有恢复与完整生命周期

**文件：**

- 修改：`frontend/apps/admin/src/features/auth/api/auth.ts`
- 修改：`frontend/apps/admin/src/features/auth/api/auth.test.ts`
- 修改：`frontend/apps/admin/src/features/auth/model/authStore.ts`
- 修改：`frontend/apps/admin/src/features/auth/model/authStore.test.ts`
- 修改：`frontend/apps/admin/src/app/router.test.ts`
- 修改：`frontend/apps/admin/src/features/auth/index.ts`

**接口：**

```ts
export type AccessToken = AuthSession

export interface AuthStoreDependencies {
  now: () => number
  loginRequest: typeof loginWithPassword
  sessionRepository: AuthSessionRepository
}

login(username: string, password: string, rememberLogin: boolean): Promise<void>
username: ShallowRef<string | null>
role: ShallowRef<AuthRole | null>
persistenceWarning: ShallowRef<boolean>
```

- [x] **步骤 1：扩展响应 Parser 的失败测试**

  把既有合法响应改为：

  ```ts
  expect(parseAccessToken({
    access_token: '7dp_t_id.secret',
    token_type: 'bearer',
    expires_in: 90,
    username: 'admin',
    role: 'Owner',
  }, 1_000)).toEqual({
    token: '7dp_t_id.secret',
    expiresAt: 91_000,
    username: 'admin',
    role: 'Owner',
  })
  ```

  增加缺失/空用户名、错误类型、未知角色用例，预期 `AuthError('invalid-response')`。

- [x] **步骤 2：运行 Parser RED、实现并验证 GREEN**

  ```powershell
  Set-Location frontend/apps/admin
  pnpm exec vitest run src/features/auth/api/auth.test.ts
  ```

  `parseAccessToken` 复用 `AuthRole` 固定集合，返回完整 `AccessToken`；不接受表单 username 作为响应后备。复跑命令，预期通过。

- [x] **步骤 3：写 Store 恢复与持久化失败测试**

  在测试中提供可观测 fake repository，覆盖：

  ```ts
  const repository = createFakeRepository(restoredSession)
  const useStore = createAuthStore({ now: () => 1_000, loginRequest, sessionRepository: repository })
  const store = useStore()
  expect(store.$state).toMatchObject({
    token: restoredSession.token,
    expiresAt: restoredSession.expiresAt,
    username: 'admin',
    role: 'Owner',
  })
  ```

  完整覆盖：Store 创建时同步恢复并设 timer；过期恢复保持匿名；未选择保存 `'tab'`、选择保存 `'browser'`；每次新登录先清旧状态；保存失败仍 authenticated 且 `persistenceWarning=true`；成功登录使用服务端 username/role；失败登录不保留旧身份；到期、`logout()`、`expireSession()` 清内存和 repository；403 不调用 expire；外部有效 session 替换内存与 timer；外部 null 只清内存、不循环写 repository；Store dispose 取消订阅。

- [x] **步骤 4：运行 Store RED**

  ```powershell
  pnpm exec vitest run src/features/auth/model/authStore.test.ts src/app/router.test.ts
  ```

  预期：依赖和 Store 尚不支持 repository、username、role、rememberLogin，测试失败。

- [x] **步骤 5：实现 Store 最小状态机**

  把会话赋值集中为内部函数，避免登录、恢复和跨标签页各自维护字段：

  ```ts
  function applySession(session: AuthSession) {
    clearSessionExpiryTimer()
    token.value = session.token
    expiresAt.value = session.expiresAt
    username.value = session.username
    role.value = session.role
    sessionExpiryTimer = setTimeout(
      expireSession,
      Math.max(0, session.expiresAt - dependencies.now()),
    )
  }

  function clearInMemorySession() {
    clearSessionExpiryTimer()
    token.value = null
    expiresAt.value = null
    username.value = null
    role.value = null
  }

  function expireSession() {
    clearInMemorySession()
    dependencies.sessionRepository.clear()
  }
  ```

  Store setup 先 `restore(now)` 并同步 `applySession`，再订阅外部变更；外部 null 调 `clearInMemorySession`，不能回写 repository。`login` 开始调用 `expireSession()`，成功后先 `applySession(accessToken)` 再 `save(accessToken, rememberLogin ? 'browser' : 'tab')`，保存 false 只设置 warning。使用 `onScopeDispose(unsubscribe)` 清理订阅和 timer。

- [x] **步骤 6：接入默认浏览器 Repository 并验证 GREEN**

  默认 `useAuthStore` 使用：

  ```ts
  const browserSessionRepository = createBrowserAuthSessionRepository({
    now: Date.now,
    getLocalStorage: () => window.localStorage,
    getSessionStorage: () => window.sessionStorage,
    eventTarget: window,
  })
  ```

  不得在模块初始化时提前求值 `window.localStorage` 或 `window.sessionStorage`；禁用 Storage 的浏览器可以在 getter 求值时抛出 `SecurityError`，必须由 Repository 捕获并降级。

  Router 测试不再直接只写 token/expiry；测试 helper 写入完整 username/role，或为 Router 创建注入 fake repository 的 Store。复跑步骤 4，预期通过。

- [x] **步骤 7：提交任务 4（需用户授权）**

  ```powershell
  pnpm exec vitest run src/features/auth src/app/router.test.ts
  pnpm typecheck
  git add frontend/apps/admin/src/features/auth frontend/apps/admin/src/app/router.test.ts
  git commit -m "feat(frontend): restore persisted auth sessions"
  ```

  预期定向测试和 typecheck 通过；只有获得授权后执行 Git 命令。

### 任务 5：交付保持登录与账户身份 UI

**文件：**

- 修改：`frontend/apps/admin/src/features/auth/ui/LoginForm.vue`
- 修改：`frontend/apps/admin/src/features/auth/ui/LoginForm.test.ts`
- 修改：`frontend/apps/admin/src/app/AppShell.vue`
- 新建：`frontend/apps/admin/src/app/AppShell.test.ts`

**组件边界：** `LoginForm` 只拥有用户名、密码和 `rememberLogin` 表单输入并调用 Store；`AppShell` 只读取 Store 的 username/role，构造 Nuxt UI 账户菜单和登出命令。组件不读写 Storage。

- [x] **步骤 1：写 LoginForm 失败测试**

  使用 Nuxt UI 4 `UCheckbox` 的可访问 label 锁定默认值和提交：

  ```ts
  const remember = wrapper.get('button[role="checkbox"][aria-label="保持登录"]')
  expect(remember.attributes('aria-checked')).toBe('false')
  await remember.trigger('click')
  await wrapper.get('form').trigger('submit')
  expect(store.login).toHaveBeenCalledWith('Owner', 'top-secret-password', true)
  ```

  失败登录后断言复选框仍选中、用户名保留、密码清空；repository 保存失败但登录成功时断言 `useToast().add({ title: '会话无法持久保存，刷新或关闭页面后需要重新登录', color: 'warning' })`，随后仍进入目标路由。

- [x] **步骤 2：运行 LoginForm RED、实现并验证 GREEN**

  ```powershell
  Set-Location frontend/apps/admin
  pnpm exec vitest run src/features/auth/ui/LoginForm.test.ts
  ```

  在 `reactive` credentials 旁增加 `const rememberLogin = shallowRef(false)`，模板在密码后增加：

  ```vue
  <UCheckbox
    v-model="rememberLogin"
    aria-label="保持登录"
    label="保持登录"
    description="关闭浏览器后，在访问令牌有效期内继续登录"
  />
  ```

  提交调用 `auth.login(credentials.username, password, rememberLogin.value)`；成功后若 `auth.persistenceWarning`，在路由替换前发 warning toast。复跑命令，预期通过。

- [x] **步骤 3：写 AppShell 失败测试**

  新建组件测试，用真实 Pinia 状态和最小 Nuxt UI/Router stub 验证：

  ```ts
  expect(wrapper.get('[data-testid="account-menu-trigger"]').text()).toContain('server-owner')
  expect(wrapper.text()).toContain('server-owner')
  expect(wrapper.text()).toContain('Owner')
  expect(wrapper.get('[data-testid="account-menu-trigger"]').attributes('aria-label'))
    .toBe('server-owner 账号')
  ```

  再覆盖 80 字符用户名不会改变按钮固定宽度类、折叠时 label 隐藏但 aria-label 保留，以及触发“退出登录”后 `auth.logout()` 并 `router.replace('/login')`。

- [x] **步骤 4：实现账户菜单并验证 GREEN**

  `accountItems` 使用 Nuxt UI 4 的 label 项和 separator：

  ```ts
  const accountItems = computed<DropdownMenuItem[][]>(() => [[
    { label: auth.username ?? '', type: 'label' },
    { label: auth.role ?? '', type: 'label' },
  ], [{
    label: '退出登录',
    icon: 'i-lucide-log-out',
    onSelect: logout,
  }]])
  ```

  触发按钮使用 `:aria-label="`${auth.username} 账号`"`、`:label="auth.username ?? ''"`、`data-testid="account-menu-trigger"`，并通过 `truncate min-w-0`/固定现有 sidebar 宽度避免长用户名撑开。复跑：

  ```powershell
  pnpm exec vitest run src/features/auth/ui/LoginForm.test.ts src/app/AppShell.test.ts
  pnpm typecheck
  ```

  预期全部通过。

- [x] **步骤 5：提交任务 5（需用户授权）**

  ```powershell
  git add frontend/apps/admin/src/features/auth/ui/LoginForm.vue frontend/apps/admin/src/features/auth/ui/LoginForm.test.ts frontend/apps/admin/src/app/AppShell.vue frontend/apps/admin/src/app/AppShell.test.ts
  git commit -m "feat(frontend): add persistent login controls"
  ```

### 任务 6：真实浏览器验收、聚合门禁与文档提升

**文件：**

- 修改：`frontend/apps/admin/tests/e2e/admin-online-players.spec.ts`
- 修改：`frontend/apps/admin/tests/e2e/admin-api-keys.spec.ts`
- 修改：`docs/design.md`
- 修改：`docs/architecture.md`
- 修改：`docs/test.md`
- 修改：`docs/architecture/admin-frontend-target-blueprint.md`

**接口：** 当前文档只在实现与对应验证通过后提升登录身份响应、CSP、两种浏览器会话和账户菜单；PRD 与主规格已经拥有批准决策，不在本任务重新设计。

- [x] **步骤 1：改写真实 E2E 的旧内存会话断言**

  把 `admin-online-players.spec.ts` 的“认证材料不进入 Storage”和“刷新回登录”改为目标合同：

  ```ts
  await page.getByLabel('保持登录').uncheck()
  await loginOwner(page)
  await page.reload()
  await expect(page).toHaveURL(/\/players$/)
  await expect(page.getByRole('button', { name: `${username} 账号` })).toBeVisible()
  expect(await page.evaluate(() => localStorage.getItem('7dpanel.auth.session.v1'))).toBeNull()
  expect(await page.evaluate(() => sessionStorage.getItem('7dpanel.auth.session.v1'))).not.toBeNull()
  ```

  关闭该 page 并在同一 context 新建 page，访问 `/players` 必须回登录。密码、API Key、Cookie、URL 和控制台仍不得包含敏感值；认证 Storage 只允许版本化会话记录。

- [x] **步骤 2：增加“保持登录”与清理 E2E**

  在独立测试中勾选保持登录，断言 `localStorage` 有唯一记录、`sessionStorage` 无记录；新 page 恢复身份。测试从 fixture 接收 `browser`，用 `const storageState = await context.storageState()` 后显式创建 `const restartedContext = await browser.newContext({ storageState })` 模拟同一浏览器配置重启；新 context 的页面访问 `/players` 仍登录并显示 username/role，并在 `finally` 中关闭该 context。执行退出后，当前和其他 page 都回到登录，两种 Storage 均无认证键；损坏/过期记录不会发送 Authorization 请求，也不产生重定向循环。

  `admin-api-keys.spec.ts` 的时钟到期测试增加两种 Storage 清空断言，并保持重新登录可用。

- [x] **步骤 3：运行前端定向与聚合门禁**

  ```powershell
  Set-Location frontend/apps/admin
  pnpm exec vitest run src/features/auth src/app/AppShell.test.ts src/app/router.test.ts
  pnpm test
  pnpm lint
  pnpm typecheck
  pnpm build
  ```

  预期全部 exit 0；Vitest 无失败或未处理异常，生产构建不含密码、有效 Token 或远程脚本。

- [x] **步骤 4：运行后端聚合门禁**

  ```powershell
  Set-Location ../../..
  dotnet restore backend/7DPanel.sln
  dotnet build backend/7DPanel.sln --configuration Release --no-restore
  dotnet test backend/7DPanel.sln --configuration Release --no-build --no-restore
  ```

  预期 restore/build/test 全部成功、0 失败。

- [ ] **步骤 5：发布到受控 OWIN 环境并运行真实 Edge E2E 与 CSP 浏览器检查（需用户授权）**

  本步骤会发布 Mod、启动或重启真实 7DTD 测试进程并在结束时停服，必须先获得用户对该环境的明确授权。使用 `backend/.env.local` 现有目标和 `frontend/apps/admin/.env.local` 的 `SEVENDPANEL_ADMIN_URL`、`PANEL_USERNAME`、`PANEL_PASSWORD`，按顺序运行：

  ```powershell
  Set-Location <repository-root>
  backend\scripts\Stop-Server.cmd
  backend\scripts\Test-HealthEndpoint.cmd -ExpectUnavailable -TimeoutSeconds 5
  backend\scripts\Publish-Mod.cmd
  backend\scripts\Start-Server.cmd
  backend\scripts\Test-HealthEndpoint.cmd -TimeoutSeconds 90
  Set-Location frontend/apps/admin
  pnpm test:e2e
  Set-Location ../../..
  backend\scripts\Stop-Server.cmd
  backend\scripts\Test-HealthEndpoint.cmd -ExpectUnavailable -TimeoutSeconds 5
  ```

  如果服务器原本明确处于停止状态，可以省略第一组 Stop/Unavailable，但必须记录该前置状态。无论 E2E 成功或失败，都必须正常执行最后的 Stop/Unavailable；不得以强制杀进程替代正常关服。

  预期发布、启动和健康检查成功；所有登录、玩家和 API Key 场景真实执行且 0 skip、0 fail；Edge 控制台无 CSP violation；根路径、`/players`、`/api-keys` 刷新、API、SSE 和静态资产均可用；最终停服释放监听端口。缺少授权、环境或 suite skip 时，只能报告本地实现门禁通过，不能声称真实 OWIN 浏览器验收完成。

- [x] **步骤 6：更新 Current 文档**

  - `docs/design.md`：把刷新重登替换为默认标签页会话、可选保持登录、Storage 降级提示和账户菜单身份显示。
  - `docs/architecture.md`：记录 OAuth 身份字段、Auth Session Repository、两种 Storage、跨标签页同步、CSP 和剩余 XSS 风险。
  - `docs/test.md`：记录实际通过的单元、Katana、聚合和 Edge 结果；未运行或 skip 的证据保持缺口。
  - `docs/architecture/admin-frontend-target-blueprint.md`：删除已失效的“只使用内存会话”目标，保持它仍是 Target 而非当前证据。

  运行：

  ```powershell
  git diff --check
  git status --short
  ```

  预期只有本计划范围内的实现、测试和文档文件；无空白错误、占位符或生成发布物。

- [x] **步骤 7：提交任务 6（需用户授权）**

  ```powershell
  git add frontend/apps/admin/tests/e2e/admin-online-players.spec.ts frontend/apps/admin/tests/e2e/admin-api-keys.spec.ts docs/design.md docs/architecture.md docs/test.md docs/architecture/admin-frontend-target-blueprint.md
  git commit -m "test(auth): validate persistent browser sessions"
  ```

## 完成定义

- password grant 成功响应和 OpenAPI 均包含服务端确认的 `username` 与 `role`，错误响应不泄露身份元数据。
- 未选择“保持登录”时刷新保持登录、关闭标签页后清除；选择后关闭标签页或重启浏览器仍在 Token 原有效期内恢复。
- 登录、恢复和跨标签页同步只产生一个版本化认证记录；损坏、到期、登出和 401 清除内存及相关 Storage，403 和暂时网络失败不误清会话。
- Storage 不可用时当前页面仍可登录并明确提示降级；密码和完整 API Key 从不进入认证记录。
- 账户菜单显示服务端确认的用户名和角色，长用户名与 `390x844` 布局无重叠或水平溢出。
- 生产 Admin 文档带批准的 CSP，不包含第三方脚本来源或 `unsafe-eval`；API、Swagger、SSE 和静态资产行为不回归。
- 后端 Release build/全量测试、Admin unit/lint/typecheck/build 和真实 Edge E2E 全部通过；真实 E2E skip 不满足完成定义。
- Current 文档只记录已经实现并验证的事实，主规格与本计划不替代代码和测试证据。