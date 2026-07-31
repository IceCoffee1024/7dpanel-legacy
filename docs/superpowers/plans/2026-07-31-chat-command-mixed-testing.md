---
state: Current
document_role: Implementation Plan
primary_spec: ../specs/2026-07-31-chat-command-mixed-testing-design.md
last_updated: "2026-07-31"
---

# 聊天命令混合测试实施计划

## 主规格与权威边界

本计划只实施[聊天命令混合测试设计规格](../specs/2026-07-31-chat-command-mixed-testing-design.md)。当前组件与生命周期事实以[系统架构](../../architecture.md)为准，测试分级、真实环境门禁和最终证据状态以[测试策略](../../test.md)为准。本计划是待执行变更记录，不是当前实现或验证通过证据。

## 目标

交付一条虚拟玩家为主门禁、真实 `ClientInfo` 为可选窄边界的聊天命令测试链。默认配置完全关闭；真实玩家按稳定跨平台 ID 精确选择并在每个场景重新解析；缺少玩家时记录 `Skipped`；kick/restart 永远只进入记录型端口；teleport/reward 只有显式 opt-in 才能调用现有类型化真实端口；整个方案不新增 Harmony patch。

## 执行约束

- 保留工作区中已有的用户修改，先核实 `ChatCommandTesting` 配置雏形再增量实施，不覆盖来源不明的改动。
- 只创建本设计需要的具体 runner、resolver、scenario 和 recording port，不建立通用测试 registry、通用 action bus 或任意命令执行器。
- 虚拟矩阵是必跑主门禁；真实进程测试不能替代它，也不能让单元测试依赖在线服务器。
- `TestPlayerId` 优先精确匹配 `ClientInfo.CrossplatformId.CombinedString`，没有跨平台匹配时精确回退到 `PlatformId.CombinedString`；禁止名称、entity ID、模糊匹配和首玩家回退。
- 每个真实场景都在 `GameThreadDispatcher` 委托内重新解析当前 `ClientInfo`，不得跨场景保存游戏活对象。
- kick/restart 的生产动作实现不得进入测试组合；teleport/reward 必须同时通过功能启用、对应 opt-in、目标重解析和固定场景校验。
- 不新增聊天订阅、HTTP 管理面或 Harmony patch；使用固定管理员控制台命令 `7dp-test chat <status|virtual|boundary|all>` 显式触发。
- 迭代期只运行当前任务的聚焦测试；实现稳定后按测试策略运行一次后端受影响聚合门禁。真实 7DTD 只在具备受控玩家和明确操作授权时执行。
- 本计划不授权 `git commit`、`git push`、`git reset`、`git revert` 或其他历史修改。

## 任务 1：固定配置合同与安全默认值

**文件：**

- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/Configuration/PanelHostConfig.cs`
- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/Configuration/PanelHostConfigurationLoader.cs`
- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/config.example.json`
- 修改：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/PanelHostOptions.cs`
- 新建或完善：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/PanelChatCommandTestingOptions.cs`
- 新建或完善：`backend/tests/LSTY.SevenDPanel.Tests/ChatCommandTestingOptionsTests.cs`

- [ ] 先检查现有未提交配置代码并保留用户修改；不得因本计划重写已经成立的 options 语义。
- [ ] 固定 `Enabled=false`、`TestPlayerId=null`、`AllowTeleport=false`、`AllowRewardDelivery=false` 默认值。
- [ ] 让启用状态要求 trim 后非空的稳定玩家 ID；无效绑定记录安全诊断并整体回退为 disabled。
- [ ] 让 disabled options 即使收到两个 `true` 绑定值也强制关闭真实副作用。
- [ ] 补齐 config 缺失、显式禁用、启用缺 ID、trim、合法启用和 loader 回退测试。
- [ ] 确认示例配置保持全部危险能力关闭，且不包含真实玩家 ID、凭据或机器路径。

**聚焦验证：**

```powershell
dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~ChatCommandTestingOptionsTests|FullyQualifiedName~PanelHostOptionsTests"
```

## 任务 2：建立虚拟玩家主矩阵

**文件：**

- 修改：`backend/tests/LSTY.SevenDPanel.Tests/GameChatCommunityCommandBridgeTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/CommunityGameCommandTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/ChatMuteAndCommandTests.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/ChatCommandVirtualPlayerMatrixTests.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/Support/ChatCommandVirtualPlayerFixture.cs`

- [ ] 先写虚拟玩家标量 fixture，包含稳定 `crossplatformId`、显示名、在线快照、世界/位置和固定时钟，不引用或构造 `ClientInfo`。
- [ ] 复用生产 `GameChatCommandCatalog`、Community router/consumer 和 Application 用例，使用隔离 SQLite 与具体 recording ports。
- [ ] 覆盖 `help`、名称/别名、大小写、前缀、`AllowNoPrefix`、参数分隔符、普通聊天放行、未知/停用/非法参数和异常结果。
- [ ] 覆盖经济、商店、兑换、daily、家、城市、TPA、votekick/voterestart 的代表性成功、拒绝、幂等和并发场景。
- [ ] 对每个状态变更断言 consumer/typed port 最多调用一次，并固定审计 `Begin → Execute once → Complete` 顺序。
- [ ] 让 intent 写失败不执行业务动作，terminal 写失败保留 pending 且不重放；扫描结果不含命令参数、兑换码或私发正文。
- [ ] 将依赖未初始化 `ClientInfo` 的业务正确性断言迁移到虚拟矩阵；只保留确实用于游戏程序集签名兼容的窄测试。

**聚焦验证：**

```powershell
dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~ChatCommandVirtualPlayerMatrixTests|FullyQualifiedName~GameChatCommunityCommandBridgeTests|FullyQualifiedName~CommunityGameCommandTests|FullyQualifiedName~ChatMuteAndCommandTests"
```

## 任务 3：实现真实玩家 resolver 与每场景重解析

**文件：**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Runtime/ChatTesting/SevenDaysChatCommandTestPlayerResolver.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Runtime/ChatTesting/ChatCommandTestPlayerResolution.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/ChatCommandRealPlayerResolverTests.cs`

