---
state: Current
document_role: Implementation Plan
primary_spec: ../specs/2026-07-21-admin-online-players-design.md
last_updated: "2026-07-22"
---

# Admin 登录与在线玩家页面实施计划

> **面向智能体执行者：** 实施时必须使用 `superpowers:subagent-driven-development`（推荐）或 `superpowers:executing-plans`，逐任务执行并在每个任务后评审。以下步骤使用复选框跟踪。

**对应规格：** [Admin 登录与在线玩家页面设计规格](../specs/2026-07-21-admin-online-players-design.md)

**目标：** 为当前唯一 `Owner` 提供独立 Admin 登录页、仅存内存的 Bearer 会话和受保护的在线玩家页面，并建立首个 Admin 单元、组件与真实浏览器验证门禁。

**架构：** Pinia Setup Store 只拥有跨路由认证会话；显式接收同一 Pinia 实例的 Router guard 保护 `/` 与 `/players`。共享 Fetch 原语处理同源传输和 Problem Details，玩家 Feature 自己验证 DTO，并由页面级 `useOnlinePlayers` 管理 10 秒轮询、取消、single-flight 与 Fresh/Stale/Offline 状态；玩家快照不进入 Pinia。

**技术栈：** Vue `3.5.40`、Vue Router `5.2.0` 文件路由、Pinia、TypeScript `6.0.3`、Nuxt UI `4.10.0`、Vite `8.1.5`、Vitest `4.1.6`、Vue Test Utils、happy-dom、Playwright、pnpm `11.13.1`。

## 全局约束

- 使用 Vue 3 Composition API 和 `<script setup lang="ts">`；路由页面只组合 Feature，不直接发起 Fetch。
- `/login` 公开；`/` 与 `/players` 要求有效内存会话。健康 API 保持匿名。
- Token 只能进入 Pinia 内存 state 和 `Authorization: Bearer` Header；不得进入 URL、Cookie、`localStorage`、`sessionStorage`、日志、异常文本、测试快照或构建产物。
- 密码只存在于登录表单局部 state 和 `login(username, password)` 调用栈；提交完成或失败后清空。
- Pinia 只保存客户端会话，不保存玩家、健康状态或其他服务器权威数据；不安装持久化插件或 Pinia Colada。
- 共享 API Client 固定请求相对同源 `/api/v1`，支持 `AbortSignal`、有限超时和稳定错误映射，不展示原始后端异常。
- 玩家查询首次进入立即执行，随后每 10 秒刷新；页面隐藏暂停，恢复后立即刷新；自动和手动刷新共用客户端 single-flight。
- 刷新失败保留最后成功快照和原 `capturedAtUtc`，明确进入 Stale；没有旧快照时才进入 Offline。
- 401 清除会话并保留安全站内返回目标；403 保留会话并显示 Forbidden；不自动重放请求。
- 玩家页面只显示批准字段，不实现搜索、排序控件、分页、详情、位置、IP、玩家动作、SSE 或审计。
- 沿用 `058da3c` Template 派生的 Nuxt UI Dashboard 视觉语言；桌面使用全宽表格，窄屏使用字段明确的行详情，不使用横向滚动完成关键流程。
- 每个生产行为先写一个因缺少该行为而失败的测试，观察正确 RED 后才实现；不使用 snapshot-only 测试代替行为断言。
- 导入、导出、编译或测试发现错误不计为 RED；遇到此类错误时只补齐抛出 `NotImplemented` 的最小可编译骨架，再运行测试，必须观察到预期行为断言失败后才能实现生产逻辑。
- 不修改后端接口、认证策略、角色或响应字段；若真实契约与规格冲突，停止实施并更新规格，不在前端猜测兼容。
- 本计划不授权 `git commit`、`git push`、`git reset` 或 `git revert`；逐任务检查点只记录验证结果与工作区 diff，只有用户另行明确授权后才能执行相应 Git 操作。

---

### 任务 1：建立测试运行器和共享 HTTP 错误边界

**文件：**

- 修改：`frontend/apps/admin/package.json`
- 修改：`frontend/apps/admin/pnpm-lock.yaml`
- 修改：`frontend/apps/admin/vite.config.ts`
- 修改：`frontend/apps/admin/tsconfig.app.json`
- 删除：`frontend/apps/admin/tests/.gitkeep`
- 新建：`frontend/apps/admin/src/shared/api/http.ts`
- 新建：`frontend/apps/admin/src/shared/api/http.test.ts`
- 新建：`frontend/apps/admin/src/shared/testing/setup.ts`

**接口：**

- 产出 `HttpErrorCode = 'aborted' | 'network' | 'timeout' | 'http' | 'invalid'`。
- 产出 `HttpError`，字段为 `code`、可选 `status`、`problemCode`、`traceId`。
- 产出 `requestJson<T>(path, options): Promise<T>`；`path` 必须以 `/api/v1/` 开始，调用方可传 `method`、`headers`、`body`、`signal`、`timeoutMs`。
- 不在共享层读取 Pinia 或理解认证、OAuth、玩家 DTO。

