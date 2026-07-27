---
state: Current
document_role: Implementation Plan
primary_spec: ../specs/2026-07-26-legacy-parity-jobs-backup-design.md
last_updated: "2026-07-27"
---

# 第二波：持久作业、备份恢复与调度实施计划

## 2026-07-27 当前执行记录

- 已实现纵向切片：Domain/Application 类型化作业合同、`009_JobsBackupsSchedules.sql` 与 SQLite stores、有界 worker、世界/Panel 数据库/服务器配置备份、保留与删除、跨重启 marker/receipt 恢复、Cron/公告/计划执行、DI/生命周期、Owner Web 合同及 Admin 备份/调度页面均已有当前实现和测试文件。
- 已知执行证据：本轮 `pnpm api:schema` 为 `1/1`、`pnpm api:gen` 成功，Bootstrap Release build 为 `0 warning`、`0 error`；备份策略 API/composable/面板/页面聚焦 Vitest 为 `4/4` 通过，备份策略 i18n 消息合同聚焦为 `1/1` 通过，`pnpm typecheck` 通过。当前可见记录仍没有可明确归属于任务 1～7 或 Jobs HTTP 过滤命令的通过结果；测试文件存在只证明测试已编写，Bootstrap build 不替代聚合测试。
- 未执行门禁与真实缺口：最终 Admin typecheck 以及 AppShell/router/i18n/Community/备份策略聚焦组合已通过，但 Backups/Schedules 完整聚焦组合、后端/Admin 聚合、publish、Playwright、浏览器及真实 `v3.0.1-b4` 世界备份恢复均未确认；`WorldRestoreTimingGate` 仍以 `world_restore_timing_unverified` 稳定拒绝未获真实时序证据的世界恢复，Live 测试文件不存在。Current 文档只记录代码与现有证据，不代表真实恢复已经完成。

> **面向智能体执行者：** 同一会话逐任务实施时使用 `superpowers:subagent-driven-development`；在独立执行会话按检查点实施时使用 `superpowers:executing-plans`。每次只推进一个任务，并在进入下一任务前核对该任务的聚焦测试结果。

**目标：** 按[持久作业、备份恢复与调度设计规格](../specs/2026-07-26-legacy-parity-jobs-backup-design.md)交付可跨进程重启恢复的持久作业底座、世界/Panel 数据库/服务器配置三类备份与恢复、即时及规则公告、Cron 调度、计划命令和计划重启，并把后端合同生成到 Admin 的两个可操作页面。

**架构：** 新增纯领域项目承载状态机、Cron 和并发策略；Application 定义七类作业的类型化合同；SQLite 保存共享生命周期、各自 payload、备份目录和计划；Local Adapter 承担有界消费、Cron 唤醒、文件归档和启动前恢复；Bootstrap 严格执行“恢复标记 → migration → 恢复结果归并 → worker/scheduler → OWIN”，停止时逆序。Web 仅暴露类型化应用服务，Admin 只消费生成客户端并以服务端状态为准。

**技术栈：** .NET Framework/C#、Cronos 0.13.0、DbUp、Dapper、Microsoft.Data.Sqlite、OWIN；Vue 3 Composition API、`<script setup lang="ts">`、Pinia Colada、Nuxt UI、Hey API、Vitest、Vue Test Utils。

## 执行边界与依据

- Current 产品合同：[PRD](../../PRD.md)中的 `CAP-03`、`CAP-04`、`CAP-05` 与 `NFR-01` 至 `NFR-05`。
- Current 实现事实：[架构](../../architecture.md)、[界面设计](../../design.md)、[测试策略](../../test.md)。只有已实现且通过验证的事实才能提升到这些文档。
- Target 对齐依据：[后端目标蓝图](../../architecture/backend-target-blueprint.md)、[Admin 前端目标蓝图](../../architecture/admin-frontend-target-blueprint.md)、[旧版功能对齐目标蓝图](../../architecture/legacy-feature-parity-target-blueprint.md)。这些文档是目标，不是当前实现证据。
- 仓库外旧项目 `7dtd-serveradmin` 始终只读，只能用于核对可观察行为、字段和运行历史；不得复制源码、DTO、页面模板、脚本或目录结构。
- 本波只允许七种作业：`WorldBackup`、`PanelDatabaseBackup`、`ServerConfigurationBackup`、`Restore`、`ScheduledConsoleCommand`、`ScheduledRestart`、`ScheduledAnnouncement`。共享表只保存生命周期；每种作业使用自己的 payload 表，禁止 JSON 万能 payload。
- 公告内容仅为长度 `1..500` 的纯文本。不得引入模板语言、脚本平台、通用事件总线、作业 DAG、动态 handler registry 或没有本波生产消费者的接口。
- 默认不执行 publish、Playwright 或额外浏览器 smoke。真实 7DTD 只在任务 10 执行一次世界保存/备份和一次跨重启恢复及时序验证。
- 任务 1、2 是串行合流点；任务 3、4 在 migration 和合同稳定后可分派给不同 worker；任务 5 必须在 3、4 合流后执行；任务 6 可与任务 5 的非共享文件部分并行；任务 7、8 串行合流；任务 9 在生成客户端完成后执行；任务 10 最后执行。

## 固定项目与依赖方向

