CREATE TABLE game_events (
    event_id TEXT NOT NULL PRIMARY KEY,
    event_type TEXT NOT NULL CHECK (event_type IN (
        'PlayerJoined', 'PlayerLeft', 'PlayerKilledEntity', 'PlayerDied')),
    occurred_utc INTEGER NOT NULL,
    observed_utc INTEGER NOT NULL,
    actor_crossplatform_id TEXT NULL,
    actor_platform_id TEXT NULL,
    actor_entity_id INTEGER NULL,
    actor_name TEXT NULL,
    target_crossplatform_id TEXT NULL,
    target_platform_id TEXT NULL,
    target_entity_id INTEGER NULL,
    target_name TEXT NULL,
    game_shutting_down INTEGER NULL CHECK (
        game_shutting_down IS NULL OR game_shutting_down IN (0, 1))
);

CREATE INDEX ix_game_events_occurred
    ON game_events (occurred_utc DESC, event_id DESC);
CREATE INDEX ix_game_events_actor_crossplatform
    ON game_events (actor_crossplatform_id, occurred_utc DESC, event_id DESC);
CREATE INDEX ix_game_events_target_crossplatform
    ON game_events (target_crossplatform_id, occurred_utc DESC, event_id DESC);
CREATE INDEX ix_game_events_type
    ON game_events (event_type, occurred_utc DESC, event_id DESC);

CREATE TABLE game_event_gaps (
    gap_id TEXT NOT NULL PRIMARY KEY,
    reason TEXT NOT NULL CHECK (reason IN ('QueueFull', 'StoreFailure', 'DrainTimeout')),
    started_utc INTEGER NOT NULL,
    ended_utc INTEGER NULL CHECK (ended_utc IS NULL OR ended_utc >= started_utc),
    affected_count INTEGER NOT NULL CHECK (affected_count > 0)
);

CREATE INDEX ix_game_event_gaps_started
    ON game_event_gaps (started_utc DESC, gap_id DESC);

CREATE TABLE chat_mute (
    crossplatform_id TEXT NOT NULL PRIMARY KEY,
    display_name TEXT NULL,
    reason TEXT NOT NULL,
    muted_until_utc INTEGER NULL,
    created_by TEXT NOT NULL,
    created_utc INTEGER NOT NULL,
    updated_by TEXT NOT NULL,
    updated_utc INTEGER NOT NULL CHECK (updated_utc >= created_utc)
);

CREATE INDEX ix_chat_mute_updated
    ON chat_mute (updated_utc DESC, crossplatform_id DESC);
CREATE INDEX ix_chat_mute_muted_until
    ON chat_mute (muted_until_utc);

CREATE TABLE chat_mute_operation (
    operation_id TEXT NOT NULL PRIMARY KEY,
    operation_kind TEXT NOT NULL CHECK (operation_kind IN ('Create', 'Update', 'Release', 'Expire')),
    target_crossplatform_id TEXT NOT NULL,
    actor_subject TEXT NULL,
    occurred_utc INTEGER NOT NULL,
    result TEXT NOT NULL,
    correlation_id TEXT NULL,
    muted_until_utc INTEGER NULL,
    reason TEXT NULL
);

CREATE INDEX ix_chat_mute_operation_occurred
    ON chat_mute_operation (occurred_utc DESC, operation_id DESC);

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
FROM chat_mute_operation;
