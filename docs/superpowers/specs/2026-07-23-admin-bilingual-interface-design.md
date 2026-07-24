---
state: Current
document_role: Design Spec
last_updated: "2026-07-23"
---

# Admin 中英文界面设计规格

> 本文描述已经批准、尚待实现的 Admin 双语界面切片。产品行为由[产品需求](../../PRD.md)中的 `NFR-03` 定义，交互与内容边界由[产品设计](../../design.md)定义，当前实现事实与验证证据仍分别以[系统架构](../../architecture.md)和[测试策略](../../test.md)为准。

## 上游与范围

当前 Admin 的登录、应用壳、概览、在线玩家和 API Keys 页面直接包含简体中文产品文案，`UApp` 尚未接收 locale，表单没有统一的 Valibot schema，反馈 model 也保存已经格式化的中文消息。浏览器语言、用户语言偏好、Nuxt UI 内置文案、表单校验和业务错误因此还没有共同的当前语言来源。

本变更完成当前已经实现的 Admin 用户界面，而不是只安装依赖或只翻译登录页。交付范围包括：

- 登录页、应用壳、概览、在线玩家和 API Keys 的全部产品文案与可访问名称；
- 登录前和登录后的语言入口；
- 浏览器语言协商、英文回退、非敏感偏好持久化和同源标签页同步；
- Nuxt UI 内置文案、Valibot 内置校验消息、日期数字格式和稳定业务错误码的同语言呈现；
- 当前页面、筛选和可安全保留表单输入在语言切换时不丢失；
- 与上述行为对应的单元、组件、构建和浏览器验证。

## 目标

- 首版只支持 `en` 和 `zh-CN`，没有有效偏好时按浏览器语言首选，无法匹配时回退 `en`。
- 用户可以在未登录页和认证后的全局框架切换语言，结果立即生效并保存在当前浏览器。
- 产品文案、Nuxt UI、Valibot、日期数字格式和业务错误在任一时刻使用同一当前语言。
- 语言切换不重建 Router、Pinia 或当前页面，不清除可安全保留的表单输入、筛选和路由状态。
- 英文资源和简体中文资源拥有相同键集合；缺失键在自动化中失败，不向用户显示翻译键或空白内容。
- 技术标识、用户数据和日志原文保持原值，不因界面语言改变。

## 非目标

- 不增加繁体中文、自动翻译、服务端翻译、账号级语言同步或跨浏览器同步。
- 不给路由增加 locale 前缀，不因切换语言导航到另一 URL，也不为语言资源增加网络请求。
- 不引入按 Feature 或按路由懒加载语言包；当前界面规模不支持这项异步复杂度。
- 不翻译玩家名、服务器名、Steam ID、EOS ID、IP、坐标、API Key 名称、安全标识、角色代码、路径、日志原文、协议名称、Correlation ID 或 Audit ID。
- 不根据任意服务端异常文本或英文句子进行客户端翻译；只有稳定错误码可以选择产品文案。
- 不在本切片实现尚未存在的 P0 页面，也不把目标蓝图中的未来页面当作当前完成范围。

## 方案与依赖职责

采用集中式 JSON 消息目录和构建期预编译：

| 依赖或边界 | 归类 | 职责 |
|---|---|---|
| `vue-i18n` | `dependency` | 产品消息、插值、日期数字格式和响应式当前语言 |
| `valibot` | `dependency` | 当前表单的 Standard Schema 与结构化浏览器校验 |
| `@valibot/i18n` | `dependency` | Valibot 通用 schema/action issue 的 `en` 与简体中文官方消息 |
| `@intlify/unplugin-vue-i18n` | `devDependency` | 在 Vite 构建期预编译纳入边界的 JSON 消息资源，避免浏览器运行时编译产品消息 |
| `UApp :locale` | 既有 Nuxt UI 集成 | Nuxt UI 内置文案、组件区域格式和方向配置 |

不采用 TypeScript 内联消息，因为这会让 `@intlify/unplugin-vue-i18n` 没有真实运行职责，并降低翻译资源的独立审阅能力。不采用 Feature 级懒加载，因为当前约 30 个组件的静态双语资源不足以证明异步 chunk、切换竞态和资源失败状态的必要性。

Vite 插件只包含 `src/app/i18n/locales/**`，不能扫描整个仓库或外部输入。产品消息不使用 HTML，不通过 `v-html` 渲染，也不放入来自服务端或用户输入的可执行内容。

## 语言标识与浏览器协商

应用内部只允许以下稳定标识：

```ts
type SupportedLocale = 'en' | 'zh-CN'
```