| 路径 | 动作 | 职责 | 依赖限制 |
| --- | --- | --- | --- |
| `backend/src/Core/LSTY.SevenDPanel.Domain/` | 创建 | 作业状态机、备份类别、Cron 值对象、并发策略 | 仅 BCL 与 Cronos；不得依赖 Application、Adapter、Web、Bootstrap |
| `backend/src/Core/LSTY.SevenDPanel.Application/Jobs/` | 创建 | 七类作业命令、查询、类型化 payload 与端口 | 依赖 Domain；不得依赖具体 SQLite、文件系统或 7DTD 实现 |
| `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Jobs/` | 创建 | 作业、payload、备份目录和计划的事务性持久化 | 实现 Application 端口；不承载业务状态转换 |
| `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/` | 创建 | 持久 worker、Cron scheduler、ZIP、原子文件与恢复 marker | 依赖 Application、Domain 及现有类型化 7DTD gateway |
| `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/` | 修改 | HTTP 路由、鉴权、Problem Details 和下载响应 | 只调用 Application；不得直接访问 SQLite 或磁盘 |
| `backend/src/Bootstrap/LSTY.SevenDPanel/` | 修改 | migration 前恢复、DI、运行顺序和逆序停止 | 唯一组合根；显式组合全部生产实现 |
| `frontend/apps/admin/src/features/backups/` | 创建 | “备份与恢复”纵向功能 | 只经生成客户端访问 HTTP；不得猜测成功状态 |
| `frontend/apps/admin/src/features/schedules/` | 创建 | “公告与调度”纵向功能 | 使用类型化表单和服务端返回的下一触发时间 |

## 固定领域与持久化合同

```csharp
public enum JobKind
{
    WorldBackup,
    PanelDatabaseBackup,
    ServerConfigurationBackup,
    Restore,
    ScheduledConsoleCommand,
    ScheduledRestart,
    ScheduledAnnouncement
}

public enum JobStatus
{
    Queued,
    Running,
    PendingRestart,
    Succeeded,
    Failed,
    Cancelled,
    Interrupted,
    ResultUnknown
}

public enum BackupKind { World, PanelDatabase, ServerConfiguration }
public enum ScheduleConcurrencyPolicy { SkipIfRunning, QueueOne }

public interface IJobStore
{
    JobRecord Enqueue(NewJob job);
    JobRecord? TryClaimNext(string workerId, DateTimeOffset now);
    bool TryTransition(Guid jobId, long expectedRowVersion, JobStatus expected,
                       JobStatus next, JobCompletion completion);
    JobRecord Get(Guid jobId);
    PagedResult<JobRecord, JobCursor> List(JobQuery query);
}

public interface IJobSubmissionStore
{
    JobRecord Enqueue(NewJob job, WorldBackupPayload payload);
    JobRecord Enqueue(NewJob job, PanelDatabaseBackupPayload payload);
    JobRecord Enqueue(NewJob job, ServerConfigurationBackupPayload payload);
    JobRecord Enqueue(NewJob job, RestorePayload payload);
    JobRecord Enqueue(NewJob job, ScheduledConsoleCommandPayload payload);
    JobRecord Enqueue(NewJob job, ScheduledRestartPayload payload);
    JobRecord Enqueue(NewJob job, ScheduledAnnouncementPayload payload);
}

public interface IJobPayloadReader
{
    WorldBackupPayload GetWorldBackup(Guid jobId);
    PanelDatabaseBackupPayload GetPanelDatabaseBackup(Guid jobId);
    ServerConfigurationBackupPayload GetServerConfigurationBackup(Guid jobId);
    RestorePayload GetRestore(Guid jobId);
    ScheduledConsoleCommandPayload GetScheduledConsoleCommand(Guid jobId);
    ScheduledRestartPayload GetScheduledRestart(Guid jobId);
    ScheduledAnnouncementPayload GetScheduledAnnouncement(Guid jobId);
}

public interface IBackupCatalog
{
    BackupArtifact Add(CompletedBackup backup);
    BackupArtifact Get(Guid backupId);
    PagedResult<BackupArtifact, BackupCursor> List(BackupQuery query);
    bool Delete(Guid backupId);
}

public interface IScheduleStore
{
    ScheduleRecord Upsert(ScheduleDefinition definition);
    IReadOnlyList<ScheduleRecord> ClaimDue(DateTimeOffset now, string ownerId);
    void RecordOutcome(ScheduleRunOutcome outcome);
}
```

`009_JobsBackupsSchedules.sql` 是本波唯一 migration，关键 schema 固定为：

```sql
jobs(id, kind, status, actor_subject, source_schedule_id, idempotency_key,
     correlation_id, created_at_utc, started_at_utc, completed_at_utc,
     progress_current, progress_total, error_code, worker_id, row_version)
world_backup_job_payloads(job_id, world_name)
panel_database_backup_job_payloads(job_id)
server_configuration_backup_job_payloads(job_id)
restore_job_payloads(job_id, backup_id, backup_kind, restart_after_stage)
scheduled_console_command_job_payloads(job_id, schedule_id, command_text)
scheduled_restart_job_payloads(job_id, schedule_id, countdown_seconds)
scheduled_announcement_job_payloads(job_id, schedule_id, message_text)
backup_artifacts(id, kind, backup_root_id, relative_resource_id, size_bytes,
                 sha256, world_id, game_version, validation_status,
                 created_at_utc, source_job_id, manifest_version)
backup_policies(kind, enabled, cron_expression, time_zone_id, backup_root_id,
                retention_count, retention_days, compression_enabled, row_version)
schedules(id, kind, name, cron_expression, time_zone_id, enabled,
          concurrency_policy, command_text, countdown_seconds, message_text,
          next_occurrence_utc, last_occurrence_utc, row_version)
schedule_runs(id, schedule_id, scheduled_for_utc, job_id, outcome,
              created_at_utc)
job_admin_operations(id, actor_subject, action, target_kind, target_id,
                     status, occurred_utc, correlation_id)
```

