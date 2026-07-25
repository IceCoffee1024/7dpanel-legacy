CREATE TABLE recent_activity (
    activity_id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    event_type TEXT NOT NULL CHECK (event_type IN (
        'panel_login_succeeded',
        'player_joined',
        'player_left',
        'restart_script_started',
        'shutdown_requested',
        'server_operation_failed')),
    message_key TEXT NOT NULL CHECK (message_key IN (
        'panel_login_succeeded',
        'player_joined',
        'player_left',
        'restart_script_started',
        'shutdown_requested',
        'server_operation_failed')),
    message_args TEXT NOT NULL CHECK (json_valid(message_args)),
    actor_subject TEXT NULL CHECK (actor_subject IS NULL OR length(actor_subject) BETWEEN 1 AND 128),
    actor_display_name TEXT NULL CHECK (actor_display_name IS NULL OR length(actor_display_name) BETWEEN 1 AND 128),
    occurred_utc TEXT NOT NULL,
    CONSTRAINT ck_recent_activity_event_message
        CHECK (event_type = message_key)
);

CREATE INDEX ix_recent_activity_occurred_utc
    ON recent_activity (occurred_utc DESC, activity_id DESC);

CREATE TABLE server_operation_audit (
    operation_id TEXT NOT NULL PRIMARY KEY CHECK (length(operation_id) BETWEEN 1 AND 128),
    operation_type TEXT NOT NULL CHECK (operation_type IN ('restart', 'shutdown')),
    actor_subject TEXT NOT NULL CHECK (length(actor_subject) BETWEEN 1 AND 128),
    status TEXT NOT NULL CHECK (status IN ('Pending', 'Started', 'Failed')),
    requested_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL CHECK (updated_utc >= requested_utc),
    failure_code TEXT NULL CHECK (failure_code IS NULL OR length(failure_code) BETWEEN 1 AND 64),
    CONSTRAINT ck_server_operation_audit_failure
        CHECK ((status = 'Failed' AND failure_code IS NOT NULL) OR
               (status <> 'Failed' AND failure_code IS NULL))
);

CREATE INDEX ix_server_operation_audit_requested_utc
    ON server_operation_audit (requested_utc DESC, operation_id DESC);
