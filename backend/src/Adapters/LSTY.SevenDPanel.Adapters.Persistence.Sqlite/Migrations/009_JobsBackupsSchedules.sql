CREATE TABLE jobs (
    id TEXT NOT NULL PRIMARY KEY,
    kind TEXT NOT NULL CHECK (kind IN (
        'WorldBackup', 'PanelDatabaseBackup', 'ServerConfigurationBackup', 'WorldOperation',
        'Restore', 'ScheduledConsoleCommand', 'ScheduledRestart', 'ScheduledAnnouncement')),
    status TEXT NOT NULL CHECK (status IN (
        'Queued', 'Running', 'PendingRestart', 'Succeeded', 'Failed',
        'Cancelled', 'Interrupted', 'ResultUnknown')),
    actor_subject TEXT NULL,
    source_schedule_id TEXT NULL,
    idempotency_key TEXT NOT NULL UNIQUE CHECK (length(trim(idempotency_key)) > 0),
    correlation_id TEXT NULL,
    created_at_utc INTEGER NOT NULL,
    started_at_utc INTEGER NULL CHECK (started_at_utc IS NULL OR started_at_utc >= created_at_utc),
    completed_at_utc INTEGER NULL CHECK (completed_at_utc IS NULL OR completed_at_utc >= created_at_utc),
    progress_current INTEGER NULL CHECK (progress_current IS NULL OR progress_current >= 0),
    progress_total INTEGER NULL CHECK (progress_total IS NULL OR progress_total >= 0),
    error_code TEXT NULL,
    worker_id TEXT NULL,
    row_version INTEGER NOT NULL DEFAULT 0 CHECK (row_version >= 0),
    CHECK (progress_current IS NULL OR progress_total IS NULL OR progress_current <= progress_total)
);

CREATE INDEX ix_jobs_status_created
    ON jobs (status, created_at_utc DESC, id DESC);
CREATE INDEX ix_jobs_kind_status_created
    ON jobs (kind, status, created_at_utc DESC, id DESC);
CREATE INDEX ix_jobs_source_schedule_status
    ON jobs (source_schedule_id, status, created_at_utc DESC, id DESC);

CREATE TABLE world_backup_job_payloads (
    job_id TEXT NOT NULL PRIMARY KEY REFERENCES jobs(id) ON DELETE CASCADE,
    world_name TEXT NOT NULL CHECK (length(trim(world_name)) BETWEEN 1 AND 200)
);

CREATE TABLE panel_database_backup_job_payloads (
    job_id TEXT NOT NULL PRIMARY KEY REFERENCES jobs(id) ON DELETE CASCADE
);

CREATE TABLE server_configuration_backup_job_payloads (
    job_id TEXT NOT NULL PRIMARY KEY REFERENCES jobs(id) ON DELETE CASCADE
);

CREATE TABLE restore_job_payloads (
    job_id TEXT NOT NULL PRIMARY KEY REFERENCES jobs(id) ON DELETE CASCADE,
    backup_id TEXT NOT NULL,
    backup_kind TEXT NOT NULL CHECK (backup_kind IN ('World', 'PanelDatabase', 'ServerConfiguration')),
    restart_after_stage INTEGER NOT NULL CHECK (restart_after_stage IN (0, 1))
);

CREATE TABLE scheduled_console_command_job_payloads (
    job_id TEXT NOT NULL PRIMARY KEY REFERENCES jobs(id) ON DELETE CASCADE,
    schedule_id TEXT NOT NULL,
    command_text TEXT NOT NULL CHECK (length(trim(command_text)) > 0)
);

CREATE TABLE scheduled_restart_job_payloads (
    job_id TEXT NOT NULL PRIMARY KEY REFERENCES jobs(id) ON DELETE CASCADE,
    schedule_id TEXT NOT NULL,
    countdown_seconds INTEGER NOT NULL CHECK (countdown_seconds BETWEEN 0 AND 86400)
);

CREATE TABLE scheduled_announcement_job_payloads (
    job_id TEXT NOT NULL PRIMARY KEY REFERENCES jobs(id) ON DELETE CASCADE,
    schedule_id TEXT NOT NULL,
    message_text TEXT NOT NULL CHECK (length(message_text) BETWEEN 1 AND 500)
);