## 任务 1：建立 Domain、Application 类型化合同与项目引用

**文件：**

- Create: `backend/src/Core/LSTY.SevenDPanel.Domain/LSTY.SevenDPanel.Domain.csproj`
- Create: `backend/src/Core/LSTY.SevenDPanel.Domain/Jobs/JobKind.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Domain/Jobs/JobStatus.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Domain/Jobs/JobStateMachine.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Domain/Backups/BackupKind.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Domain/Schedules/CronSchedule.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Domain/Schedules/ScheduleConcurrencyPolicy.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Jobs/JobContracts.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Jobs/JobPayloads.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Jobs/IJobStore.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Jobs/IJobSubmissionStore.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Jobs/IJobPayloadReader.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Backups/IBackupCatalog.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Schedules/IScheduleStore.cs`
- Modify: `backend/src/Core/LSTY.SevenDPanel.Application/LSTY.SevenDPanel.Application.csproj`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj`
- Modify: `backend/7DPanel.sln`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/Domain/Jobs/JobStateMachineTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/Domain/Schedules/CronScheduleTests.cs`

- [x] 先写状态迁移测试，固定 `Queued → Running → Succeeded|Failed|Interrupted|ResultUnknown`、`Queued → Cancelled`、`Restore: Queued → PendingRestart → Running → Succeeded|Failed|ResultUnknown`；终态不可再次迁移，普通作业不得进入 `PendingRestart`。
- [x] 写 Cron 测试，固定 `CronExpression.Parse(expression, CronFormat.Standard)`、显式 `TimeZoneInfo`、DST 下一个触发点，以及日字段和星期字段同时给出时的 AND 语义；非法表达式返回 `cron_invalid`，未知时区返回 `time_zone_invalid`。
- [ ] 运行 RED：`dotnet test backend/7DPanel.sln --filter "FullyQualifiedName~JobStateMachineTests|FullyQualifiedName~CronScheduleTests"`；预期测试因 Domain 类型和 Application 合同尚不存在而编译失败。
- [x] 创建 Domain 项目并固定 `Cronos` 包版本 `0.13.0`；实现不可变 `CronSchedule` 与显式状态机，不把数据库时间戳或 HTTP DTO 放入 Domain。
- [x] 在 Application 定义七个独立 payload：`WorldBackupPayload`、`PanelDatabaseBackupPayload`、`ServerConfigurationBackupPayload`、`RestorePayload`、`ScheduledConsoleCommandPayload`、`ScheduledRestartPayload`、`ScheduledAnnouncementPayload`；共享 `JobRecord` 不含 payload JSON。
- [x] 将 Domain 加入 solution，并让 Application、Bootstrap 和测试项目显式引用 Domain；不得让 Domain 反向引用任何现有项目。
- [ ] 运行 GREEN：`dotnet test backend/7DPanel.sln --filter "FullyQualifiedName~JobStateMachineTests|FullyQualifiedName~CronScheduleTests"`；预期全部通过，并覆盖非法转换、DST 与非法 Cron。

## 任务 2：落地 009 migration 与事务性 SQLite stores

**文件：**

- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Migrations/009_JobsBackupsSchedules.sql`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Jobs/SqliteJobStore.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Jobs/SqliteJobPayloadStore.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Backups/SqliteBackupCatalog.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Schedules/SqliteScheduleStore.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/LSTY.SevenDPanel.Adapters.Persistence.Sqlite.csproj`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/Persistence/JobsBackupsSchedulesMigrationTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/Persistence/SqliteJobStoreTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/Persistence/SqliteScheduleStoreTests.cs`

- [x] 先写 migration 测试，断言空数据库执行到 `009` 后出现固定的十三张表、外键、`jobs(status, created_at_utc)` 与 `schedules(enabled, next_occurrence_utc)` 索引，并能从第一波 `008_EvidenceFoundation.sql` 升级且保留现有数据。
- [x] 先写竞争测试：两个连接只能有一个通过 `BEGIN IMMEDIATE` 把同一 `Queued` 作业 claim 为 `Running`；`row_version` 不匹配时更新返回 false；每种 `JobKind` 只能插入对应 payload 表。
- [x] 先写计划幂等测试：`schedule_runs(schedule_id, scheduled_for_utc)` 唯一；重复唤醒不得生成第二个 job；`QueueOne` 至多保留一个尚未开始的后继 job，`SkipIfRunning` 记录跳过结果。
- [ ] 运行 RED：`dotnet test backend/7DPanel.sln --filter "FullyQualifiedName~JobsBackupsSchedulesMigrationTests|FullyQualifiedName~SqliteJobStoreTests|FullyQualifiedName~SqliteScheduleStoreTests"`；预期因 `009` 和 store 尚不存在而失败。
- [x] 编写单一 `009` migration：状态和 kind 使用 CHECK 约束，所有时间以 Unix 毫秒保存；路径只保存批准备份根 ID 与服务端生成的相对资源 ID，payload 主键同时是指向 `jobs(id)` 的外键。migration 重新创建第一波 `unified_audit_projection`，只把 `jobs`、`schedule_runs` 和 `job_admin_operations` 的稳定摘要并入查询，不复制备份路径、公告正文或命令输出。
- [x] 实现 `SqliteJobStore` 的 enqueue、claim、条件转换、分页查询和启动恢复：进程退出遗留的 `Running` 先转为 `Interrupted`，再由各作业类型判断是否可安全重新排队；已开始副作用但无法证明结果的作业转为 `ResultUnknown`。已经开始写归档但未发布的临时文件由 Local Adapter 清理；恢复作业按 marker/receipt 归并，不能被普通重排覆盖。
- [x] 实现 `SqliteBackupCatalog` 与 `SqliteScheduleStore`；`IJobSubmissionStore` 的七类提交和 `claim due schedule + schedule_run + job + typed payload` 各自在一个事务内完成，`IJobPayloadReader` 提供七个显式读取入口。同一幂等键若对应不同 payload 必须返回 `job_idempotency_conflict`。
- [ ] 运行 GREEN：`dotnet test backend/7DPanel.sln --filter "FullyQualifiedName~JobsBackupsSchedulesMigrationTests|FullyQualifiedName~SqliteJobStoreTests|FullyQualifiedName~SqliteScheduleStoreTests"`；预期 migration、双连接竞争、payload 约束和计划幂等测试全部通过。

## 任务 3：交付有界持久 worker 与世界备份首个纵向切片

**文件：**

- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/LSTY.SevenDPanel.Adapters.Local.csproj`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/Files/ApprovedStorageRoots.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/Files/AtomicFileWriter.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/Backups/FileSystemBackupArchiveStore.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/Backups/BackupManifest.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/Jobs/BackgroundWorkConsumer.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Backups/CreateWorldBackup.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Backups/IWorldSaveGateway.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Backups/SevenDaysWorldSaveGateway.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/LSTY.SevenDPanel.Adapters.Local.csproj`
- Modify: `backend/7DPanel.sln`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/Local/ApprovedStorageRootsTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/Local/WorldBackupJobTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/Local/BackgroundWorkConsumerTests.cs`