没有有效持久偏好时，按 `navigator.languages` 的原始顺序检查候选；`navigator.languages` 不可用时再检查 `navigator.language`：

- `en` 及 `en-*` 映射到 `en`；
- `zh-CN`、`zh-SG`、`zh-Hans`、带 `Hans` script 的中文标签和无 region/script 的 `zh` 映射到 `zh-CN`；
- `zh-TW`、`zh-HK`、`zh-MO`、`zh-Hant` 和带 `Hant` script 的中文标签不映射到简体中文，继续检查下一候选；
- 其他语言继续检查下一候选；没有候选匹配时返回 `en`。

匹配过程忽略大小写，但输出必须规范化为内部稳定标识。实现优先使用平台 `Intl.Locale` 解析可解析标签；无效标签必须安全忽略，不能阻断应用启动。自动化需要覆盖候选顺序，例如 `['zh-TW', 'en-US']` 选择 `en`，而 `['fr-FR', 'zh-Hans']` 选择 `zh-CN`。

`fallbackLocale` 固定为 `en`。英文资源是缺失当前语言消息时的唯一运行时回退，不根据浏览器候选建立多级回退链。

## 偏好记录与生命周期

语言偏好使用独立版本化键 `7dpanel.locale.v1`，不得复用或写入 `7dpanel.auth.session.v1`：

```ts
interface PersistedLocalePreferenceV1 {
  version: 1
  locale: 'en' | 'zh-CN'
}
```

- 应用启动先严格解析 `localStorage` 记录；版本、字段集合、类型或 locale 无效时删除该记录并重新执行浏览器协商。
- 用户显式切换语言后，先更新当前内存语言，再尝试写入 `localStorage`；Storage 不可用时当前页面仍完成切换，不显示阻断错误。
- 语言偏好是非敏感浏览器设置，不随登录、登出、Access Token 到期、401、认证 Storage 清理或 API Key 生命周期删除。
- 同源其他标签页对该键的有效 `storage` 事件立即采用新语言；删除或损坏事件重新执行本标签页浏览器协商，不循环写回。
- 浏览器后续修改系统语言不覆盖已经保存的用户选择。没有保存选择时也不要求实时监听系统语言变化，下一次应用启动重新协商即可。

## 运行时所有权与数据流

```text
application bootstrap
  -> read locale preference
  -> negotiate browser locale when needed
  -> create Vue I18n Composer with fallbackLocale=en
  -> set Valibot global lang
  -> install i18n before mount

locale switch
  -> validate SupportedLocale
  -> update Composer locale
  -> update Valibot global lang
  -> persist preference when available
  -> UApp locale and document lang reactively update
  -> mounted route, filters and component-local safe input remain intact
```

`src/app/i18n` 是当前语言的唯一所有者，负责：

- `SupportedLocale` 与外部 locale 标识映射；
- 浏览器语言协商和偏好 codec/repository；
- Vue I18n 实例、消息资源、格式和切换 API；
- Nuxt UI locale 对象映射；
- Valibot `lang` 同步；
- 根文档 `lang` 同步和 Storage 事件订阅清理。

Pinia 不保存语言，因为语言是应用级非业务配置，Vue I18n Composer 已提供响应式唯一来源。Feature 组件不得直接读写语言 Storage，也不得各自维护 locale ref。

`main.ts` 在 Router 和组件挂载前创建并安装 i18n。`App.vue` 从 i18n 边界获取当前 Nuxt UI locale，并通过 `UApp :locale` 提供给全部组件。根文档的 `document.documentElement.lang` 与内部 locale 保持一致；当前两种语言方向均为 `ltr`。

## 消息目录与键所有权

产品消息采用两个成对 JSON 资源：

```text
src/app/i18n/
  index.ts
  locale.ts
  localePreference.ts
  locales/
    en.json
    zh-CN.json
```

消息键按产品语义组织，而不是按中文原句或 HTML 结构命名，例如 `auth.login.fields.username`、`players.kick.feedback.notOnline` 和 `apiKeys.create.actions.submit`。共享命令只在语义完全一致时进入 `common`；不能为了减少几个重复字符串让不同业务动作共享模糊键。

- 英文与简体中文必须拥有相同叶子键和兼容插值参数。
- 插值值默认按文本处理；不允许产品消息包含 HTML。
- 动态可访问名称、Toast、对话框标题、表格标题、空态、loading、按钮、菜单和 placeholder 都属于产品消息。
- `API Keys`、`Steam ID`、`Owner` 等稳定产品或协议标识可以在两种资源中保持相同文本，但仍由周围句子提供当前语言语义。
- 测试和开发环境的 missing handler 必须让缺失键可见并失败；生产运行时使用英文回退，不能把键名当成正常 UI。