CREATE TABLE backup_artifacts (
    id TEXT NOT NULL PRIMARY KEY,
    kind TEXT NOT NULL CHECK (kind IN ('World', 'PanelDatabase', 'ServerConfiguration')),
    backup_root_id TEXT NOT NULL CHECK (
        length(trim(backup_root_id)) > 0 AND
        instr(backup_root_id, '/') = 0 AND instr(backup_root_id, char(92)) = 0 AND
        instr(backup_root_id, '..') = 0),
    relative_resource_id TEXT NOT NULL CHECK (
        length(trim(relative_resource_id)) > 0 AND
        instr(relative_resource_id, '/') = 0 AND instr(relative_resource_id, char(92)) = 0 AND
        instr(relative_resource_id, '..') = 0),
    size_bytes INTEGER NOT NULL CHECK (size_bytes >= 0),
    sha256 TEXT NOT NULL CHECK (length(sha256) = 64),
    world_id TEXT NULL,
    game_version TEXT NULL,
    validation_status TEXT NOT NULL CHECK (length(trim(validation_status)) > 0),
    created_at_utc INTEGER NOT NULL,
    source_job_id TEXT NOT NULL UNIQUE REFERENCES jobs(id) ON DELETE RESTRICT,
    manifest_version INTEGER NOT NULL CHECK (manifest_version > 0),
    UNIQUE (backup_root_id, relative_resource_id)
);

CREATE INDEX ix_backup_artifacts_kind_created
    ON backup_artifacts (kind, created_at_utc DESC, id DESC);

CREATE TABLE backup_policies (
    kind TEXT NOT NULL PRIMARY KEY CHECK (kind IN ('World', 'PanelDatabase', 'ServerConfiguration')),
    enabled INTEGER NOT NULL CHECK (enabled IN (0, 1)),
    cron_expression TEXT NOT NULL CHECK (length(trim(cron_expression)) > 0),
    time_zone_id TEXT NOT NULL CHECK (length(trim(time_zone_id)) > 0),
    backup_root_id TEXT NOT NULL CHECK (
        length(trim(backup_root_id)) > 0 AND
        instr(backup_root_id, '/') = 0 AND instr(backup_root_id, char(92)) = 0 AND
        instr(backup_root_id, '..') = 0),
    retention_count INTEGER NOT NULL CHECK (retention_count >= 0),
    retention_days INTEGER NOT NULL CHECK (retention_days >= 0),
    compression_enabled INTEGER NOT NULL CHECK (compression_enabled IN (0, 1)),
    row_version INTEGER NOT NULL DEFAULT 0 CHECK (row_version >= 0)
);

CREATE TABLE schedules (
    id TEXT NOT NULL PRIMARY KEY,
    kind TEXT NOT NULL CHECK (kind IN (
        'ScheduledConsoleCommand', 'ScheduledRestart', 'ScheduledAnnouncement')),
    name TEXT NOT NULL CHECK (length(trim(name)) BETWEEN 1 AND 200),
    cron_expression TEXT NOT NULL CHECK (length(trim(cron_expression)) > 0),
    time_zone_id TEXT NOT NULL CHECK (length(trim(time_zone_id)) > 0),
    enabled INTEGER NOT NULL CHECK (enabled IN (0, 1)),
    concurrency_policy TEXT NOT NULL CHECK (concurrency_policy IN ('SkipIfRunning', 'QueueOne')),
    command_text TEXT NULL,
    countdown_seconds INTEGER NULL,
    message_text TEXT NULL,
    next_occurrence_utc INTEGER NULL,
    last_occurrence_utc INTEGER NULL,
    row_version INTEGER NOT NULL DEFAULT 0 CHECK (row_version >= 0),
    CHECK (last_occurrence_utc IS NULL OR next_occurrence_utc IS NULL OR next_occurrence_utc > last_occurrence_utc),
    CHECK (
        (kind = 'ScheduledConsoleCommand' AND command_text IS NOT NULL
            AND length(trim(command_text)) > 0
            AND instr(command_text, char(10)) = 0 AND instr(command_text, char(13)) = 0
            AND countdown_seconds IS NULL AND message_text IS NULL)
        OR
        (kind = 'ScheduledRestart' AND command_text IS NULL
            AND countdown_seconds IS NOT NULL AND countdown_seconds BETWEEN 0 AND 86400
            AND message_text IS NULL)
        OR
        (kind = 'ScheduledAnnouncement' AND command_text IS NULL
            AND countdown_seconds IS NULL AND message_text IS NOT NULL
            AND length(message_text) BETWEEN 1 AND 500)
    )
);

