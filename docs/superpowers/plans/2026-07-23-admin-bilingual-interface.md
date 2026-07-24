---
state: Current
document_role: Implementation Plan
primary_spec: ../specs/2026-07-23-admin-bilingual-interface-design.md
last_updated: "2026-07-23"
---

# Admin 中英文界面实施计划

> **面向智能体执行者：** 实施时必须使用 `superpowers:executing-plans`，按任务执行规格符合性与代码质量检查；用户已明确要求不使用子智能体。以下步骤使用复选框跟踪。

**对应规格：** [Admin 中英文界面设计规格](../specs/2026-07-23-admin-bilingual-interface-design.md)

**目标：** 让当前 Admin 登录、应用壳、概览、在线玩家和 API Keys 在 `en` 与 `zh-CN` 下完整可用，首次访问按浏览器语言选择、不支持时回退英文，并持久化用户选择。

**架构：** `app/i18n` 统一拥有语言协商、版本化偏好、Vue I18n Composer、Nuxt UI locale 和 Valibot `lang` 同步；成对 JSON 消息由 Vite 插件构建期预编译。Feature model 只保留稳定反馈 code，组件在渲染边界翻译，语言切换不重建路由、Store 或表单。

**技术栈：** Vue 3.5、TypeScript 6、Vite 8、Vue I18n、`@intlify/unplugin-vue-i18n`、Valibot、`@valibot/i18n`、Nuxt UI 4、Vue Test Utils、Vitest 4、Playwright 1.61、pnpm 11。

## 全局约束

- 内部语言只允许 `en` 与 `zh-CN`，`fallbackLocale` 固定为 `en`；繁体中文标签不得冒充简体中文。
- 偏好键固定为 `7dpanel.locale.v1`，值固定为 `{ version: 1, locale }`；它只使用 `localStorage`，与认证记录分离且登出不清除。
- `vue-i18n`、`valibot`、`@valibot/i18n` 进入 `dependencies`；`@intlify/unplugin-vue-i18n` 进入 `devDependencies`，且只预编译 `src/app/i18n/locales/**`。
- 不从网络加载语言资源，不使用 HTML 消息、`v-html`、locale 路由前缀、Feature 懒加载语言包或 Pinia locale Store。
- `en.json` 与 `zh-CN.json` 必须具有相同叶子键和插值参数；缺失键是自动化失败，生产只回退英文。
- 玩家名、服务器名、Steam ID、EOS ID、API Key 名称/标识、角色代码、版本、路径、日志、协议和审计标识保持原值。
- 当前登录、踢人和 API Key 创建表单接入 Valibot Standard Schema；客户端校验不替代 API 防御性校验或服务端授权。
- Feature model 不保存已翻译消息；稳定 code 在 UI 渲染边界映射，任意服务端异常文本不得直接显示。
- 每项生产行为先写失败测试并确认 RED，再做最小实现并复跑同一检查；稳定后只运行一次聚合门禁。
- 本计划不授权 `git commit`、`git push`、`git reset`、`git revert`、远程发布或真实服务器启停；不得修改 `7dtd-reference/`。

---

### 任务 1：建立语言核心、资源和依赖边界

**文件：**

- 修改：`frontend/apps/admin/package.json`
- 修改：`frontend/apps/admin/pnpm-lock.yaml`
- 修改：`frontend/apps/admin/vite.config.ts`
- 新建：`frontend/apps/admin/src/app/i18n/locale.ts`
- 新建：`frontend/apps/admin/src/app/i18n/locale.test.ts`
- 新建：`frontend/apps/admin/src/app/i18n/localePreference.ts`
- 新建：`frontend/apps/admin/src/app/i18n/localePreference.test.ts`
- 新建：`frontend/apps/admin/src/app/i18n/locales/en.json`
- 新建：`frontend/apps/admin/src/app/i18n/locales/zh-CN.json`
- 新建：`frontend/apps/admin/src/app/i18n/messages.test.ts`
- 新建：`frontend/apps/admin/src/app/i18n/index.ts`

**接口：**