## 语言入口与页面行为

登录页和认证后的应用壳复用同一个语言菜单组件：

- 入口使用语言图标和当前语言的自称名称：`English` 或 `简体中文`；折叠状态使用图标按钮并提供当前语言的可访问名称和 tooltip。
- 菜单只包含两种受支持语言，当前项使用单选或勾选状态表达，不使用颜色作为唯一状态。
- 登录页入口在提交凭据前可达；应用壳入口与外观和账号入口处于同一全局工具区域。
- 切换不调用 Router、不刷新页面、不重建表单、不关闭非危险的当前输入，也不更改认证状态。
- 密码和危险确认状态继续服从各 Feature 的既有安全生命周期；语言持久化不能被用来保存这些状态。
- 英文文本在桌面、`390x844` 和 320 CSS 像素宽度下不能遮挡、截断关键命令或造成水平页面滚动。允许标签按既有布局换行，图标按钮保持稳定尺寸。

## 表单与 Valibot

当前存在用户输入的登录、踢出玩家和创建 API Key 表单迁移到 Valibot Standard Schema，并由 Nuxt UI `UForm` 使用：

- schema 保持纯结构和通用约束，不读取组件实例或 Router；
- 必填、字符串类型、长度和时间格式等通用 issue 使用 `@valibot/i18n` 官方翻译；
- 用户输入在语言切换时保持原值，已显示的校验结果应在重新校验后使用当前语言；
- 目标身份变化、玩家已离线、权限不足、会话到期、API 失败等业务结果不塞入 Valibot schema，继续由稳定业务错误码处理；
- 服务端仍是安全和业务约束的权威，客户端校验不能替代 API 验证或授权。

只导入简体中文所需的官方 locale 模块，英文使用 Valibot 内置默认消息，不能导入全部 `@valibot/i18n` 翻译。内部 locale 到 Valibot `lang` 的精确映射由实现基于锁定包的实际导出验证，不能假设两个库使用相同标识。

API 边界现有输入规范化和服务端防御性校验继续保留；表单 schema 负责用户反馈，API 函数不能因为调用者当前使用 schema 就信任任意运行时输入。

## 业务反馈与错误边界

Feature model 和 API 层不得保存已经翻译的 `message` 作为业务状态。反馈状态保留稳定 code 以及需要插值的非敏感结构化数据，UI 在渲染时根据当前 locale 选择消息：

```ts
interface FeatureFeedback {
  code: StableFeedbackCode
  values?: Record<string, string | number>
}
```

- 切换语言后，当前可见错误、状态和 Toast 之后的新反馈立即使用新语言；页面内持续反馈不需要重新请求服务端。
- Toast 在创建时生成文本，已经弹出的短暂 Toast 不要求原地变更语言；语言切换后新 Toast 必须使用新语言。
- 原始服务端异常文本、Problem Details `detail` 或 HTTP status text 不直接显示或作为翻译键。
- 未知服务端错误使用当前 Feature 的通用失败键，并保留已有 Correlation ID/Audit ID 展示边界；不伪造具体失败原因。
- 401 继续触发认证失效，403 保留会话并显示当前语言的权限反馈，网络和 5xx 不误清会话。

## 日期、数字与原值边界

日期和数字格式由 Vue I18n 的格式配置或基于当前 locale 的 `Intl` 提供：

- `en` 使用明确的英文区域格式，`zh-CN` 使用简体中文区域格式；
- UTC 时刻的解析、排序和状态判断继续使用原始时间值，格式化只发生在 UI 边界；
- 未知或无值状态使用消息目录中的当前语言标签，不把中文占位值写入 model；
- Steam ID、EOS ID、坐标、端口、版本号、API Key 安全标识和其他协议值不做本地化数字分组；
- 日期切换语言后从原始值重新格式化，不能在 Store 或 controller 中缓存中文展示字符串。

## 失败与安全边界

- 语言偏好损坏、Storage 抛错、单个浏览器语言标签无效或缺少非英文消息都不能阻断登录和受保护管理页面。
- 英文资源缺失键属于构建或测试失败；生产出现该状态时使用受控通用英文消息，不能暴露异常对象或空白关键操作。
- 消息资源是受版本控制的静态构建输入，不从 CDN、服务端配置、用户输入或运行时远程地址加载，保持 `NFR-01` 离线运行和既有 CSP 边界。
- 语言记录不包含用户名、Token、密码、API Key、筛选、表单输入或其他业务数据。
- 翻译文本不决定权限、状态机、路由身份或错误分类；代码只使用稳定 code 和类型做控制流。
- 依赖安装后复核生产构建不引入远程脚本、`unsafe-eval`、动态消息编译需求或全部 Valibot 语言资源。

