CREATE TABLE server_operation_lifecycle (
    operation_id TEXT PRIMARY KEY CHECK (length(trim(operation_id)) > 0),
    operation_kind TEXT NOT NULL CHECK (operation_kind IN ('restart_script', 'shutdown')),
    actor_subject TEXT NOT NULL CHECK (length(trim(actor_subject)) > 0),
    origin_process_instance_id TEXT NOT NULL CHECK (length(trim(origin_process_instance_id)) > 0),
    status TEXT NOT NULL CHECK (status IN (
        'queued', 'running', 'succeeded', 'failed', 'cancelled', 'result-unknown')),
    requested_at_utc INTEGER NOT NULL CHECK (requested_at_utc >= 0),
    started_at_utc INTEGER NULL CHECK (started_at_utc IS NULL OR started_at_utc >= requested_at_utc),
    completed_at_utc INTEGER NULL CHECK (completed_at_utc IS NULL OR completed_at_utc >= requested_at_utc),
    completion_deadline_utc INTEGER NOT NULL CHECK (completion_deadline_utc > requested_at_utc),
    failure_code TEXT NULL CHECK (failure_code IS NULL OR length(trim(failure_code)) > 0),
    audit_status TEXT NOT NULL CHECK (audit_status IN ('recorded', 'audit_degraded')),
    row_version INTEGER NOT NULL DEFAULT 1 CHECK (row_version > 0),
    CHECK ((status = 'queued' AND started_at_utc IS NULL AND completed_at_utc IS NULL AND failure_code IS NULL) OR
           (status = 'running' AND started_at_utc IS NOT NULL AND completed_at_utc IS NULL AND failure_code IS NULL) OR
           (status = 'succeeded' AND started_at_utc IS NOT NULL AND completed_at_utc IS NOT NULL AND failure_code IS NULL) OR
           (status IN ('failed', 'cancelled', 'result-unknown') AND started_at_utc IS NOT NULL AND completed_at_utc IS NOT NULL AND failure_code IS NOT NULL))
);

CREATE INDEX ix_server_operation_lifecycle_running_deadline
    ON server_operation_lifecycle(status, completion_deadline_utc, operation_id);