- [x] 先写安全测试：拒绝绝对路径、`..`、符号链接/重解析点越界、ZIP entry 越界和批准根之外的下载；临时归档必须与目标同卷并通过原子 rename 发布。
- [x] 先写世界备份切片测试：Application 在同一事务写 `jobs` 和 `world_backup_job_payloads`；worker claim 后通过专用 `IWorldSaveGateway` 在 `GameThreadDispatcher` 请求并确认保存提交，随后只归档批准的当前世界目录，写 manifest、SHA-256 和 catalog，最后转为 `Succeeded`。不得把控制台原文作为保存合同。
- [x] 先写故障测试：保存命令失败、源目录消失、ZIP 写入失败或 checksum 失败时作业为 `Failed` 且有稳定错误码；未发布临时文件被清理；正在运行的同类操作不并行写同一目标。
- [ ] 运行 RED：`dotnet test backend/7DPanel.sln --filter "FullyQualifiedName~ApprovedStorageRootsTests|FullyQualifiedName~WorldBackupJobTests|FullyQualifiedName~BackgroundWorkConsumerTests"`；预期因 Local Adapter 和世界备份 handler 尚不存在而失败。
- [x] 实现 `ApprovedStorageRoots`，只接收 Bootstrap 提供的规范化世界根、Panel 状态根、服务器配置根和备份根；实现 `AtomicFileWriter` 与版本化 manifest，不接受调用方自定义任意根。
- [x] 实现单进程、有界、可停止的 `BackgroundWorkConsumer`：显式 switch 七种 `JobKind`，本任务只接通 `WorldBackup`；空队列退避，停止时不 claim 新作业，已 claim 作业在边界点完成或记录可重试失败。
- [x] 实现世界备份 handler，成功顺序固定为“游戏线程保存 → ZIP 临时文件 → manifest/checksum → 原子发布 → catalog → job 终态”；进度只写确定的文件数/字节数，不伪造百分比。
- [ ] 运行 GREEN：`dotnet test backend/7DPanel.sln --filter "FullyQualifiedName~ApprovedStorageRootsTests|FullyQualifiedName~WorldBackupJobTests|FullyQualifiedName~BackgroundWorkConsumerTests"`；预期路径攻击、失败清理、状态顺序和成功归档测试全部通过。

## 任务 4：补齐 Panel 数据库与服务器配置备份、目录、保留和删除

**文件：**

- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Backups/CreatePanelDatabaseBackup.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Backups/CreateServerConfigurationBackup.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Backups/BackupCatalogService.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/Backups/PanelDatabaseBackupJobHandler.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/Backups/ServerConfigurationBackupJobHandler.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/Backups/BackupRetentionService.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/Jobs/BackgroundWorkConsumer.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/Local/PanelDatabaseBackupJobTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/Local/ServerConfigurationBackupJobTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/Application/BackupCatalogServiceTests.cs`

- [x] 先写数据库备份测试，要求通过 `Microsoft.Data.Sqlite` 的在线备份能力得到一致副本，再归档副本；不得复制活动中的 WAL/SHM 组合冒充备份。
- [x] 先写配置备份测试，固定批准清单中的服务器 XML/配置文件，保持相对路径并排除日志、世界数据、Panel 数据库、备份根和秘密外溢文件；空清单与缺失必需文件返回稳定失败。
- [x] 先写目录测试，按 kind、创建时间和分页列出；下载前复核 catalog 相对路径、文件存在、大小和 SHA-256；删除先做条件检查再删文件和 catalog，恢复 marker 正在引用的 artifact 返回 `backup_in_use`。
- [x] 先写保留测试：每个 kind 独立按完成时间保留配置数量；只清理有 catalog 记录、超过保留数且不在恢复中的归档；清理失败记录错误但不得把刚成功的备份改成失败。
- [ ] 运行 RED：`dotnet test backend/7DPanel.sln --filter "FullyQualifiedName~PanelDatabaseBackupJobTests|FullyQualifiedName~ServerConfigurationBackupJobTests|FullyQualifiedName~BackupCatalogServiceTests"`；预期两个 handler 与 catalog service 缺失而失败。
- [x] 实现两种 Application command 和 Local handler，复用任务 3 的归档发布原语，但保留三个明确入口与 payload；不得引入按字符串选择来源目录的通用备份脚本。
- [x] 实现查询、下载解析、删除和 `BackupRetentionService`；所有外部返回文件名都由 kind、UTC 时间与 artifact id 生成，不使用用户输入拼接路径。
- [ ] 运行 GREEN：`dotnet test backend/7DPanel.sln --filter "FullyQualifiedName~PanelDatabaseBackupJobTests|FullyQualifiedName~ServerConfigurationBackupJobTests|FullyQualifiedName~BackupCatalogServiceTests"`；预期一致性、批准清单、checksum、保留和占用保护测试全部通过。

## 任务 5：实现跨重启恢复、回滚、receipt 归并与世界时序门禁

**文件：**

- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Backups/StageRestore.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/Restore/PendingRestoreMarker.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/Restore/JsonPendingRestoreStore.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/Restore/PendingRestoreApplier.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/Restore/RestoreResultReceipt.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/Restore/RestoreResultReconciler.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/Restore/WorldRestoreTimingGate.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/Local/JsonPendingRestoreStoreTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/Local/PendingRestoreApplierTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/Local/RestoreResultReconcilerTests.cs`

- [x] 先写 stage 测试：只接受 catalog 中 checksum、manifest 和 kind 均匹配的 artifact；一次只允许一个 marker；原子写 marker 后把 `Restore` 作业置为 `PendingRestart`，最后才调用现有 `IRestartScriptLauncher`。
- [x] 先写三类恢复测试：恢复前创建同卷 safety copy；验证 ZIP entry、manifest 和批准目标；提取到 staging；原子替换；失败则回滚；marker 未完成前不得启动 OWIN、worker 或 scheduler。
- [x] 先写 Panel 数据库覆盖场景：marker 携带不可变 job 快照，applier 在数据库不可用时写独立 receipt；被恢复的旧数据库完成 migration 后，reconciler 按 job id upsert/归并 `Succeeded` 或 `Failed`，不得丢失这次恢复终态。
- [x] 先写幂等和断电测试：`Prepared`、`Applied`、`RolledBack` receipt 阶段重复启动不会重复覆盖；checksum 不符、回滚失败、receipt 损坏分别返回稳定错误并保留诊断材料。
- [x] 先写世界门禁测试：只有 `WorldRestoreTimingGate` 中持久化的 `v3.0.1-b4` 实测证据为通过时才可应用世界恢复；证据缺失或不能证明世界打开前执行时，作业失败为 `world_restore_timing_unverified`，绝不在线覆盖。
- [ ] 运行 RED：`dotnet test backend/7DPanel.sln --filter "FullyQualifiedName~JsonPendingRestoreStoreTests|FullyQualifiedName~PendingRestoreApplierTests|FullyQualifiedName~RestoreResultReconcilerTests"`；预期 marker、applier 与 reconciler 缺失而失败。
- [x] 实现版本化 marker/receipt，只保存 artifact id、kind、规范化相对路径、checksum、job 快照和阶段；原子写入 Panel 状态根，禁止把数据库连接或任意 shell 参数写入 marker。
- [x] 实现三类恢复与 safety copy 回滚；服务器配置和 Panel 数据库恢复可由单元/集成测试证明，世界恢复路径在任务 10 实测门禁通过前保持稳定拒绝。
- [ ] 运行 GREEN：`dotnet test backend/7DPanel.sln --filter "FullyQualifiedName~JsonPendingRestoreStoreTests|FullyQualifiedName~PendingRestoreApplierTests|FullyQualifiedName~RestoreResultReconcilerTests"`；预期 stage、幂等、数据库覆盖归并、回滚和未验证世界门禁测试全部通过。

## 任务 6：交付 Cron、错过触发补偿、公告、计划命令与计划重启

**文件：**

- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Schedules/ScheduleContracts.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Schedules/ScheduleService.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Announcements/AnnouncementContracts.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Announcements/IAnnouncementGateway.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Announcements/AnnouncementService.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/Schedules/BackgroundScheduler.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/Schedules/ScheduledJobExecutor.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Announcements/SevenDaysAnnouncementGateway.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/Jobs/BackgroundWorkConsumer.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/Application/ScheduleServiceTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/Local/BackgroundSchedulerTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/SevenDays/SevenDaysAnnouncementGatewayTests.cs`

- [x] 先写 CRUD 验证：计划名称非空，Cron 与时区由 Domain 校验；命令文本非空且不含换行；重启倒计时为规格允许范围；公告严格为纯文本 `1..500`；更新采用 `row_version`，冲突返回 `schedule_conflict`。
- [x] 先写 scheduler 测试：启动时只补偿上次检查后错过的最近一次触发，不回放无界历史；`schedule_runs` 唯一键保证重复 tick 幂等；下一触发时间由保存的时区和 Cronos 计算并返回。
- [x] 先写并发策略测试：前一作业仍为 `Queued|Running|PendingRestart` 时，`SkipIfRunning` 记一次 skipped run 且不建 job；`QueueOne` 可建一个后继 job，后续 tick 继续记 skipped，不形成积压队列。
- [x] 先写三类执行测试：计划命令复用现有 `IConsoleCommandGateway` 和 `GameThreadDispatcher`；计划重启复用 `IRestartScriptLauncher`；计划公告与即时公告都通过类型化 `IAnnouncementGateway`，欢迎、轮播、血月只产生纯文本公告，不执行模板或脚本。
- [ ] 运行 RED：`dotnet test backend/7DPanel.sln --filter "FullyQualifiedName~ScheduleServiceTests|FullyQualifiedName~BackgroundSchedulerTests|FullyQualifiedName~SevenDaysAnnouncementGatewayTests"`；预期 scheduler、公告 gateway 和计划服务缺失而失败。
- [x] 实现 `BackgroundScheduler` 为单实例有界轮询器，claim due 后在事务内创建对应的三类 typed payload；显式处理系统时钟前跳、后跳和 DST，不以本地字符串比较时间。
- [x] 实现 `ScheduledJobExecutor` 的三个显式分支并接入 worker；欢迎公告在玩家进入的现有类型化事件边界发送，轮播和血月通过持久 schedule 触发，三者共享纯文本发送能力而不共享万能 payload。
- [ ] 运行 GREEN：`dotnet test backend/7DPanel.sln --filter "FullyQualifiedName~ScheduleServiceTests|FullyQualifiedName~BackgroundSchedulerTests|FullyQualifiedName~SevenDaysAnnouncementGatewayTests"`；预期 Cron、补偿、幂等、并发策略和三种执行路径全部通过。

## 任务 7：串行合流 migration、恢复、DI 与运行生命周期

**文件：**

- Create: `backend/src/Bootstrap/LSTY.SevenDPanel/Runtime/JobsAndSchedulingRuntime.cs`
- Create: `backend/src/Bootstrap/LSTY.SevenDPanel/Runtime/PendingRestoreStartupStep.cs`
- Modify: `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- Modify: `backend/src/Bootstrap/LSTY.SevenDPanel/ModMain.cs`
- Modify: `backend/src/Bootstrap/LSTY.SevenDPanel/LSTY.SevenDPanel.csproj`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/Bootstrap/JobsAndSchedulingCompositionTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/Bootstrap/JobsAndSchedulingLifecycleTests.cs`

- [x] 先写 composition test，枚举任务 1 至 6 的每个 Application 端口并解析唯一生产实现；验证 `BackgroundWorkConsumer`、`BackgroundScheduler`、`PendingRestoreApplier`、三个 backup handler、三个 scheduled handler 均不是 request scoped 或重复单例。
- [x] 先写 lifecycle test，记录启动序列严格为 `PendingRestoreApplier → DbUp migrations → RestoreResultReconciler → BackgroundWorkConsumer → BackgroundScheduler → OWIN`，停止序列严格逆向；任何启动步骤失败时只停止已经启动的组件。
- [x] 先写重启恢复测试，模拟 worker 在 `Running` 中退出、scheduler 在写 run 后退出、restore receipt 存在三种状态；再次 `InitMod` 后不能丢 job、重复触发 schedule 或在恢复完成前开放 HTTP。
- [ ] 运行 RED：`dotnet test backend/7DPanel.sln --filter "FullyQualifiedName~JobsAndSchedulingCompositionTests|FullyQualifiedName~JobsAndSchedulingLifecycleTests"`；预期 DI 注册与生命周期组件尚未合流而失败。
- [x] 在 `PanelServiceProviderFactory` 显式注册 Domain/Application/SQLite/Local/SevenDays 实现和经过验证的批准根配置；保留现有 `IModRuntime` 装饰链，不另建生命周期框架。
- [x] 实现 `PendingRestoreStartupStep` 与 `JobsAndSchedulingRuntime`，确保 Panel 数据库恢复发生在任何数据库连接/migration 前，receipt 归并发生在 migration 后，HTTP 最后开放。
- [ ] 运行 GREEN：`dotnet test backend/7DPanel.sln --filter "FullyQualifiedName~JobsAndSchedulingCompositionTests|FullyQualifiedName~JobsAndSchedulingLifecycleTests"`；预期唯一注册、完整顺序、部分启动回滚和重启恢复测试全部通过。

## 任务 8：发布 Web 路由、权限、Problem Details 与生成合同

**文件：**

- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/JobsController.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/BackupsController.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/AnnouncementsController.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/SchedulesController.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/JobHttpModels.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/BackupHttpModels.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/ScheduleHttpModels.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OpenApi/PanelOpenApiOperationProcessor.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/Web/JobsBackupsSchedulesApiTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/Web/JobsBackupsSchedulesAuthorizationTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostOpenApiSnapshotTests.cs`
- Generate: `frontend/apps/admin/src/shared/api/generated/`

固定路由合同：