```ts
export type SupportedLocale = 'en' | 'zh-CN'

export const DEFAULT_LOCALE: SupportedLocale = 'en'
export const LOCALE_PREFERENCE_STORAGE_KEY = '7dpanel.locale.v1'

export function matchSupportedLocale(tag: string): SupportedLocale | null
export function negotiateLocale(tags: readonly string[]): SupportedLocale
export function parseLocalePreference(value: string | null): SupportedLocale | null
export function serializeLocalePreference(locale: SupportedLocale): string

export interface LocalePreferenceRepository {
  restore(browserLanguages: readonly string[]): SupportedLocale
  save(locale: SupportedLocale): boolean
  subscribe(listener: (locale: SupportedLocale) => void): () => void
}

export function createBrowserLocalePreferenceRepository(options: {
  getStorage: () => Storage
  eventTarget: Pick<Window, 'addEventListener' | 'removeEventListener'>
  browserLanguages: () => readonly string[]
}): LocalePreferenceRepository
```

- [x] **步骤 1：写 locale 协商 RED 测试**

  在 `locale.test.ts` 使用表驱动测试锁定：`en`/`en-US -> en`，`zh`/`zh-CN`/`zh-SG`/`zh-Hans-CN -> zh-CN`，`zh-TW`/`zh-HK`/`zh-MO`/`zh-Hant -> null`，无效标签不抛错；断言 `negotiateLocale(['zh-TW', 'en-US']) === 'en'`、`negotiateLocale(['fr', 'zh-Hans']) === 'zh-CN'`、无匹配返回 `en`。

- [x] **步骤 2：运行 locale RED**

  ```powershell
  Set-Location frontend/apps/admin
  pnpm exec vitest run src/app/i18n/locale.test.ts
  ```

  预期：模块不存在，测试失败。

- [x] **步骤 3：实现 locale 协商并验证 GREEN**

  `matchSupportedLocale` 先使用 `Intl.Locale`，按 language、script、region 判定；`Hant` 或 `TW/HK/MO` 优先排除，`Hans` 或 `CN/SG` 接受，无 script/region 的纯 `zh` 接受。任何构造异常返回 `null`。复跑步骤 2，预期通过。

- [x] **步骤 4：写偏好 Repository RED 测试**

  覆盖严格 codec、有效偏好优先于浏览器语言、损坏/未知版本/额外字段清理后协商、读写删除抛错降级、保存成功或失败、只响应指定键、有效 Storage 事件同步、删除/损坏事件重新协商和 unsubscribe。

- [x] **步骤 5：实现偏好 Repository 并验证 GREEN**

  ```powershell
  pnpm exec vitest run src/app/i18n/localePreference.test.ts
  ```

  每次 Storage 操作单独 `try/catch`；事件 listener 不写回，`restore()` 返回有效偏好，否则尽力删除并调用 `negotiateLocale(browserLanguages())`。预期全部通过。

- [x] **步骤 6：写消息目录 RED 测试**

  递归展开两个 JSON 的叶子键，断言集合相等、叶子均为非空字符串、消息不含 HTML 标签；提取 `{name}` 形式插值参数并断言同键两种语言参数集合相等。先只创建含一个占位键且键集合不一致的资源，确认测试因键缺失失败。

- [x] **步骤 7：安装依赖并配置构建期预编译**

  ```powershell
  pnpm add vue-i18n valibot @valibot/i18n
  pnpm add -D @intlify/unplugin-vue-i18n
  ```

  在 `vite.config.ts` 导入 `node:path`、`node:url` 和 `@intlify/unplugin-vue-i18n/vite`，在 `vue()` 前注册插件，`include` 精确指向 `src/app/i18n/locales/**`。不得设置 `runtimeOnly: false`。

- [x] **步骤 8：建立完整成对消息骨架并验证任务 1**

  两份 JSON 先包含当前页面所需的 `common`、`locale`、`appearance`、`auth`、`overview`、`players`、`apiKeys` 命名空间；后续任务只填充或使用已成对增加的键。运行：

  ```powershell
  pnpm exec vitest run src/app/i18n/locale.test.ts src/app/i18n/localePreference.test.ts src/app/i18n/messages.test.ts
  pnpm typecheck
  pnpm build
  ```

  预期全部 exit 0，生产构建不报告 runtime message compiler 或 locale resource 错误。