- [ ] **步骤 1：安装最小测试依赖并声明脚本**

  在 `frontend/apps/admin` 执行：

  ```powershell
  pnpm add -D vitest@4.1.6 @vue/test-utils happy-dom
  ```

  在 `package.json` 增加一次性门禁和独立 E2E 脚本：

  ```json
  {
    "scripts": {
      "test": "vitest run",
      "test:unit": "vitest run"
    }
  }
  ```

  在 `vite.config.ts` 的现有配置中增加测试配置，复用同一 Vue/Nuxt UI 转换链：

  ```ts
  test: {
    environment: 'happy-dom',
    setupFiles: ['./src/shared/testing/setup.ts'],
    clearMocks: true,
    restoreMocks: true,
  },
  ```

  `src/**/*.ts` 已覆盖测试文件，因此不重复增加 include pattern；只在 `tsconfig.app.json` 的 `types` 中加入 `vitest/globals`。`setup.ts` 只在每个测试后执行 Vue Test Utils cleanup 和恢复真实计时器，不注册产品 Store 或全局测试数据。

- [ ] **步骤 2：写共享 HTTP 边界失败测试**

  在 `http.test.ts` 用 `vi.stubGlobal('fetch', vi.fn())` 覆盖以下独立行为：

  ```ts
  test('returns validated JSON from an API path', async () => {
    vi.mocked(fetch).mockResolvedValue(new Response('{"ok":true}', {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }))

    await expect(requestJson<{ ok: boolean }>('/api/v1/example')).resolves.toEqual({ ok: true })
    expect(fetch).toHaveBeenCalledWith('/api/v1/example', expect.objectContaining({ credentials: 'omit' }))
  })

  test('maps Problem Details without exposing detail', async () => {
    vi.mocked(fetch).mockResolvedValue(new Response(JSON.stringify({
      status: 503,
      code: 'game_not_ready',
      detail: 'internal text',
      traceId: 'trace-1',
    }), { status: 503, headers: { 'Content-Type': 'application/problem+json' } }))

    const error = await requestJson('/api/v1/example').catch(value => value)
    expect(error).toMatchObject({ code: 'http', status: 503, problemCode: 'game_not_ready', traceId: 'trace-1' })
    expect(String(error)).not.toContain('internal text')
  })

  test('rejects paths outside the versioned API root', async () => {
    await expect(requestJson('https://example.test/api')).rejects.toMatchObject({ code: 'invalid' })
    expect(fetch).not.toHaveBeenCalled()
  })
  ```

  另写取消、超时、网络失败、非 JSON 成功响应和调用方 signal 已取消的测试。超时测试使用 fake timers，断言内部 controller 取消请求且错误码为 `timeout`；调用方取消映射为 `aborted`。

- [ ] **步骤 3：运行测试并确认正确 RED**

  ```powershell
  pnpm test -- src/shared/api/http.test.ts
  ```

  如果测试先因 `src/shared/api/http.ts` 或导出缺失而报错，只创建签名正确且抛出 `NotImplemented` 的最小骨架并重跑。正确 RED 必须是上述行为断言失败，不能是 Vite、happy-dom、导入、编译或测试发现错误。

- [ ] **步骤 4：实现最小共享 HTTP 边界**

  `requestJson` 必须：

  ```ts
  export interface RequestJsonOptions extends Omit<RequestInit, 'signal'> {
    signal?: AbortSignal
    timeoutMs?: number
  }

  export async function requestJson<T>(path: string, options: RequestJsonOptions = {}): Promise<T>
  ```

  - 拒绝不以 `/api/v1/` 开始或包含 scheme/host 的路径；
  - 用内部 `AbortController` 合并调用方取消与默认 10 秒 timeout；
  - 固定 `credentials: 'omit'`，不读取 Cookie；
  - 非 `2xx` 只提取 Problem Details 的字符串 `code` 和 `traceId`；
  - OAuth 非 Problem Details body 仍只映射 HTTP 状态，不保存 `error_description`；
  - 成功 body 必须是可解析 JSON；
  - `finally` 清理 timeout 与外部 signal listener。

- [ ] **步骤 5：重跑定向测试和静态门禁**

  ```powershell
  pnpm test -- src/shared/api/http.test.ts
  pnpm lint
  pnpm typecheck
  ```

  预期：HTTP 测试全部通过，lint/typecheck 无错误。

- [ ] **步骤 6：记录基础设施检查点**

  ```powershell
  git diff --check -- frontend/apps/admin
  git status --short
  ```

  预期：只包含本任务文件和此前保留的文档改动；保持未暂存、未提交。

### 任务 2：实现 OAuth 登录边界和 Pinia 内存会话

**文件：**

- 新建：`frontend/apps/admin/src/features/auth/api/auth.ts`
- 新建：`frontend/apps/admin/src/features/auth/api/auth.test.ts`
- 新建：`frontend/apps/admin/src/features/auth/model/authStore.ts`
- 新建：`frontend/apps/admin/src/features/auth/model/authStore.test.ts`
- 新建：`frontend/apps/admin/src/features/auth/index.ts`
- 修改：`frontend/apps/admin/package.json`
- 修改：`frontend/apps/admin/pnpm-lock.yaml`
- 修改：`frontend/apps/admin/src/main.ts`