CREATE INDEX ix_schedules_enabled_next
    ON schedules (enabled, next_occurrence_utc, id);

CREATE TABLE schedule_runs (
    id TEXT NOT NULL PRIMARY KEY,
    schedule_id TEXT NOT NULL,
    scheduled_for_utc INTEGER NOT NULL,
    job_id TEXT NULL REFERENCES jobs(id) ON DELETE SET NULL,
    outcome TEXT NOT NULL CHECK (length(trim(outcome)) > 0),
    created_at_utc INTEGER NOT NULL,
    UNIQUE (schedule_id, scheduled_for_utc)
);

CREATE INDEX ix_schedule_runs_schedule_created
    ON schedule_runs (schedule_id, created_at_utc DESC, id DESC);

CREATE TABLE job_admin_operations (
    id TEXT NOT NULL PRIMARY KEY,
    actor_subject TEXT NULL,
    action TEXT NOT NULL CHECK (length(trim(action)) > 0),
    target_kind TEXT NOT NULL CHECK (length(trim(target_kind)) > 0),
    target_id TEXT NOT NULL CHECK (length(trim(target_id)) > 0),
    status TEXT NOT NULL CHECK (length(trim(status)) > 0),
    occurred_utc INTEGER NOT NULL,
    correlation_id TEXT NULL
);

CREATE INDEX ix_job_admin_operations_occurred
    ON job_admin_operations (occurred_utc DESC, id DESC);

CREATE TRIGGER validate_world_backup_payload_kind
BEFORE INSERT ON world_backup_job_payloads
WHEN COALESCE((SELECT kind FROM jobs WHERE id = NEW.job_id), '') <> 'WorldBackup'
BEGIN
    SELECT RAISE(ABORT, 'job_kind_mismatch');
END;

CREATE TRIGGER validate_panel_database_backup_payload_kind
BEFORE INSERT ON panel_database_backup_job_payloads
WHEN COALESCE((SELECT kind FROM jobs WHERE id = NEW.job_id), '') <> 'PanelDatabaseBackup'
BEGIN
    SELECT RAISE(ABORT, 'job_kind_mismatch');
END;

CREATE TRIGGER validate_server_configuration_backup_payload_kind
BEFORE INSERT ON server_configuration_backup_job_payloads
WHEN COALESCE((SELECT kind FROM jobs WHERE id = NEW.job_id), '') <> 'ServerConfigurationBackup'
BEGIN
    SELECT RAISE(ABORT, 'job_kind_mismatch');
END;

CREATE TRIGGER validate_restore_payload_kind
BEFORE INSERT ON restore_job_payloads
WHEN COALESCE((SELECT kind FROM jobs WHERE id = NEW.job_id), '') <> 'Restore'
BEGIN
    SELECT RAISE(ABORT, 'job_kind_mismatch');
END;

CREATE TRIGGER validate_scheduled_job_schedule
BEFORE INSERT ON jobs
WHEN NEW.kind IN (
        'ScheduledConsoleCommand', 'ScheduledRestart', 'ScheduledAnnouncement')
  AND COALESCE(
        (SELECT kind FROM schedules WHERE id = NEW.source_schedule_id), '') <> NEW.kind
BEGIN
    SELECT RAISE(ABORT, 'schedule_missing_or_kind_mismatch');
END;

CREATE TRIGGER validate_scheduled_console_command_payload_kind
BEFORE INSERT ON scheduled_console_command_job_payloads
WHEN COALESCE((SELECT kind FROM jobs WHERE id = NEW.job_id), '') <> 'ScheduledConsoleCommand'
  OR COALESCE((SELECT source_schedule_id FROM jobs WHERE id = NEW.job_id), '') <> NEW.schedule_id
  OR COALESCE((SELECT kind FROM schedules WHERE id = NEW.schedule_id), '') <> 'ScheduledConsoleCommand'