### 任务 2：接线 Vue I18n、Nuxt UI、Valibot 与语言入口

**文件：**

- 修改：`frontend/apps/admin/src/app/i18n/index.ts`
- 新建：`frontend/apps/admin/src/app/i18n/i18n.test.ts`
- 新建：`frontend/apps/admin/src/components/LocaleMenu.vue`
- 新建：`frontend/apps/admin/src/components/LocaleMenu.test.ts`
- 修改：`frontend/apps/admin/src/main.ts`
- 修改：`frontend/apps/admin/src/App.vue`
- 修改：`frontend/apps/admin/src/app/AppShell.vue`
- 修改：`frontend/apps/admin/src/app/AppShell.test.ts`
- 修改：`frontend/apps/admin/src/pages/login.vue`

**接口：**

```ts
export const i18n: I18n
export function createAdminI18n(options?: {
  repository?: LocalePreferenceRepository
  documentElement?: Pick<HTMLElement, 'lang'>
}): {
  i18n: I18n
  locale: Readonly<Ref<SupportedLocale>>
  nuxtLocale: ComputedRef<Locale>
  setLocale(locale: SupportedLocale): void
  dispose(): void
}

export function useAdminLocale(): {
  locale: Readonly<Ref<SupportedLocale>>
  nuxtLocale: ComputedRef<Locale>
  setLocale(locale: SupportedLocale): void
}
```

- [x] **步骤 1：写运行时同步 RED 测试**

  使用内存 repository 创建实例，断言恢复 locale 写入 Composer、根 `lang`、Nuxt UI locale code 和 Valibot global config；调用 `setLocale('zh-CN')` 后四者同步且 repository 保存一次；Storage listener 模拟外部切换时不重复保存；`dispose()` 取消订阅。

- [x] **步骤 2：实现运行时同步并验证 GREEN**

  Vue I18n 使用 Composition API、JSON messages、`fallbackLocale: 'en'` 和明确的 `datetimeFormats`/`numberFormats`。按锁定包实际导出只导入简体中文模块，英文使用 Valibot 内置默认消息，并将内部 locale 映射到 Valibot 支持的 lang；Nuxt UI 从 `@nuxt/ui/locale` 导入 `en` 和 `zh_cn`。运行：

  ```powershell
  pnpm exec vitest run src/app/i18n/i18n.test.ts
  ```

- [x] **步骤 3：写 LocaleMenu RED 组件测试**

  挂载真实 i18n plugin，断言入口显示当前语言自称、菜单包含 `English` 与 `简体中文`、当前项已选、选择另一项调用统一 `setLocale`，折叠态有当前语言可访问名称。

- [x] **步骤 4：实现 LocaleMenu 并验证 GREEN**

  使用 Lucide `languages` 图标和 Nuxt UI dropdown radio/checkbox item；展开态显示图标加当前语言，折叠态只显示图标并提供 tooltip/aria-label。复跑步骤 3。

- [x] **步骤 5：接线 bootstrap 与两个入口**

  `main.ts` 在 mount 前安装 i18n；`App.vue` 传递 `<UApp :locale="nuxtLocale">`；登录页在表单外提供 `LocaleMenu`，`AppShell` 在外观菜单和账户菜单之间提供同一组件。组件不读取 Storage。

- [x] **步骤 6：验证应用接线**

  更新测试挂载器安装 i18n，并运行：

  ```powershell
  pnpm exec vitest run src/app/i18n/i18n.test.ts src/components/LocaleMenu.test.ts src/app/AppShell.test.ts
  pnpm typecheck
  ```

  预期两种入口可达、切换不改变当前 route/auth identity，全部 exit 0。

### 任务 3：迁移登录、外观和概览

**文件：**