**接口：**

- `loginWithPassword(username, password, signal?): Promise<AccessToken>`。
- `AccessToken = { token: string; expiresAt: number }`，`expiresAt` 为 Unix 毫秒。
- `createAuthStore(dependencies): StoreDefinition` 创建可隔离测试的 Store definition；生产导出 `useAuthStore = createAuthStore(defaultDependencies)`。
- `useAuthStore` 产出 `status`、`error`、`isAuthenticated`、`authorizationHeader`、`login`、`logout`、`expireSession`。
- `AuthStoreDependencies = { now: () => number; loginRequest: typeof loginWithPassword }`；测试创建专用 Store definition，生产默认使用真实时间和 `loginWithPassword`。

- [ ] **步骤 1：安装 Pinia 并写 OAuth 解析失败测试**

  在 `frontend/apps/admin` 执行：

  ```powershell
  pnpm add pinia@^3.0.4
  ```

  在 `auth.test.ts` 先测试纯函数 `parseAccessToken(value, now)`：

  ```ts
  test('accepts an opaque bearer token and computes expiry', () => {
    expect(parseAccessToken({
      access_token: 'opaque-token',
      token_type: 'Bearer',
      expires_in: 1800,
    }, 1_000)).toEqual({ token: 'opaque-token', expiresAt: 1_801_000 })
  })

  test.each([
    {},
    { access_token: '', token_type: 'bearer', expires_in: 1800 },
    { access_token: 'token', token_type: 'mac', expires_in: 1800 },
    { access_token: 'token', token_type: 'bearer', expires_in: 0 },
  ])('rejects invalid token response %#', (value) => {
    expect(() => parseAccessToken(value, 0)).toThrow(AuthError)
  })
  ```

  `loginWithPassword` 测试断言 body 是 `URLSearchParams`，只含 `grant_type=password`、`username`、`password`，请求不含 Authorization Header，且 OAuth `invalid_grant`、429、网络/超时和无效成功响应映射为不含原始描述的 `AuthErrorCode`。

- [ ] **步骤 2：运行 OAuth 测试并确认 RED**

  ```powershell
  pnpm test -- src/features/auth/api/auth.test.ts
  ```

  如果先遇到文件或导出缺失，只补齐签名正确且抛出 `NotImplemented` 的最小骨架并重跑；正确 RED 必须是 OAuth 行为断言失败。

- [ ] **步骤 3：实现 OAuth API 最小代码并转 GREEN**

  `AuthErrorCode` 固定为：

  ```ts
  export type AuthErrorCode = 'invalid-credentials' | 'rate-limited' | 'unavailable' | 'invalid-response'
  ```

  `loginWithPassword` 使用 `/api/v1/auth/token`、`application/x-www-form-urlencoded;charset=UTF-8` 和 `requestJson<unknown>`。任何 400/401 OAuth 失败统一为 `invalid-credentials`；429 为 `rate-limited`；网络、超时和 5xx 为 `unavailable`；成功结构错误为 `invalid-response`。

  ```powershell
  pnpm test -- src/features/auth/api/auth.test.ts
  ```

- [ ] **步骤 4：写 Pinia Store 失败测试**

  每个测试使用 `setActivePinia(createPinia())`。覆盖：

  ```ts
  test('stores only token metadata after login', async () => {
    const loginRequest = vi.fn().mockResolvedValue({ token: 'opaque-token', expiresAt: 2_000 })
    const useTestAuthStore = createAuthStore({ now: () => 1_000, loginRequest })
    const store = useTestAuthStore(pinia)

    await store.login('owner', 'secret')

    expect(store.isAuthenticated).toBe(true)
    expect(store.authorizationHeader).toBe('Bearer opaque-token')
    expect(JSON.stringify(store.$state)).not.toContain('owner')
    expect(JSON.stringify(store.$state)).not.toContain('secret')
  })

  test('expires and clears a known-expired token', async () => {
    let now = 1_000
    const useTestAuthStore = createAuthStore({
      now: () => now,
      loginRequest: vi.fn().mockResolvedValue({ token: 'opaque-token', expiresAt: 2_000 }),
    })
    const store = useTestAuthStore(pinia)
    await store.login('owner', 'secret')
    now = 2_000

    expect(store.isAuthenticated).toBe(false)
    expect(store.authorizationHeader).toBeNull()
  })
  ```

  另覆盖提交中重复 login 拒绝、统一错误、不保留旧 Token、logout、expireSession 和全新 Pinia 无恢复数据。

- [ ] **步骤 5：运行 Store 测试并确认 RED**

  ```powershell
  pnpm test -- src/features/auth/model/authStore.test.ts
  ```

  预期：因 Store 未实现而失败。

- [ ] **步骤 6：实现 Setup Store 并注册 Pinia**

  `createAuthStore(dependencies)` 返回 `defineStore('auth', () => { ... })`；生产导出 `useAuthStore = createAuthStore({ now: Date.now, loginRequest: loginWithPassword })`。测试使用独立 Pinia 和由同一工厂生成的专用 Store definition，不修改模块级依赖。不要把密码保存到 ref。

  在 `main.ts`：

  ```ts
  const pinia = createPinia()
  app.use(pinia)
  app.use(router)
  ```

  Pinia 必须在首次 Router navigation 前可用；后续 Router 工厂会显式接收该实例。