## 自动化与验证

### 单元测试

- locale 协商覆盖大小写、无效标签、候选顺序、`en-*`、简体标签、繁体标签和最终英文回退。
- 偏好 codec/repository 覆盖合法记录、未知版本、额外或缺失字段、不支持 locale、损坏 JSON、Storage 异常及有效/删除/损坏 `storage` 事件。
- 消息目录测试递归比较 `en` 和 `zh-CN` 键集合及插值参数，并验证关键路径不会返回键名、空字符串或英文之外的隐式回退。
- Valibot 同步测试验证两种应用 locale 映射到实际包支持的 `lang`，同一无效输入分别生成英文和简体中文官方 issue。
- 业务反馈映射测试验证稳定 code 在两种语言下生成正确消息，未知 code 进入受控通用反馈。
- 日期数字格式测试验证两种语言结果来自同一原始值，技术标识不被格式化。

### 组件测试

- 登录页在没有偏好时采用浏览器匹配或英文回退，切换后用户名、密码、保持登录选择和当前错误生命周期符合设计。
- 应用壳语言入口在展开和折叠状态可达，切换后路由、账号身份、侧栏状态和页面输入不丢失。
- 概览、在线玩家、踢出确认、API Key 列表/创建/一次性结果/撤销以及外观菜单分别断言 `en` 和 `zh-CN` 的可见文案、可访问名称和反馈。
- `UApp :locale` 验证 Nuxt UI 内置文本和日期组件 locale 跟随应用语言，不形成中英文混排。
- 表单验证在两种语言下显示 Valibot 官方 issue，语言切换后重新校验不清除输入。
- 动态玩家名、身份标识、API Key 名称和角色代码在切换前后保持原值。

### 构建与浏览器 E2E

- lint、TypeScript、Vitest 和生产 Vite 构建必须通过；构建确认 JSON 消息被预编译且产物不依赖远程语言资源。
- 真实浏览器覆盖 `zh-CN`、`en-US`、不支持语言、繁体优先加英文次选，以及无可匹配语言的首访行为。
- 登录前选择语言后刷新仍保留；登录后语言入口使用同一偏好；登出、Token 到期和 401 不清除语言。
- 两种语言分别完成当前登录、概览、在线玩家和 API Key 关键流程，验证 Header-only Bearer、安全输入保留和稳定错误码映射不回归。
- 桌面、`390x844` 和 320 CSS 像素检查英文扩展、语言菜单、对话框、表格/列表和操作按钮无重叠、不可达命令或页面水平溢出。
- 浏览器检查 Nuxt UI 内置文案、Valibot issue、日期数字、技术标识原值、缺失键泄漏、控制台错误和 CSP 违规。

真实 OWIN/Edge 验收需要 `SEVENDPANEL_ADMIN_URL`、`PANEL_USERNAME` 和 `PANEL_PASSWORD` 等受控本地环境值。缺失时只报告 suite discovery 和未执行原因，不能用历史认证结果或 mock 后端替代当前双语合同。

## 文档影响与提升

- [产品需求](../../PRD.md)已经拥有 `NFR-03` 产品合同，本规格不重复改变语言范围或验收目标。
- [产品设计](../../design.md)已经拥有语言入口、持久化、内容边界和输入保留规则；实现若没有改变这些规则，不再重复扩写。
- 本规格批准后创建一份实施计划，不在计划中重新决定资源格式、locale 匹配、Storage 键或错误文案所有权。
- 实现与验证稳定后，把实际 i18n bootstrap、消息目录、偏好 repository、Nuxt UI/Valibot 同步和反馈 code 边界提升到[系统架构](../../architecture.md)。
- 自动化稳定后，把已运行命令、断言数量、浏览器环境和证据缺口更新到[测试策略](../../test.md)，不能把 suite 发现写成真实浏览器通过。
- [Admin 前端目标蓝图](../../architecture/admin-frontend-target-blueprint.md)继续描述批准目标；只有依赖状态、边界或证据缺口随实现发生变化时才同步，不把本规格当作当前实现证据。
- 发布后才把用户可见双语能力加入 `CHANGELOG.md`；设计批准、依赖安装和实现提交均不等于已经发布。