- 修改：`frontend/apps/admin/src/components/AppBrand.vue`
- 修改：`frontend/apps/admin/src/components/AppearanceMenu.vue`
- 新建：`frontend/apps/admin/src/components/AppearanceMenu.test.ts`
- 修改：`frontend/apps/admin/src/features/auth/ui/LoginForm.vue`
- 修改：`frontend/apps/admin/src/features/auth/ui/LoginForm.test.ts`
- 修改：`frontend/apps/admin/src/pages/login.vue`
- 修改：`frontend/apps/admin/src/pages/index.vue`
- 新建：`frontend/apps/admin/src/pages/index.test.ts`
- 修改：`frontend/apps/admin/src/app/AppShell.vue`
- 修改：`frontend/apps/admin/src/app/AppShell.test.ts`
- 修改：`frontend/apps/admin/src/app/i18n/locales/en.json`
- 修改：`frontend/apps/admin/src/app/i18n/locales/zh-CN.json`

**接口：**

```ts
const LoginSchema = v.object({
  username: v.pipe(v.string(), v.trim(), v.nonEmpty()),
  password: v.pipe(v.string(), v.nonEmpty()),
})
```

- [x] **步骤 1：先把现有组件测试改为双语 RED**

  LoginForm 在 `en` 下断言 `Username`、`Password`、`Keep me signed in`、`Sign in` 和英文认证错误，在 `zh-CN` 下保留既有语义；切换语言后断言 username、password 和 remember 值不变。AppearanceMenu 与 AppShell 分别断言两种语言的外观、导航、搜索、账号和退出文案，角色 `Owner` 不变。Overview 测试用固定时间断言 loading/fresh/stale/offline 与当前 locale 日期格式。

- [x] **步骤 2：运行组件 RED**

  ```powershell
  pnpm exec vitest run src/features/auth/ui/LoginForm.test.ts src/components/AppearanceMenu.test.ts src/app/AppShell.test.ts src/pages/index.test.ts
  ```

  预期：硬编码中文和缺少 schema 使英文断言失败。

- [x] **步骤 3：迁移静态与 computed 文案**

  所有 props、template 文本、动态 aria-label、Toast 和 computed 状态改用 `useI18n().t`；`navigation`、`searchGroups`、`accountItems`、Appearance items 和健康状态使用 computed，避免切换后保留旧字符串。日期使用 Vue I18n `d()` 或显式当前 locale 的 `Intl.DateTimeFormat`，不再传 `undefined`。

- [x] **步骤 4：接入登录 Valibot schema**

  `UForm` 传 `:schema="LoginSchema"`；空用户名和密码由 Valibot issue 呈现，认证失败仍按 `auth.error` 稳定 code 翻译。提交继续清空密码且保留用户名/remember；切换语言不替换 `credentials` 对象。

- [x] **步骤 5：验证任务 3**

  复跑步骤 2，再运行消息目录测试和 typecheck。预期两种语言均通过、没有裸中文产品字符串进入上述生产组件、消息目录键集合一致。

### 任务 4：迁移在线玩家与踢出流程

**文件：**

- 修改：`frontend/apps/admin/src/features/players/model/useOnlinePlayers.ts`
- 修改：`frontend/apps/admin/src/features/players/model/useOnlinePlayers.test.ts`
- 修改：`frontend/apps/admin/src/features/players/model/useKickPlayer.ts`
- 修改：`frontend/apps/admin/src/features/players/model/useKickPlayer.test.ts`
- 修改：`frontend/apps/admin/src/features/players/ui/OnlinePlayersToolbar.vue`
- 修改：`frontend/apps/admin/src/features/players/ui/OnlinePlayersState.vue`
- 修改：`frontend/apps/admin/src/features/players/ui/OnlinePlayersTable.vue`
- 修改：`frontend/apps/admin/src/features/players/ui/OnlinePlayersList.vue`
- 修改：`frontend/apps/admin/src/features/players/ui/OnlinePlayersView.vue`
- 修改：`frontend/apps/admin/src/features/players/ui/OnlinePlayersView.test.ts`
- 修改：`frontend/apps/admin/src/features/players/ui/KickPlayerDialog.vue`
- 修改：`frontend/apps/admin/src/features/players/ui/KickPlayerDialog.test.ts`
- 修改：`frontend/apps/admin/src/app/i18n/locales/en.json`
- 修改：`frontend/apps/admin/src/app/i18n/locales/zh-CN.json`

**接口：**