- [ ] **步骤 7：重跑认证测试和静态门禁**

  ```powershell
  pnpm test -- src/features/auth
  pnpm lint
  pnpm typecheck
  ```

  预期：认证测试全部通过，Store state 扫描不含测试用户名或密码。

- [ ] **步骤 8：记录认证模型检查点**

  ```powershell
  git diff --check -- frontend/apps/admin
  git status --short
  ```

  预期：只增加本任务认证文件及依赖改动；保持未暂存、未提交。

### 任务 3：增加安全路由守卫、登录页和认证后外壳

**文件：**

- 新建：`frontend/apps/admin/src/app/router.ts`
- 新建：`frontend/apps/admin/src/app/router.test.ts`
- 新建：`frontend/apps/admin/src/features/auth/model/safeRedirect.ts`
- 新建：`frontend/apps/admin/src/features/auth/model/safeRedirect.test.ts`
- 新建：`frontend/apps/admin/src/features/auth/ui/LoginForm.vue`
- 新建：`frontend/apps/admin/src/features/auth/ui/LoginForm.test.ts`
- 新建：`frontend/apps/admin/src/pages/login.vue`
- 修改：`frontend/apps/admin/src/pages/index.vue`
- 修改：`frontend/apps/admin/src/App.vue`
- 修改：`frontend/apps/admin/src/app/AppShell.vue`
- 修改：`frontend/apps/admin/src/main.ts`

**接口：**

- `createAdminRouter(pinia, history?)` 创建生产或测试 Router。
- `resolveSafeRedirect(raw, router): string` 返回合法站内 fullPath，否则 `/players`。
- 页面通过 route meta `requiresAuth: true` 标记保护边界。
- `LoginForm` 无 props；使用真实 auth Store，成功后 `router.replace(safeTarget)`。

- [ ] **步骤 1：写安全 redirect 失败测试**

  ```ts
  test.each([
    ['/players', '/players'],
    ['/?from=players', '/?from=players'],
    ['//evil.test/path', '/players'],
    ['https://evil.test/path', '/players'],
    ['players', '/players'],
    ['', '/players'],
  ])('maps %s to %s', (raw, expected) => {
    expect(resolveSafeRedirect(raw, router)).toBe(expected)
  })
  ```

  另验证不存在的内部路由回退 `/players`。

- [ ] **步骤 2：运行 redirect 测试并确认 RED，再实现转 GREEN**

  ```powershell
  pnpm test -- src/features/auth/model/safeRedirect.test.ts
  ```

  实现只接受单个 `/` 开始、不能以 `//` 开始且 `router.resolve(raw).matched.length > 0` 的路径。重跑同一命令，预期全部通过。

- [ ] **步骤 3：写 Router 守卫失败测试**

  用 `createMemoryHistory()`、独立 Pinia 和最小测试路由覆盖：

  ```ts
  test('redirects an unauthenticated protected route to login', async () => {
    const router = createAdminRouter(pinia, createMemoryHistory())
    await router.push('/players')
    await router.isReady()
    expect(router.currentRoute.value.fullPath).toBe('/login?redirect=/players')
  })

  test('allows an authenticated owner to enter a protected route', async () => {
    await authenticateTestStore(pinia)
    const router = createAdminRouter(pinia, createMemoryHistory())
    await router.push('/players')
    await router.isReady()
    expect(router.currentRoute.value.path).toBe('/players')
  })
  ```

  另覆盖已认证访问 `/login`、过期 Token、合法 redirect 和恶意 redirect。

- [ ] **步骤 4：运行 Router 测试并确认 RED，再实现守卫**

  ```powershell
  pnpm test -- src/app/router.test.ts
  ```

  `createAdminRouter` 使用现有 `routes` 和 `createWebHistory()` 默认值；guard 内调用 `useAuthStore(pinia)`。`main.ts` 创建一次 Pinia，再把同一实例传给 Router。不要在模块顶层调用 Store。

- [ ] **步骤 5：写 LoginForm 失败组件测试**

  使用真实 Pinia Store 和 memory Router，mock auth API；断言：

  - 用户名和密码输入拥有 label 与 autocomplete；
  - 提交只调用一次 Store login；
  - submitting 时按钮固定且不可重复提交；
  - 成功 replace 到安全目标；
  - 失败保留用户名、清空密码并显示统一文案；
  - 429 使用“请求过于频繁，请稍后重试”；
  - 页面 HTML、Pinia state、Router URL 均不含密码。

  ```powershell
  pnpm test -- src/features/auth/ui/LoginForm.test.ts
  ```

  如果先遇到组件或页面缺失，只补齐可挂载的最小组件骨架并重跑；正确 RED 必须是表单行为断言失败。