- [ ] 先写 resolver 测试，固定零匹配、唯一匹配、多个匹配、空 `CrossplatformId`、断开中和身份变化语义。
- [ ] resolver 只在 `GameThreadDispatcher` 委托内枚举当前连接，并按 `CombinedString` 精确 ordinal 匹配 `TestPlayerId`。
- [ ] 返回值只复制稳定 ID、当次 entity ID、显示名和解析时间；`ClientInfo` 只在受控委托回调内可见。
- [ ] 为 runner 提供“解析并在同一委托内执行”的具体方法，避免先返回 handle 再异步使用。
- [ ] 证明每个场景都重新枚举连接；场景间离线或身份变化时后续项为 `Skipped`，不复用前一对象。
- [ ] 加入源码/反射边界断言，确保测试结果、Application 和持久模型不含 `ClientInfo`、`EntityPlayer` 或网络对象字段。

**聚焦验证：**

```powershell
dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~ChatCommandRealPlayerResolverTests"
```

## 任务 4：固定场景结果与危险动作策略

**文件：**

- 新建：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/ChatCommandTesting/ChatCommandTestResult.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Runtime/ChatTesting/ChatCommandTestScenarioSet.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Runtime/ChatTesting/RecordingKickTestPort.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Runtime/ChatTesting/RecordingRestartTestPort.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/ChatCommandSideEffectPolicyTests.cs`

- [ ] 定义固定 `Passed`、`Failed`、`Skipped`、`RecordedOnly` 结果和稳定原因码，不用自由文本推断状态。
- [ ] 建立固定场景集；只允许类型化参数，不接受配置中的任意聊天命令、坐标、物品、奖励包、脚本或控制台原文。
- [ ] 在测试组合根中强制把 kick/restart 绑定到 recording ports，并断言任何 options 组合都无法解析生产动作实现。
- [ ] 默认将 teleport/reward 标记为 `Skipped`；只有对应 opt-in 有效且当前场景再次解析到同一稳定玩家时才调用现有类型化端口。
- [ ] 对 opt-in 状态变更使用稳定幂等键；动作开始后不因测试取消、日志故障或超时自动重试。
- [ ] 结果未知沿用既有类型化未知语义，不能记作 `Passed`；record-only 只证明动作意图和调用次数，不证明真实副作用。
- [ ] 敏感字段测试拒绝完整正文、参数、兑换码、Token、网络对象字符串和 `ClientInfo.ToString()`。

**聚焦验证：**

```powershell
dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~ChatCommandSideEffectPolicyTests"
```

## 任务 5：接入一次性真实 `ClientInfo` 运行器

**文件：**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Runtime/ChatTesting/ChatCommandMixedTestRuntime.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Runtime/ChatTesting/ChatCommandTestEvidenceLogger.cs`
- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/SevenDaysChatRuntimeTests.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/ChatCommandMixedTestRuntimeTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/DependencyInjectionTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/DependencyRulesTests.cs`

- [x] disabled 时只允许 `status`/`virtual`，不解析玩家、不注册聊天事件，也不触碰任何游戏动作端口。
- [x] 控制台桥随生命周期注册；`Start` 不运行场景，只有 `GameReady` 后的管理员显式命令执行诊断。
- [x] identity、current-position 和 private-reply 三个边界探针分别在游戏线程重新解析 `ClientInfo`；更广的真实命令消费者仍留给受控真实验收。
- [x] 不广播测试文本，不替换生产活动目录，不注册第二个 `ModEvents.ChatMessage` handler，也不 patch 任何 Harmony 目标。
- [ ] 使用当次真实 `ClientInfo` 验证私发回复；委托外只保留稳定标量、结果码和调用次数。
- [ ] 玩家缺失、多个匹配、正在断开或 game-not-ready 时记录 `Skipped` 并继续安全收束，不选择替代玩家。
- [ ] `Stop` 拒绝新场景、取消未开始工作并限时收束已开始工作；未知结果不重试。
- [ ] 结构化汇总分别报告 virtual、real、skipped、record-only 和 failed 数量，不把 skip/record-only 计为真实通过。
- [ ] DI 测试证明正常组合不增加第二套 catalog/registry，Core/Application 不引用游戏类型，发布物不新增 Harmony 依赖。

**聚焦验证：**

```powershell
dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~ChatCommandMixedTestRuntimeTests|FullyQualifiedName~SevenDaysChatRuntimeTests|FullyQualifiedName~DependencyInjectionTests|FullyQualifiedName~DependencyRulesTests"
```

## 任务 6：稳定后执行受影响聚合门禁

- [ ] 运行 canonical `net48` Release build，确认无新增 warning/error。
- [ ] 运行聊天、Community、奖励、传送、投票、配置、DI 和依赖规则的受影响测试聚合。
- [ ] 运行 `git diff --check`，检查新增文档与代码没有尾随空白或冲突标记。
- [ ] 检查发布示例配置仍为 disabled、无真实玩家 ID，且发布依赖中没有新增 `0Harmony.dll` 或测试专用第三方包。
- [ ] 不运行浏览器 E2E、Admin 构建、publish 或真实 7DTD；这些边界未被实现迭代本身触发。

**聚合命令：**

```powershell
dotnet build backend/7DPanel.sln --configuration Release
dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~ChatCommand|FullyQualifiedName~GameChat|FullyQualifiedName~Community|FullyQualifiedName~Reward|FullyQualifiedName~Teleport|FullyQualifiedName~Vote|FullyQualifiedName~PanelHostOptions|FullyQualifiedName~DependencyInjection|FullyQualifiedName~DependencyRules"
git diff --check
```

## 任务 7：执行受控真实进程验收

**前置条件：** Windows `v3.0.1-b4` 受控服务器、可恢复配置、一个明确同意参与的在线测试玩家及其精确 `CrossplatformId.CombinedString`、可归档的安全日志。若任一前置条件缺失，本任务保持未完成，不得用虚拟结果代替。

- [ ] 先以默认配置启动，确认没有 chat-command testing 启动记录或游戏副作用。
- [ ] 逐字节备份配置，只启用 `Enabled=true` 并填写稳定 `TestPlayerId`；保持 teleport/reward opt-in 为 false。
- [ ] 玩家在线时验证唯一解析、至少一个只读命令、现有私发回复和结构化结果。
- [ ] 验证 kick/restart 都是 `RecordedOnly`，没有玩家断开、重启脚本或关服动作。
- [ ] 验证 teleport/reward 都是 `Skipped`，玩家位置、库存和奖励状态未因默认测试变化。
- [ ] 在玩家缺失或场景间离线时确认 `Skipped`，日志中没有名称/首玩家回退和自动重试。
- [ ] 只有用户另行明确授权对应真实副作用时，才分别打开一个 opt-in 并执行一个固定窄场景；未获授权时保持未完成并记录未执行。
- [ ] 恢复 `Enabled=false` 和原配置，正常关服，确认 worker 收束且没有新增 Harmony patch/unpatch 记录。
- [ ] 归档脱敏汇总；`Skipped`、`RecordedOnly` 和未授权 opt-in 不得写成真实动作通过。

## 任务 8：证据提升与交付

**实施完成后评估的文件：**

- `docs/architecture.md`
- `docs/test.md`
- `backend/README.md`

- [ ] 只有代码、自动化和适用真实证据完成后，才把已验证的运行边界提升到系统架构；未执行的真实副作用继续标为缺口。
- [ ] 在测试策略记录精确测试命令、数量、真实环境、`Skipped`/`RecordedOnly` 和 opt-in 执行状态，不复制本计划的实现任务。
- [ ] 仅当新增了可运行配置或模块命令时更新最近的 backend README；根 README 继续只保留仓库聚合入口。
- [ ] 不更新 `CHANGELOG.md`，因为测试基础设施不是已发布的用户可见能力。
- [ ] 交付报告列出修改文件、自动化结果、真实玩家前置条件、未执行 opt-in 和所有保留缺口；不提交 Git。

## 完成判定

- [ ] 虚拟玩家矩阵是稳定、快速且覆盖完整命令业务的主门禁。
- [ ] 默认配置禁用测试功能，teleport/reward 默认关闭，kick/restart 无真实实现可达。
- [ ] 真实目标只由稳定 ID 选择，每个场景重新解析 `ClientInfo`，缺失时明确 `Skipped`。
- [ ] 真实窄通道验证了现有聊天命令入口和私发回复，且未新增 Harmony patch、第二个聊天订阅或生产命令目录。
- [ ] 证据明确区分 `Passed`、`Failed`、`Skipped` 与 `RecordedOnly`，不泄漏敏感正文或网络对象。
- [ ] living docs 只根据实际代码和验证结果更新；未执行的真实传送、奖励或跨平台环境继续保留为缺口。