```ts
export interface PlayerFeedback {
  code: PlayerFeedbackCode
}

const KickPlayerSchema = v.object({
  reason: v.pipe(v.string(), v.trim(), v.nonEmpty(), v.maxLength(200)),
})
```

- [x] **步骤 1：把 model 测试改为 code-only RED**

  所有 `{ code, message }` 期望改为 `{ code }`，确认 controller 不再拥有中文文案；运行两个 model test，预期当前多余 `message` 导致失败。

- [x] **步骤 2：最小化 model 反馈并验证 GREEN**

  删除 message 常量和通用中文映射，只保留既有稳定 code；不得改变 retry、401/403、stale、unknown 或身份匹配状态机。复跑 model test。

- [x] **步骤 3：写玩家双语 UI 与 Valibot RED 测试**

  两种语言覆盖 navbar、刷新、在线数量、loading/empty/stale/offline/forbidden/error、表头/移动标签、复制与玩家操作 aria-label、踢出标题/说明/原因/取消/提交、所有反馈 code 和成功 Toast。切换语言后目标玩家、reason 输入和对话框打开状态保持；玩家名与平台 identity 原值不变。空原因及超过 200 字符显示相应 Valibot locale issue。

- [x] **步骤 4：迁移玩家 UI 并接入 schema**

  表头、菜单项和状态派生数据必须 computed；持续反馈用 `t(codeToKey[feedback.code])`，成功 Toast 在触发时使用当前 `t`。`KickPlayerDialog` 的 `UForm` 使用 schema，trim 后值提交但组件不因 locale 切换重建 state。

- [x] **步骤 5：验证任务 4**

  ```powershell
  pnpm exec vitest run src/features/players
  pnpm exec vitest run src/app/i18n/messages.test.ts
  pnpm typecheck
  ```

  预期所有玩家 API/model/UI 测试通过，技术值切换前后相同。

### 任务 5：迁移 API Keys 页面与表单

**文件：**

- 修改：`frontend/apps/admin/src/features/api-keys/model/useApiKeys.ts`
- 修改：`frontend/apps/admin/src/features/api-keys/model/useApiKeys.test.ts`
- 修改：`frontend/apps/admin/src/features/api-keys/ui/ApiKeysView.vue`
- 修改：`frontend/apps/admin/src/features/api-keys/ui/ApiKeysView.test.ts`
- 修改：`frontend/apps/admin/src/features/api-keys/ui/CreateApiKeyDialog.vue`
- 修改：`frontend/apps/admin/src/features/api-keys/ui/CreateApiKeyDialog.test.ts`
- 修改：`frontend/apps/admin/src/features/api-keys/ui/ApiKeyCreatedDialog.vue`
- 修改：`frontend/apps/admin/src/features/api-keys/ui/ApiKeyCreatedDialog.test.ts`
- 修改：`frontend/apps/admin/src/features/api-keys/ui/RevokeApiKeyDialog.vue`
- 修改：`frontend/apps/admin/src/features/api-keys/ui/RevokeApiKeyDialog.test.ts`
- 修改：`frontend/apps/admin/src/pages/api-keys.test.ts`
- 修改：`frontend/apps/admin/src/app/i18n/locales/en.json`
- 修改：`frontend/apps/admin/src/app/i18n/locales/zh-CN.json`

**接口：**

```ts
export interface ApiKeyFeedback {
  code: ApiKeyFeedbackCode
}

const CreateApiKeySchema = v.object({
  name: v.pipe(v.string(), v.trim(), v.nonEmpty(), v.maxLength(80)),
  expiresAtUtc: v.union([v.literal(''), v.pipe(v.string(), v.isoTimestamp())]),
})
```

- [x] **步骤 1：把 controller 反馈改为 code-only RED/GREEN**

  测试先期望 `{ code }` 并确认 RED，再删除 controller 的中文 message。保持创建、撤销、401/403、刷新和一次性 secret 清理状态机不变，复跑 `useApiKeys.test.ts`。

- [x] **步骤 2：写 API Keys 双语 RED 测试**

  两种语言覆盖 navbar、创建、刷新、loading/empty/failed/forbidden、表头/移动内容、状态、日期、撤销、创建说明、一次性结果、复制和关闭确认；API Key 名称、prefix、完整一次性值和角色相关标识保持原值。切换语言时已输入 name/expiry 和打开的非危险创建对话框保持。