- [ ] **步骤 6：实现登录页和认证后 App Shell**

  `App.vue` 根据 `route.meta.public` 只对登录页直接渲染 `RouterView`，其他路由渲染 `AppShell`。`login.vue` 只组合品牌和 `LoginForm`，并声明公开 meta：

  ```vue
  <route lang="json">
  { "meta": { "public": true } }
  </route>
  ```

  在 `index.vue` 增加文件路由 meta：

  ```vue
  <route lang="json">
  { "meta": { "requiresAuth": true } }
  </route>
  ```

  `AppShell` footer 保留 AppearanceMenu，并增加 `Owner` 账号菜单和“退出登录”；登出调用 Store 后 `router.replace('/login')`。不显示登录用户名。

- [ ] **步骤 7：重跑路由/登录测试与 Admin 门禁**

  ```powershell
  pnpm test -- src/app/router.test.ts src/features/auth
  pnpm lint
  pnpm typecheck
  pnpm build
  ```

  预期：测试通过，生成 route map 包含 `/login`，现有 Overview 仍可构建。

- [ ] **步骤 8：记录登录路由检查点**

  ```powershell
  git diff --check -- frontend/apps/admin
  git status --short
  ```

  预期：只增加本任务路由、登录和外壳改动；保持未暂存、未提交。

### 任务 4：实现玩家 DTO、受保护请求和查询状态机

**文件：**

- 新建：`frontend/apps/admin/src/features/players/api/onlinePlayers.ts`
- 新建：`frontend/apps/admin/src/features/players/api/onlinePlayers.test.ts`
- 新建：`frontend/apps/admin/src/features/players/model/useOnlinePlayers.ts`
- 新建：`frontend/apps/admin/src/features/players/model/useOnlinePlayers.test.ts`
- 新建：`frontend/apps/admin/src/features/players/index.ts`

**接口：**

- `PlayerIdentity = { combinedId: string; platform: string }`。
- `OnlinePlayer = { entityId: number; name: string; platformIdentity: PlayerIdentity; crossplatformIdentity: PlayerIdentity | null; ping: number; level: number; health: number }`。
- `OnlinePlayersSnapshot = { capturedAtUtc: string; players: readonly OnlinePlayer[] }`。
- `fetchOnlinePlayers(authorizationHeader, signal?): Promise<OnlinePlayersSnapshot>`。
- `useOnlinePlayers(options?)` 产出 `state`、`snapshot`、`errorCode`、`isRefreshing`、`refresh`、`dispose`。
- `VisibilitySource = { isVisible: () => boolean; subscribe: (listener: () => void) => () => void }`；生产适配 `document.visibilityState` 和 `visibilitychange`，测试使用内存实现。

- [ ] **步骤 1：写玩家 DTO 和认证 Header 失败测试**

  ```ts
  test('parses an empty online snapshot', () => {
    expect(parseOnlinePlayers({ capturedAtUtc: '2026-07-21T00:00:00Z', players: [] }))
      .toEqual({ capturedAtUtc: '2026-07-21T00:00:00Z', players: [] })
  })

  test('accepts nullable crossplatform identity', () => {
    const snapshot = parseOnlinePlayers(validSnapshot({ crossplatformIdentity: null }))
    expect(snapshot.players[0]?.crossplatformIdentity).toBeNull()
  })
  ```

  用 table test 拒绝非 UTC 捕获时间、空名称、缺失平台身份、非整数 entityId/ping/level/health、未知根类型。`fetchOnlinePlayers` 测试断言 URL 为 `/api/v1/players/online`，Header 精确为 `Bearer ...`，URL 与 body 不含 Token。

- [ ] **步骤 2：运行玩家 API 测试并确认 RED，再实现转 GREEN**

  ```powershell
  pnpm test -- src/features/players/api/onlinePlayers.test.ts
  ```

  `parseOnlinePlayers` 使用显式 TypeScript type guard/校验函数，不增加 schema 库。UTC 接受 `Z` 或零 offset，保留后端原始字符串用于显示。冻结或复制数组，防止响应对象后续变更。

- [ ] **步骤 3：写查询状态机失败测试**

  测试通过 `mountComposable` 小包装组件触发生命周期，并注入 `fetchPlayers`、`now`、`documentVisibility`。使用 fake timers 覆盖：

  ```ts
  test('loads immediately and refreshes every ten seconds', async () => {
    const fetchPlayers = vi.fn()
      .mockResolvedValueOnce(snapshotAt('2026-07-21T00:00:00Z'))
      .mockResolvedValueOnce(snapshotAt('2026-07-21T00:00:10Z'))
    const query = mountOnlinePlayers({ fetchPlayers })
    await flushPromises()
    expect(query.state.value).toBe('fresh')

    await vi.advanceTimersByTimeAsync(10_000)
    expect(fetchPlayers).toHaveBeenCalledTimes(2)
  })

  test('keeps the last snapshot stale after refresh failure', async () => {
    const fetchPlayers = vi.fn()
      .mockResolvedValueOnce(snapshotAt('2026-07-21T00:00:00Z'))
      .mockRejectedValueOnce(new HttpError('network'))
    const query = mountOnlinePlayers({ fetchPlayers })
    await flushPromises()
    await query.refresh()
    expect(query.state.value).toBe('stale')
    expect(query.snapshot.value?.capturedAtUtc).toBe('2026-07-21T00:00:00Z')
  })
  ```

  另覆盖无旧值失败为 Offline、403 为 Forbidden、401 调用 `expireSession`、busy 保留旧值、game-not-ready 文案分类、single-flight、页面隐藏暂停、恢复立即刷新、dispose 取消和取消不改状态。