BEGIN
    SELECT RAISE(ABORT, 'job_kind_or_schedule_mismatch');
END;

CREATE TRIGGER validate_scheduled_restart_payload_kind
BEFORE INSERT ON scheduled_restart_job_payloads
WHEN COALESCE((SELECT kind FROM jobs WHERE id = NEW.job_id), '') <> 'ScheduledRestart'
  OR COALESCE((SELECT source_schedule_id FROM jobs WHERE id = NEW.job_id), '') <> NEW.schedule_id
  OR COALESCE((SELECT kind FROM schedules WHERE id = NEW.schedule_id), '') <> 'ScheduledRestart'
BEGIN
    SELECT RAISE(ABORT, 'job_kind_or_schedule_mismatch');
END;

CREATE TRIGGER validate_scheduled_announcement_payload_kind
BEFORE INSERT ON scheduled_announcement_job_payloads
WHEN COALESCE((SELECT kind FROM jobs WHERE id = NEW.job_id), '') <> 'ScheduledAnnouncement'
  OR COALESCE((SELECT source_schedule_id FROM jobs WHERE id = NEW.job_id), '') <> NEW.schedule_id
  OR COALESCE((SELECT kind FROM schedules WHERE id = NEW.schedule_id), '') <> 'ScheduledAnnouncement'
BEGIN
    SELECT RAISE(ABORT, 'job_kind_or_schedule_mismatch');
END;

CREATE TRIGGER validate_schedule_run_schedule
BEFORE INSERT ON schedule_runs
WHEN NOT EXISTS (
        SELECT 1 FROM schedules WHERE id = NEW.schedule_id)
  OR (NEW.job_id IS NOT NULL AND NOT EXISTS (
        SELECT 1
        FROM jobs
        INNER JOIN schedules ON schedules.id = NEW.schedule_id
        WHERE jobs.id = NEW.job_id
          AND jobs.source_schedule_id = NEW.schedule_id
          AND jobs.kind = schedules.kind))
BEGIN
    SELECT RAISE(ABORT, 'schedule_run_schedule_or_job_mismatch');
END;

DROP VIEW IF EXISTS unified_audit_projection;

CREATE VIEW unified_audit_projection AS
SELECT
    'playerAction' AS source_kind,
    operation_id AS source_id,
    actor_subject,
    target_platform_id AS target_ref,
    action_type AS action,
    requested_utc AS occurred_utc,
    status,
    NULL AS correlation_id,
    0 AS has_details
FROM player_action_audit
UNION ALL
SELECT
    'consoleCommand',
    audit_id,
    actor_subject,
    NULL,
    COALESCE(command_name, 'command'),
    started_utc,
    completion_kind,
    NULL,
    0
FROM console_command_audit
UNION ALL
SELECT
    'serverOperation',
    operation_id,
    actor_subject,
    NULL,
    operation_type,
    CAST(strftime('%s', requested_utc) AS INTEGER) * 1000
        + CAST(substr(strftime('%f', requested_utc), 4, 3) AS INTEGER),
    status,
    NULL,
    0
FROM server_operation_audit
UNION ALL
SELECT
    'chatOperation',
    CAST(id AS TEXT),
    actor_subject,
    target_crossplatform_id,
    operation,
    occurred_utc,
    result,
    business_key,
    0
FROM chat_operation_audit
UNION ALL
SELECT
    'chatMuteOperation',
    operation_id,
    actor_subject,
    target_crossplatform_id,
    operation_kind,
    occurred_utc,
    result,
    correlation_id,
    0
FROM chat_mute_operation
UNION ALL
SELECT
    'serverOperation',
    'job:' || id,
    actor_subject,
    source_schedule_id,
    'job.' || kind,
    created_at_utc,
    status,
    correlation_id,
    0
FROM jobs
UNION ALL
SELECT
    'serverOperation',
    'scheduleRun:' || id,
    NULL,
    schedule_id,
    'schedule.run',
    created_at_utc,
    outcome,
    NULL,
    0
FROM schedule_runs
UNION ALL
SELECT
    'serverOperation',
    'jobAdminOperation:' || id,
    actor_subject,
    target_kind || ':' || target_id,
    action,
    occurred_utc,
    status,
    correlation_id,
    0
FROM job_admin_operations;