- [x] **步骤 3：写创建 schema RED 测试并实现**

  空名称、超过 80 字符和无效 UTC timestamp 分别显示当前 Valibot locale issue；合法输入继续由现有 API normalize/parse 防御。对话框 `UForm` 接入 schema，不把完整 API Key 写入语言偏好、Toast 或持久 state。

- [x] **步骤 4：迁移所有 API Key UI 文案和日期**

  持续反馈按 code 渲染；按钮、对话框、说明、aria-label、复制成功 Toast、状态和日期格式使用当前 Composer。关闭一次性结果后 secret 清理合同不变，语言切换不能把 secret 复制到新状态。

- [x] **步骤 5：验证任务 5**

  ```powershell
  pnpm exec vitest run src/features/api-keys src/pages/api-keys.test.ts
  pnpm exec vitest run src/app/i18n/messages.test.ts
  pnpm typecheck
  ```

  预期 API/model/UI 测试全部通过，双语切换不改变 Key 原始值和安全生命周期。

### 任务 6：扩展 E2E、执行聚合门禁并提升文档事实

**文件：**

- 修改：`frontend/apps/admin/tests/e2e/admin-online-players.spec.ts`
- 修改：`frontend/apps/admin/tests/e2e/admin-api-keys.spec.ts`
- 修改：`docs/architecture.md`
- 修改：`docs/test.md`
- 修改：`docs/architecture/admin-frontend-target-blueprint.md`
- 修改：`docs/superpowers/plans/2026-07-23-admin-bilingual-interface.md`

**接口：**

- 消费：任务 1–5 的统一 locale API、偏好键和双语 UI。
- 产出：可发现的真实 OWIN Edge 双语合同，以及当前架构/测试事实与目标蓝图的准确状态。

- [x] **步骤 1：扩展浏览器测试合同**

  增加 `localePreferenceStorageKey = '7dpanel.locale.v1'`，使用 Playwright context locale 覆盖 `zh-CN`、`en-US`、`fr-FR` 和 `zh-TW` 加英文次选；断言登录前后切换、刷新持久化、登出不清除偏好、当前路由和安全输入保持、技术值不变。现有中文 locator 改为由测试 locale 明确选择的双语 locator，不能用过宽正则掩盖缺失翻译。

- [x] **步骤 2：运行 E2E discovery**

  ```powershell
  pnpm exec playwright test --list
  ```

  预期所有既有认证/API Key/玩家与新增 locale 场景被发现；没有真实环境变量时只允许测试按既有理由 skip，不宣称通过。

- [x] **步骤 3：运行前端聚合门禁**

  ```powershell
  pnpm lint
  pnpm typecheck
  pnpm test:unit
  pnpm build
  ```

  预期全部 exit 0。记录实际文件数、测试数与既有 happy-dom teardown 噪声；不得把 exit 0 的基础设施噪声描述成新增产品失败，也不得隐藏真正失败。

- [x] **步骤 4：检查生产产物与裸文案**

  使用 workspace 搜索确认当前生产 `.vue`/`.ts` 中没有遗留的中英文产品句子，允许技术标识、测试 fixture 和 locale JSON。检查 `dist` 不引用 CDN、远程 locale、`unsafe-eval` 或全部 Valibot 语言模块，并运行 `git diff --check`。

- [x] **步骤 5：按证据更新权威文档**

  `docs/architecture.md` 只记录实际落地的 bootstrap、消息目录、偏好 repository、Nuxt UI/Valibot 同步和 code-only 反馈边界；`docs/test.md` 记录实际自动化命令、断言与未运行真实环境原因；目标蓝图只把已经采用的候选状态和证据缺口同步为准确状态，不复制实现细节。

- [x] **步骤 6：最终复核**

  复跑受文档更新影响的链接/Markdown 检查、`git diff --check`、`git status --short`，核对只修改本计划范围文件且没有生成凭据、构建产物、非目标锁文件或 `7dtd-reference/` 改动。把本计划已完成步骤逐项勾选；不执行提交或 push。