| 方法与路由 | 行为 | 成功结果 |
| --- | --- | --- |
| `GET /api/v1/jobs`、`GET /api/v1/jobs/{jobId}` | 分页历史与详情 | `200`，含 kind/status/progress/error/timestamps |
| `POST /api/v1/jobs/{jobId}/cancel` | 只取消 `Queued` 或明确可取消的 `PendingRestart` | `202`，返回服务端 job |
| `POST /api/v1/backups/world` | 创建世界备份作业 | `202` + job |
| `POST /api/v1/backups/panel-database` | 创建 Panel 数据库备份作业 | `202` + job |
| `POST /api/v1/backups/server-configuration` | 创建服务器配置备份作业 | `202` + job |
| `GET /api/v1/backups`、`GET /api/v1/backups/{backupId}/download` | 目录与校验后下载 | `200` |
| `DELETE /api/v1/backups/{backupId}` | 删除未被恢复引用的归档 | `204` |
| `POST /api/v1/backups/{backupId}/restore` | stage 恢复并按请求重启 | `202` + restore job |
| `POST /api/v1/announcements` | 发送即时纯文本公告 | `202` |
| `GET /api/v1/schedules`、`POST /api/v1/schedules` | 列表与创建 | `200` / `201` |
| `PUT /api/v1/schedules/{scheduleId}` | 带 row version 更新 | `200` |
| `POST /api/v1/schedules/{scheduleId}/enable`、`POST /api/v1/schedules/{scheduleId}/disable` | 切换计划 | `200` |
| `DELETE /api/v1/schedules/{scheduleId}` | 删除且不影响已终态历史 | `204` |

- [x] 先写 API tests，逐条固定上述路由、请求/响应类型、分页、`202` 异步语义、下载头和取消边界；创建接口不得等待长作业完成。
- [x] 先写权限测试：作业、备份和计划的读取与写入均为 `Owner`；只有即时公告额外允许 `Admin`。未认证为 `401`，权限不足为 `403`，不能因资源不存在绕过权限检查。
- [x] 先写 Problem Details 测试，固定 `job_not_found`、`job_not_cancellable`、`backup_not_found`、`backup_in_use`、`backup_integrity_failed`、`restore_already_pending`、`world_restore_timing_unverified`、`cron_invalid`、`time_zone_invalid`、`schedule_conflict`、`announcement_invalid`。
- [ ] 运行 RED：`dotnet test backend/7DPanel.sln --filter "FullyQualifiedName~JobsBackupsSchedulesApiTests|FullyQualifiedName~JobsBackupsSchedulesAuthorizationTests|FullyQualifiedName~OwinWebHostOpenApiSnapshotTests"`；预期路由和 OpenAPI schema 缺失而失败。
- [x] 实现四个 controller，只做验证、鉴权、Application 调用与映射；流式下载前由 Application 重新校验 catalog/checksum，Web 不接收磁盘路径。
- [x] 更新 OpenAPI snapshot，确保七种 `JobKind`、八种 `JobStatus`、三种 `BackupKind`、三种 schedule kind、两种并发策略和稳定错误码均为封闭 enum/schema。
- [ ] 运行 GREEN：`dotnet test backend/7DPanel.sln --filter "FullyQualifiedName~JobsBackupsSchedulesApiTests|FullyQualifiedName~JobsBackupsSchedulesAuthorizationTests|FullyQualifiedName~OwinWebHostOpenApiSnapshotTests"`；预期路由、权限、错误映射和 snapshot 全部通过。
- [x] 运行生成：`pnpm --dir frontend/apps/admin api:gen`；`frontend/apps/admin/src/shared/api/generated/` 已由当前 snapshot 重新生成，生成文件不手工编辑。尚未取得“再次运行无额外 diff”的独立检查结果，不把它外推为 `api:check` 或 Git 基线门禁。

## 任务 9：交付 Admin“备份与恢复”和“公告与调度”页面

**文件：**

- Create: `frontend/apps/admin/src/features/backups/ui/BackupsView.vue`
- Create: `frontend/apps/admin/src/features/backups/ui/CreateBackupCard.vue`
- Create: `frontend/apps/admin/src/features/backups/ui/BackupCatalogTable.vue`
- Create: `frontend/apps/admin/src/features/backups/ui/RestoreConfirmModal.vue`
- Create: `frontend/apps/admin/src/features/backups/ui/JobProgressPanel.vue`
- Create: `frontend/apps/admin/src/features/backups/model/useBackups.ts`
- Create: `frontend/apps/admin/src/features/schedules/ui/SchedulesView.vue`
- Create: `frontend/apps/admin/src/features/schedules/ui/AnnouncementForm.vue`
- Create: `frontend/apps/admin/src/features/schedules/ui/ScheduleForm.vue`
- Create: `frontend/apps/admin/src/features/schedules/ui/ScheduleTable.vue`
- Create: `frontend/apps/admin/src/features/schedules/model/useSchedules.ts`
- Create: `frontend/apps/admin/src/pages/backups.vue`
- Create: `frontend/apps/admin/src/pages/schedules.vue`
- Modify: `frontend/apps/admin/src/app/router.ts`
- Modify: `frontend/apps/admin/src/app/AppShell.vue`
- Modify: `frontend/apps/admin/src/app/i18n/locales/zh-CN.json`
- Modify: `frontend/apps/admin/src/app/i18n/locales/en.json`
- Test: `frontend/apps/admin/src/features/backups/ui/BackupsView.test.ts`
- Test: `frontend/apps/admin/src/features/schedules/ui/SchedulesView.test.ts`