- [ ] **步骤 4：运行查询测试并确认 RED**

  ```powershell
  pnpm test -- src/features/players/model/useOnlinePlayers.test.ts
  ```

  如果先遇到 composable 缺失，只补齐签名正确且抛出 `NotImplemented` 的最小骨架并重跑；正确 RED 必须是查询状态行为断言失败。

- [ ] **步骤 5：实现最小查询状态机**

  默认依赖从 `useAuthStore()` 读取当前 `authorizationHeader`；已过期时 `expireSession()` 并调用注入的 `onSessionExpired`。查询内部保存唯一 `inFlight: Promise<void> | null`，`refresh()` 在进行中返回同一 Promise。只允许当前 request id 提交结果；`dispose()` 递增 id、取消 controller、清理 interval 和 visibility listener。

  状态映射固定为：

  ```ts
  type OnlinePlayersState = 'loading' | 'fresh' | 'stale' | 'offline' | 'forbidden'
  type OnlinePlayersErrorCode = 'game-not-ready' | 'busy' | 'timeout' | 'unavailable' | 'network' | null
  ```

- [ ] **步骤 6：重跑玩家模型测试和静态门禁**

  ```powershell
  pnpm test -- src/features/players
  pnpm lint
  pnpm typecheck
  ```

  预期：玩家 API 和查询状态测试全部通过，无真实计时器或 visibility listener 泄漏。

- [ ] **步骤 7：记录玩家模型检查点**

  ```powershell
  git diff --check -- frontend/apps/admin
  git status --short
  ```

  预期：只增加本任务玩家 API 和模型改动；保持未暂存、未提交。

### 任务 5：实现响应式在线玩家页面和导航

**文件：**

- 新建：`frontend/apps/admin/src/pages/players.vue`
- 新建：`frontend/apps/admin/src/features/players/ui/OnlinePlayersView.vue`
- 新建：`frontend/apps/admin/src/features/players/ui/OnlinePlayersView.test.ts`
- 新建：`frontend/apps/admin/src/features/players/ui/OnlinePlayersToolbar.vue`
- 新建：`frontend/apps/admin/src/features/players/ui/OnlinePlayersTable.vue`
- 新建：`frontend/apps/admin/src/features/players/ui/OnlinePlayersList.vue`
- 新建：`frontend/apps/admin/src/features/players/ui/OnlinePlayersState.vue`
- 修改：`frontend/apps/admin/src/app/AppShell.vue`
- 修改：`frontend/apps/admin/src/assets/css/main.css`

**接口：**

- `players.vue` 只渲染 `OnlinePlayersView` 并声明 `requiresAuth`。
- `OnlinePlayersView` 拥有 `useOnlinePlayers` 生命周期并向子组件传只读 props。
- Toolbar emit `refresh`；Table/List emit `copyIdentity` 或在 Feature 内调用注入 clipboard。
- Table 和 List 接收相同 `readonly OnlinePlayer[]`，不发请求、不保存副本。

- [ ] **步骤 1：写页面状态失败组件测试**

  mock `useOnlinePlayers` 的公开返回值，覆盖：

  ```ts
  test('renders the empty state with capture time', () => {
    const wrapper = mountOnlinePlayersView({
      state: 'fresh',
      snapshot: { capturedAtUtc: '2026-07-21T00:00:00Z', players: [] },
    })
    expect(wrapper.get('[data-testid="players-empty"]').text()).toContain('当前没有在线玩家')
    expect(wrapper.text()).toContain('2026')
  })

  test('keeps player rows visible while stale', () => {
    const wrapper = mountOnlinePlayersView({ state: 'stale', snapshot: onePlayerSnapshot() })
    expect(wrapper.text()).toContain('数据已过期')
    expect(wrapper.text()).toContain('Test Player')
  })
  ```

  另覆盖 Loading skeleton、Offline、Forbidden、game-not-ready、刷新 emit 和 snapshot 中禁止字段不会出现。

- [ ] **步骤 2：运行页面测试并确认 RED**

  ```powershell
  pnpm test -- src/features/players/ui/OnlinePlayersView.test.ts
  ```

  如果先遇到组件缺失，只补齐可挂载的最小组件骨架并重跑；正确 RED 必须是页面状态行为断言失败。

- [ ] **步骤 3：实现页面组合和状态组件**

  使用 `UDashboardPanel`、`UDashboardNavbar`、`UDashboardToolbar`、`UButton`、`UBadge`、`UIcon` 和 `USkeleton`。Navbar refresh 按钮使用 `i-lucide-refresh-cw`，固定 square 尺寸并提供 Tooltip/aria-label。状态必须同时有图标和文本。

  `OnlinePlayersState` 只渲染首次 Loading、Empty、Offline、Forbidden；有 snapshot 的 Stale 由 Toolbar 标记并继续渲染列表。

- [ ] **步骤 4：写玩家字段与复制失败测试**

  对 Table/List 使用同一 fixture，断言名称、entity ID、主平台、可空跨平台身份、等级、生命、延迟都可见；断言没有 IP、位置、封禁、击杀或死亡 label。mock `navigator.clipboard.writeText`，点击主身份复制按钮后只复制对应 `combinedId`。

  ```powershell
  pnpm test -- src/features/players/ui/OnlinePlayersView.test.ts
  ```

  预期：因字段组件尚未实现而失败。

- [ ] **步骤 5：实现桌面 Table 与窄屏 List**

  桌面 `OnlinePlayersTable` 使用 Nuxt UI Table 和类型化列；外层 `hidden md:block`。数值列固定宽度、右对齐并使用 tabular nums。身份值允许换行或安全截断，但复制按钮始终可达。

  窄屏 `OnlinePlayersList` 使用 `md:hidden` 的无卡片分隔列表；每项包含名称/entity ID 首行和两列 label/value 网格，最窄 320px 自动落为一列。不要创建卡片嵌套或水平滚动容器。

- [ ] **步骤 6：增加路由和 App Shell 导航**

  `players.vue` 加入：

  ```vue
  <route lang="json">
  { "meta": { "requiresAuth": true } }
  </route>
  ```

  `AppShell` navigation 增加“玩家”/`i-lucide-users`/`/players`，search group 复用同一项，增加 `g-p` shortcut。不要加入未实现导航。

- [ ] **步骤 7：重跑组件、路由和完整 Admin 门禁**

  ```powershell
  pnpm test
  pnpm lint
  pnpm typecheck
  pnpm build
  ```

  预期：全部成功；route map 包含 `/`、`/login`、`/players`；构建只产生同源静态资源。

- [ ] **步骤 8：记录玩家页面检查点**

  ```powershell
  git diff --check -- frontend/apps/admin
  git status --short
  ```

  预期：只增加本任务页面、组件、导航和样式改动；保持未暂存、未提交。

### 任务 6：完成真实浏览器门禁并同步 Current 文档

**文件：**

- 新建：`frontend/apps/admin/playwright.config.ts`
- 新建：`frontend/apps/admin/tests/e2e/admin-online-players.spec.ts`
- 修改：`frontend/apps/admin/package.json`
- 修改：`frontend/apps/admin/pnpm-lock.yaml`
- 修改：`frontend/apps/admin/README.md`
- 修改：`README.md`
- 修改：`docs/design.md`
- 修改：`docs/architecture.md`
- 修改：`docs/architecture/admin-frontend-target-blueprint.md`
- 修改：`docs/test.md`
- 更新：本计划

**接口：**

- Playwright 默认只运行 Chromium，base URL 由 `SEVENDPANEL_ADMIN_URL` 提供；测试不得把凭据写入配置、trace 名称或命令行。
- 凭据只从 `PANEL_USERNAME`、`PANEL_PASSWORD` 环境变量读取；缺失时真实 E2E 明确跳过并说明前置条件，单元/组件门禁仍必须运行。
- Current 文档只记录实际实现与本轮取得的证据。

- [x] **步骤 1：安装 Playwright 测试依赖并先写浏览器流程测试**

  在 `frontend/apps/admin` 执行：

  ```powershell
  pnpm add -D @playwright/test
  ```

  同时在 `package.json` 增加：

  ```json
  {
    "scripts": {
      "test:e2e": "playwright test"
    }
  }
  ```

  `admin-online-players.spec.ts` 使用环境变量，不输出值：

  ```ts
  test('owner logs in and sees the online player snapshot', async ({ page }) => {
    await page.goto('/players')
    await expect(page).toHaveURL(/\/login\?redirect=/)
    await page.getByLabel('用户名').fill(requiredEnv('PANEL_USERNAME'))
    await page.getByLabel('密码').fill(requiredEnv('PANEL_PASSWORD'))
    await page.getByRole('button', { name: '登录' }).click()
    await expect(page).toHaveURL(/\/players$/)
    await expect(page.getByRole('heading', { name: '在线玩家' })).toBeVisible()
    await expect(page.getByText(/在线人数/)).toBeVisible()
  })
  ```

  同一测试记录所有请求 URL，断言 URL 不含 `access_token`；通过 `page.evaluate` 只返回 Storage/Cookie 是否为空的布尔值，不返回 Token。另加 `/login`、`/players` 深链接刷新和 `390x844` 无水平溢出测试。

- [x] **步骤 2：运行 E2E 并确认前置条件 RED/skip**

  服务器未启动时：

  ```powershell
  pnpm test:e2e
  ```

  预期：若环境变量缺失则报告明确 skip；若提供测试环境但服务未启动则因页面不可达而失败。不能用 mock 页面将该门禁变绿。