- [x] 先写备份页测试：三种创建按钮提交准确 endpoint；请求中禁用重复提交；收到 `202` 后按服务端 job id 轮询；终态展示时间、进度和稳定错误；下载使用生成客户端；删除和恢复都需明确确认。
- [x] 先写恢复危险态测试：modal 展示 kind、创建时间、checksum 与“下一次启动前应用”；世界恢复在 `world_restore_timing_unverified` 时展示不可绕过的失败；Panel 数据库恢复说明历史可能由 receipt 归并，页面不做乐观成功。
- [x] 先写自动化页测试：即时公告 `1..500` 计数与纯文本提交；计划表单按 `ScheduledConsoleCommand`、`ScheduledRestart`、`ScheduledAnnouncement` 呈现互斥字段；Cron、时区、并发策略、启停和下一触发时间均来自类型化合同。
- [x] 先写并发和卸载测试：composable 使用 single-flight 与 `AbortController`；旧响应不得覆盖新筛选；组件卸载停止轮询；mutation 成功后只失效相关 query；所有 runtime parser 对未知 enum 立即失败并显示协议错误。
- [ ] 运行 RED：`pnpm --dir frontend/apps/admin exec vitest run src/features/backups/ui/BackupsView.test.ts src/features/schedules/ui/SchedulesView.test.ts`；预期页面、组件与 composable 尚不存在而失败。
- [x] 以 Vue 3 Composition API 和 `<script setup lang="ts">` 实现两个页面；保持 props-down/events-up，页面负责组合，composable 负责请求状态，小组件不直接访问 router 或生成客户端。
- [x] 添加 `/backups` 与 `/schedules` 路由、导航和中英双语文案；复用现有鉴权 guard、通知和空/加载/错误状态，不新增第二套全局 store。
- [ ] 运行 GREEN：`pnpm --dir frontend/apps/admin exec vitest run src/features/backups/ui/BackupsView.test.ts src/features/schedules/ui/SchedulesView.test.ts`；预期创建、轮询、确认、校验、并发取消和协议失败用例全部通过。

## 任务 10：执行一次合流门禁、规格要求的真实演练与 Current 文档提升

**文件：**

- Modify: `docs/architecture.md`
- Modify: `docs/design.md`
- Modify: `docs/test.md`
- Modify only if aggregate commands changed: `README.md`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/Live/WorldBackupRestoreLiveTests.cs`

- [x] 先逐节核对 primary spec：范围/非目标映射任务 1；持久状态与类型化 payload 映射任务 1、2；三类备份映射任务 3、4；跨重启恢复与回滚映射任务 5、7；公告/Cron/错过触发/并发映射任务 6；命令与重启映射任务 6；API/权限/错误映射任务 8；Admin 状态与交互映射任务 9；兼容性、门禁与文档映射本任务。
- [ ] 运行唯一一次后端受影响聚合门禁：`dotnet test backend/7DPanel.sln --configuration Release`；预期 solution 构建成功，全部非 Live 测试通过，退出码为 `0`，没有 migration、OpenAPI snapshot、生命周期或组合根失败。
- [ ] 运行唯一一次 Admin 聚合门禁，依次执行 `pnpm --dir frontend/apps/admin typecheck`、`pnpm --dir frontend/apps/admin test`、`pnpm --dir frontend/apps/admin build`；预期类型检查、全部 Vitest 与生产构建通过，退出码均为 `0`。
- [ ] 真实恢复演练会覆盖受控测试世界，属于本路线允许暂停的破坏性真实环境操作；执行前向用户确认精确实例、备份和回滚目标。确认后在隔离的 `7DTD v3.0.1-b4` 环境执行一次：`dotnet test backend/7DPanel.sln --configuration Release --filter "TestCategory=LiveSevenDays&FullyQualifiedName~WorldBackupRestoreLiveTests"`。测试固定先产生可识别世界状态，再触发一次保存/世界备份，随后 stage 一次恢复并重启，验证 applier 在世界打开前完成、恢复后状态匹配、job receipt 归并且 OWIN 最后启动。
- [ ] 若真实演练不能证明“世界打开前恢复”，保留 `WorldRestoreTimingGate` 为拒绝并确认 API 返回 `world_restore_timing_unverified`；不得通过延迟、在线覆盖或文档声明绕过。若证明通过，只把该版本和证据结果写入 Current 架构与测试文档。
- [ ] 使用 `managing-project-lifecycle` 做文档提升：在 `docs/architecture.md` 记录已经验证的项目边界、依赖、SQLite schema、启动/停止顺序、备份根与恢复 marker；在 `docs/design.md` 记录两个页面及 loading/empty/error/danger/terminal 状态；在 `docs/test.md` 记录聚焦测试、两次聚合门禁和唯一 Live 门禁。所有 `docs/` 内容使用简体中文。
- [ ] 不改动三份 Target 蓝图，除非实施揭示目标本身必须重新审批；不因尚未发布而写 `CHANGELOG.md`；只有聚合命令确实变化时才同步 `README.md`。
- [ ] 完成最终一致性检查：七个 `JobKind`、八个 `JobStatus`、三个 `BackupKind`、两种并发策略在 Domain、SQLite CHECK、OpenAPI、生成客户端、runtime parser 和页面中完全一致；migration 只有 `009`；不存在万能 payload、任意路径、动态脚本或未接通抽象。

## 结束与 Git 边界

- 本计划实施过程中不自动创建 commit，也不自动 push、reset、revert、建分支或创建 PR。
- `git commit`、`git push` 及任何其他 Git 写操作必须由用户另行显式授权；测试通过和文档提升不构成该授权。
- 完成报告只列出实际变更文件、聚焦测试结果、后端/Admin 各一次聚合门禁结果、一次 Live 演练结果，以及仍由稳定错误码关闭的能力。