- [x] **步骤 3：配置 Playwright 与 README 命令**

  `playwright.config.ts`：

  ```ts
  export default defineConfig({
    testDir: './tests/e2e',
    use: {
      baseURL: process.env.SEVENDPANEL_ADMIN_URL || 'http://127.0.0.1:18080',
      trace: 'retain-on-failure',
      screenshot: 'only-on-failure',
    },
    projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  })
  ```

  README 的 Verification 增加 `pnpm test`；真实 E2E 单独记录环境变量和 `pnpm test:e2e`，不提供默认凭据示例，不把 E2E 混入每次本地快速门禁。

- [x] **步骤 4：运行本地聚合门禁**

  在 `frontend/apps/admin`：

  ```powershell
  pnpm install --frozen-lockfile
  pnpm lint
  pnpm typecheck
  pnpm test
  pnpm build
  ```

  预期：全部成功。扫描 `dist`，确认不包含测试用户名、密码、Token、`VITE_BACKEND_URL` 的值或外部脚本 URL。

- [ ] **步骤 5：发布并执行真实 OWIN 浏览器 smoke**

  2026-07-22 未执行：`SEVENDPANEL_ADMIN_URL`、`PANEL_USERNAME`、`PANEL_PASSWORD` 均未设置。本轮未发布、启动服务器或猜测凭据，Playwright 4 项真实场景全部按前置条件 skip。

  从仓库根目录按现有脚本说明执行：

  ```powershell
  backend\scripts\Publish-Mod.ps1
  backend\scripts\Start-Server.ps1
  backend\scripts\Test-HealthEndpoint.ps1 -TimeoutSeconds 120
  ```

  只在一个受控玩家在线且测试凭据通过环境变量提供后运行：

  ```powershell
  cd frontend/apps/admin
  pnpm test:e2e
  ```

  验证 Token 只在 Authorization Header；URL、Cookie、Storage、控制台和静态构建均无 Token。桌面和 `390x844` 视口必须无关键横向滚动或重叠。

- [ ] **步骤 6：验证关服后的 Stale 和 listener 释放**

  2026-07-22 未执行：步骤 5 的受控真实 OWIN 环境与 Owner 会话不存在。本轮未停止服务器，因此没有关服后 Stale、listener 释放或重启恢复 Fresh 的新证据。

  保持玩家页打开，执行：

  ```powershell
  backend\scripts\Stop-Server.ps1
  backend\scripts\Test-HealthEndpoint.ps1 -ExpectUnavailable -TimeoutSeconds 10
  ```

  浏览器页面必须保留最后玩家快照并在下一轮刷新后标记“数据已过期”；不得显示 Fresh。重启后，浏览器因内存 Token 仍在当前标签页且服务端 Token 持久有效，可通过手动或自动刷新恢复 Fresh；如果页面刷新则按规格重新登录。

- [x] **步骤 7：同步 Current 文档**

  只根据代码、测试输出和真实浏览器证据更新：

  - `docs/design.md`：当前 `/login`、`/players`、导航、桌面/窄屏和状态事实；
  - `docs/architecture.md`：Pinia 内存会话、Router guard、shared API、players Feature、测试依赖与构建产物；
  - Admin Target 蓝图：把已验证的 Pinia、Vitest/VTU 和 Playwright 条目提升为已采用，保留未实现目标；
  - `docs/test.md`：测试数量、命令、浏览器证据与未执行门禁；
  - `README.md` 和 Admin README：只同步聚合入口与所属命令，不复制策略正文；
  - 本计划：勾选实际完成项并记录未执行原因。

- [x] **步骤 8：执行最终文档和工作区验证**

  ```powershell
  git diff --check
  git status --short
  ```

  同时运行 Markdown 诊断和本地链接检查。预期无 ERROR；CRLF 提示不是内容错误。重新运行一次 `pnpm lint`、`pnpm typecheck`、`pnpm test`、`pnpm build`，以最终文件取得新鲜证据。

- [x] **步骤 9：记录最终未提交检查点**

  ```powershell
  git diff --check
  git status --short
  ```

  预期：完整纵向切片保持未暂存、未提交，等待用户单独审阅并明确授权后再决定 Git 操作。

## 完成标准

- 未认证访问 `/` 或 `/players` 会进入独立 `/login`，登录后安全返回原站内目标。
- Pinia 只保存内存 Token 与到期信息；密码、Token 不进入 URL、Cookie、Storage、日志、错误或构建产物。
- `/players` 使用 Bearer Header 消费真实 Owner-only API，并正确验证全部批准字段。
- 首次加载、空列表、Fresh、Stale、Offline、Forbidden、Session expired 和稳定 503 均有行为测试。
- 自动刷新周期为 10 秒；页面隐藏暂停、恢复立即刷新、single-flight 和卸载取消已有确定性测试。
- 桌面表格与窄屏行详情可读取、可复制身份，不显示未批准敏感字段或未实现动作。
- `pnpm lint`、`pnpm typecheck`、`pnpm test`、`pnpm build` 全部通过。
- 真实 OWIN 中完成 Owner 登录、单玩家、深链接、桌面/窄屏、Header-only Token、关服 Stale 与 listener 释放验证。
- Current 文档只记录已由代码、自动化或真实浏览器证据支持的事实；未执行门禁保持明确缺口。