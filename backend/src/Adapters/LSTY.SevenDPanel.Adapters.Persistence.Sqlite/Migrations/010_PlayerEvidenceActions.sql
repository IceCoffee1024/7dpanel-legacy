CREATE TABLE player_sessions (
    id INTEGER NOT NULL PRIMARY KEY CHECK (id > 0),
    crossplatform_id TEXT NOT NULL CHECK (length(trim(crossplatform_id)) > 0),
    server_id TEXT NOT NULL CHECK (length(trim(server_id)) > 0),
    world_id TEXT NOT NULL CHECK (length(trim(world_id)) > 0),
    started_at_utc INTEGER NOT NULL,
    ended_at_utc INTEGER NULL CHECK (ended_at_utc IS NULL OR ended_at_utc >= started_at_utc),
    end_reason TEXT NULL,
    last_x REAL NULL,
    last_y REAL NULL,
    last_z REAL NULL,
    completeness TEXT NOT NULL CHECK (completeness IN ('Available', 'Partial', 'Unavailable', 'Forbidden')),
    CHECK ((last_x IS NULL AND last_y IS NULL AND last_z IS NULL)
        OR (last_x IS NOT NULL AND last_y IS NOT NULL AND last_z IS NOT NULL))
);

CREATE INDEX ix_player_sessions_player_started
    ON player_sessions (crossplatform_id, started_at_utc DESC, id DESC);

CREATE TABLE player_activity_events (
    id INTEGER NOT NULL PRIMARY KEY CHECK (id > 0),
    crossplatform_id TEXT NOT NULL CHECK (length(trim(crossplatform_id)) > 0),
    server_id TEXT NOT NULL CHECK (length(trim(server_id)) > 0),
    world_id TEXT NOT NULL CHECK (length(trim(world_id)) > 0),
    kind TEXT NOT NULL CHECK (length(trim(kind)) > 0),
    observed_at_utc INTEGER NOT NULL,
    correlation_id TEXT NULL,
    completeness TEXT NOT NULL CHECK (completeness IN ('Available', 'Partial', 'Unavailable', 'Forbidden'))
);

CREATE INDEX ix_player_activity_events_player_observed
    ON player_activity_events (crossplatform_id, observed_at_utc DESC, id DESC);

CREATE TABLE player_inventory_snapshots (
    id INTEGER NOT NULL PRIMARY KEY CHECK (id > 0),
    crossplatform_id TEXT NOT NULL CHECK (length(trim(crossplatform_id)) > 0),
    server_id TEXT NOT NULL CHECK (length(trim(server_id)) > 0),
    world_id TEXT NOT NULL CHECK (length(trim(world_id)) > 0),
    observed_at_utc INTEGER NOT NULL,
    game_version TEXT NOT NULL CHECK (length(trim(game_version)) > 0),
    catalog_version TEXT NULL,
    catalog_resolution TEXT NOT NULL CHECK (catalog_resolution IN ('Resolved', 'Unavailable')),
    fingerprint TEXT NOT NULL CHECK (length(trim(fingerprint)) > 0),
    admin_boundary INTEGER NOT NULL CHECK (admin_boundary IN (0, 1)),
    CHECK (catalog_resolution <> 'Resolved' OR catalog_version IS NOT NULL)
);

CREATE INDEX ix_player_inventory_snapshots_player_observed
    ON player_inventory_snapshots (crossplatform_id, observed_at_utc DESC, id DESC);

CREATE TABLE player_inventory_items (
    snapshot_id INTEGER NOT NULL REFERENCES player_inventory_snapshots(id) ON DELETE CASCADE,
    container_kind TEXT NOT NULL CHECK (length(trim(container_kind)) > 0),
    slot_index INTEGER NOT NULL CHECK (slot_index >= 0),
    internal_name TEXT NOT NULL CHECK (length(trim(internal_name)) > 0),
    item_kind TEXT NOT NULL CHECK (length(trim(item_kind)) > 0),
    count INTEGER NOT NULL CHECK (count > 0),
    quality INTEGER NULL CHECK (quality IS NULL OR quality >= 0),
    use_amount TEXT NULL,
    PRIMARY KEY (snapshot_id, container_kind, slot_index)
);

CREATE TABLE player_inventory_item_mods (
    snapshot_id INTEGER NOT NULL,
    container_kind TEXT NOT NULL,
    slot_index INTEGER NOT NULL,
    ordinal INTEGER NOT NULL CHECK (ordinal >= 0),
    internal_name TEXT NOT NULL CHECK (length(trim(internal_name)) > 0),
    PRIMARY KEY (snapshot_id, container_kind, slot_index, ordinal),
    FOREIGN KEY (snapshot_id, container_kind, slot_index)
        REFERENCES player_inventory_items(snapshot_id, container_kind, slot_index)
        ON DELETE CASCADE
);

CREATE TABLE player_skill_snapshots (
    id INTEGER NOT NULL PRIMARY KEY CHECK (id > 0),
    crossplatform_id TEXT NOT NULL CHECK (length(trim(crossplatform_id)) > 0),
    server_id TEXT NOT NULL CHECK (length(trim(server_id)) > 0),
    world_id TEXT NOT NULL CHECK (length(trim(world_id)) > 0),
    observed_at_utc INTEGER NOT NULL,
    game_version TEXT NOT NULL CHECK (length(trim(game_version)) > 0),
    level INTEGER NULL CHECK (level IS NULL OR level >= 0),
    skill_points INTEGER NULL CHECK (skill_points IS NULL OR skill_points >= 0)
);

CREATE INDEX ix_player_skill_snapshots_player_observed
    ON player_skill_snapshots (crossplatform_id, observed_at_utc DESC, id DESC);

CREATE TABLE player_skill_values (
    snapshot_id INTEGER NOT NULL REFERENCES player_skill_snapshots(id) ON DELETE CASCADE,
    skill_key TEXT NOT NULL CHECK (length(trim(skill_key)) > 0),
    state TEXT NOT NULL CHECK (state IN ('Known', 'UnsupportedByVersion', 'NotLoaded', 'Unknown')),
    value INTEGER NULL,
    minimum INTEGER NULL,
    maximum INTEGER NULL,
    next_level_cost INTEGER NULL,
    parent_key TEXT NULL,
    PRIMARY KEY (snapshot_id, skill_key),
    CHECK ((state = 'Known' AND value IS NOT NULL) OR (state <> 'Known' AND value IS NULL)),
    CHECK (minimum IS NULL OR maximum IS NULL OR maximum >= minimum)
);

CREATE TABLE inventory_gaps (
    id INTEGER NOT NULL PRIMARY KEY CHECK (id > 0),
    crossplatform_id TEXT NOT NULL CHECK (length(trim(crossplatform_id)) > 0),
    started_at_utc INTEGER NOT NULL,
    ended_at_utc INTEGER NOT NULL CHECK (ended_at_utc >= started_at_utc),
    reason TEXT NOT NULL CHECK (length(trim(reason)) > 0),
    estimated_lost_count INTEGER NOT NULL CHECK (estimated_lost_count > 0)
);

CREATE INDEX ix_inventory_gaps_player_started
    ON inventory_gaps (crossplatform_id, started_at_utc DESC, id DESC);

CREATE TABLE skill_gaps (
    id INTEGER NOT NULL PRIMARY KEY CHECK (id > 0),
    crossplatform_id TEXT NOT NULL CHECK (length(trim(crossplatform_id)) > 0),
    started_at_utc INTEGER NOT NULL,
    ended_at_utc INTEGER NOT NULL CHECK (ended_at_utc >= started_at_utc),
    reason TEXT NOT NULL CHECK (length(trim(reason)) > 0),
    estimated_lost_count INTEGER NOT NULL CHECK (estimated_lost_count > 0)
);

CREATE INDEX ix_skill_gaps_player_started
    ON skill_gaps (crossplatform_id, started_at_utc DESC, id DESC);

CREATE TABLE player_grant_item_operations (
    operation_id TEXT NOT NULL PRIMARY KEY CHECK (length(trim(operation_id)) > 0),
    operator_id TEXT NOT NULL CHECK (length(trim(operator_id)) > 0),
    target_crossplatform_id TEXT NOT NULL CHECK (length(trim(target_crossplatform_id)) > 0),
    target_entity_id INTEGER NOT NULL CHECK (target_entity_id >= 0),
    target_online_observed_at_utc INTEGER NOT NULL,
    world_id TEXT NOT NULL CHECK (length(trim(world_id)) > 0),
    client_request_key TEXT NOT NULL CHECK (length(trim(client_request_key)) > 0),
    correlation_id TEXT NULL,
    status TEXT NOT NULL CHECK (status IN ('Pending', 'Succeeded', 'Rejected', 'Failed', 'Cancelled', 'ResultUnknown')),
    created_at_utc INTEGER NOT NULL,
    started_at_utc INTEGER NULL CHECK (started_at_utc IS NULL OR started_at_utc >= created_at_utc),
    completed_at_utc INTEGER NULL CHECK (completed_at_utc IS NULL OR completed_at_utc >= COALESCE(started_at_utc, created_at_utc)),
    failure_code TEXT NULL,
    before_inventory_snapshot_id INTEGER NULL REFERENCES player_inventory_snapshots(id) ON DELETE RESTRICT,
    after_inventory_snapshot_id INTEGER NULL REFERENCES player_inventory_snapshots(id) ON DELETE RESTRICT,
    before_skill_snapshot_id INTEGER NULL REFERENCES player_skill_snapshots(id) ON DELETE RESTRICT,
    after_skill_snapshot_id INTEGER NULL REFERENCES player_skill_snapshots(id) ON DELETE RESTRICT,
    catalog_version TEXT NOT NULL CHECK (length(trim(catalog_version)) > 0),
    resource_id TEXT NULL CHECK (resource_id IS NULL OR length(trim(resource_id)) > 0),
    internal_name TEXT NOT NULL CHECK (length(trim(internal_name)) > 0),
    item_kind TEXT NOT NULL CHECK (length(trim(item_kind)) > 0),
    game_version TEXT NULL CHECK (game_version IS NULL OR length(trim(game_version)) > 0),
    numeric_id INTEGER NULL CHECK (numeric_id IS NULL OR numeric_id >= 0),
    quantity INTEGER NOT NULL CHECK (quantity > 0),
    quality INTEGER NULL CHECK (quality IS NULL OR quality >= 0),
    hidden_item_confirmed INTEGER NOT NULL CHECK (hidden_item_confirmed IN (0, 1)),
    actual_quantity INTEGER NULL CHECK (actual_quantity IS NULL OR actual_quantity BETWEEN 0 AND quantity),
    CHECK ((status = 'Pending' AND completed_at_utc IS NULL)
        OR (status <> 'Pending' AND completed_at_utc IS NOT NULL))
);

CREATE UNIQUE INDEX ux_player_grant_item_operations_operator_request
    ON player_grant_item_operations (operator_id, client_request_key);
CREATE INDEX ix_player_grant_item_operations_target_created
    ON player_grant_item_operations (target_crossplatform_id, created_at_utc DESC, operation_id DESC);

CREATE TABLE player_remove_item_operations (
    operation_id TEXT NOT NULL PRIMARY KEY CHECK (length(trim(operation_id)) > 0),
    operator_id TEXT NOT NULL CHECK (length(trim(operator_id)) > 0),
    target_crossplatform_id TEXT NOT NULL CHECK (length(trim(target_crossplatform_id)) > 0),
    target_entity_id INTEGER NOT NULL CHECK (target_entity_id >= 0),
    target_online_observed_at_utc INTEGER NOT NULL,
    world_id TEXT NOT NULL CHECK (length(trim(world_id)) > 0),
    client_request_key TEXT NOT NULL CHECK (length(trim(client_request_key)) > 0),
    correlation_id TEXT NULL,
    status TEXT NOT NULL CHECK (status IN ('Pending', 'Succeeded', 'Rejected', 'Failed', 'Cancelled', 'ResultUnknown')),
    created_at_utc INTEGER NOT NULL,
    started_at_utc INTEGER NULL CHECK (started_at_utc IS NULL OR started_at_utc >= created_at_utc),
    completed_at_utc INTEGER NULL CHECK (completed_at_utc IS NULL OR completed_at_utc >= COALESCE(started_at_utc, created_at_utc)),
    failure_code TEXT NULL,
    before_inventory_snapshot_id INTEGER NULL REFERENCES player_inventory_snapshots(id) ON DELETE RESTRICT,
    after_inventory_snapshot_id INTEGER NULL REFERENCES player_inventory_snapshots(id) ON DELETE RESTRICT,
    before_skill_snapshot_id INTEGER NULL REFERENCES player_skill_snapshots(id) ON DELETE RESTRICT,
    after_skill_snapshot_id INTEGER NULL REFERENCES player_skill_snapshots(id) ON DELETE RESTRICT,
    catalog_version TEXT NOT NULL CHECK (length(trim(catalog_version)) > 0),
    resource_id TEXT NULL CHECK (resource_id IS NULL OR length(trim(resource_id)) > 0),
    internal_name TEXT NOT NULL CHECK (length(trim(internal_name)) > 0),
    item_kind TEXT NOT NULL CHECK (length(trim(item_kind)) > 0),
    quantity INTEGER NOT NULL CHECK (quantity > 0),
    quality INTEGER NULL CHECK (quality IS NULL OR quality >= 0),
    removal_scope TEXT NOT NULL CHECK (removal_scope = 'BagOnly'),
    removal_mode TEXT NOT NULL CHECK (removal_mode IN ('Exact', 'UpToAvailable')),
    actual_quantity INTEGER NULL CHECK (actual_quantity IS NULL OR actual_quantity BETWEEN 0 AND quantity),
    CHECK ((status = 'Pending' AND completed_at_utc IS NULL)
        OR (status <> 'Pending' AND completed_at_utc IS NOT NULL))
);

CREATE UNIQUE INDEX ux_player_remove_item_operations_operator_request
    ON player_remove_item_operations (operator_id, client_request_key);
CREATE INDEX ix_player_remove_item_operations_target_created
    ON player_remove_item_operations (target_crossplatform_id, created_at_utc DESC, operation_id DESC);

CREATE TABLE player_reset_skills_operations (
    operation_id TEXT NOT NULL PRIMARY KEY CHECK (length(trim(operation_id)) > 0),
    operator_id TEXT NOT NULL CHECK (length(trim(operator_id)) > 0),
    target_crossplatform_id TEXT NOT NULL CHECK (length(trim(target_crossplatform_id)) > 0),
    target_entity_id INTEGER NOT NULL CHECK (target_entity_id >= 0),
    target_online_observed_at_utc INTEGER NOT NULL,
    world_id TEXT NOT NULL CHECK (length(trim(world_id)) > 0),
    client_request_key TEXT NOT NULL CHECK (length(trim(client_request_key)) > 0),
    correlation_id TEXT NULL,
    status TEXT NOT NULL CHECK (status IN ('Pending', 'Succeeded', 'Rejected', 'Failed', 'Cancelled', 'ResultUnknown')),
    created_at_utc INTEGER NOT NULL,
    started_at_utc INTEGER NULL CHECK (started_at_utc IS NULL OR started_at_utc >= created_at_utc),
    completed_at_utc INTEGER NULL CHECK (completed_at_utc IS NULL OR completed_at_utc >= COALESCE(started_at_utc, created_at_utc)),
    failure_code TEXT NULL,
    before_inventory_snapshot_id INTEGER NULL REFERENCES player_inventory_snapshots(id) ON DELETE RESTRICT,
    after_inventory_snapshot_id INTEGER NULL REFERENCES player_inventory_snapshots(id) ON DELETE RESTRICT,
    before_skill_snapshot_id INTEGER NULL REFERENCES player_skill_snapshots(id) ON DELETE RESTRICT,
    after_skill_snapshot_id INTEGER NULL REFERENCES player_skill_snapshots(id) ON DELETE RESTRICT,
    danger_confirmed INTEGER NOT NULL CHECK (danger_confirmed IN (0, 1)),
    CHECK ((status = 'Pending' AND completed_at_utc IS NULL)
        OR (status <> 'Pending' AND completed_at_utc IS NOT NULL))
);

CREATE UNIQUE INDEX ux_player_reset_skills_operations_operator_request
    ON player_reset_skills_operations (operator_id, client_request_key);
CREATE INDEX ix_player_reset_skills_operations_target_created
    ON player_reset_skills_operations (target_crossplatform_id, created_at_utc DESC, operation_id DESC);

CREATE TABLE player_clear_inventory_operations (
    operation_id TEXT NOT NULL PRIMARY KEY CHECK (length(trim(operation_id)) > 0),
    operator_id TEXT NOT NULL CHECK (length(trim(operator_id)) > 0),
    target_crossplatform_id TEXT NOT NULL CHECK (length(trim(target_crossplatform_id)) > 0),
    target_entity_id INTEGER NOT NULL CHECK (target_entity_id >= 0),
    target_online_observed_at_utc INTEGER NOT NULL,
    world_id TEXT NOT NULL CHECK (length(trim(world_id)) > 0),
    client_request_key TEXT NOT NULL CHECK (length(trim(client_request_key)) > 0),
    correlation_id TEXT NULL,
    status TEXT NOT NULL CHECK (status IN ('Pending', 'Succeeded', 'Rejected', 'Failed', 'Cancelled', 'ResultUnknown')),
    created_at_utc INTEGER NOT NULL,
    started_at_utc INTEGER NULL CHECK (started_at_utc IS NULL OR started_at_utc >= created_at_utc),
    completed_at_utc INTEGER NULL CHECK (completed_at_utc IS NULL OR completed_at_utc >= COALESCE(started_at_utc, created_at_utc)),
    failure_code TEXT NULL,
    before_inventory_snapshot_id INTEGER NULL REFERENCES player_inventory_snapshots(id) ON DELETE RESTRICT,
    after_inventory_snapshot_id INTEGER NULL REFERENCES player_inventory_snapshots(id) ON DELETE RESTRICT,
    before_skill_snapshot_id INTEGER NULL REFERENCES player_skill_snapshots(id) ON DELETE RESTRICT,
    after_skill_snapshot_id INTEGER NULL REFERENCES player_skill_snapshots(id) ON DELETE RESTRICT,
    removal_scope TEXT NOT NULL CHECK (removal_scope = 'BagOnly'),
    danger_confirmed INTEGER NOT NULL CHECK (danger_confirmed IN (0, 1)),
    CHECK ((status = 'Pending' AND completed_at_utc IS NULL)
        OR (status <> 'Pending' AND completed_at_utc IS NOT NULL))
);

CREATE UNIQUE INDEX ux_player_clear_inventory_operations_operator_request
    ON player_clear_inventory_operations (operator_id, client_request_key);
CREATE INDEX ix_player_clear_inventory_operations_target_created
    ON player_clear_inventory_operations (target_crossplatform_id, created_at_utc DESC, operation_id DESC);

CREATE TABLE player_reset_data_operations (
    operation_id TEXT NOT NULL PRIMARY KEY CHECK (length(trim(operation_id)) > 0),
    operator_id TEXT NOT NULL CHECK (length(trim(operator_id)) > 0),
    target_crossplatform_id TEXT NOT NULL CHECK (length(trim(target_crossplatform_id)) > 0),
    target_entity_id INTEGER NOT NULL CHECK (target_entity_id >= 0),
    target_online_observed_at_utc INTEGER NOT NULL,
    world_id TEXT NOT NULL CHECK (length(trim(world_id)) > 0),
    client_request_key TEXT NOT NULL CHECK (length(trim(client_request_key)) > 0),
    correlation_id TEXT NULL,
    status TEXT NOT NULL CHECK (status IN ('Pending', 'Succeeded', 'Rejected', 'Failed', 'Cancelled', 'ResultUnknown')),
    created_at_utc INTEGER NOT NULL,
    started_at_utc INTEGER NULL CHECK (started_at_utc IS NULL OR started_at_utc >= created_at_utc),
    completed_at_utc INTEGER NULL CHECK (completed_at_utc IS NULL OR completed_at_utc >= COALESCE(started_at_utc, created_at_utc)),
    failure_code TEXT NULL,
    before_inventory_snapshot_id INTEGER NULL REFERENCES player_inventory_snapshots(id) ON DELETE RESTRICT,
    after_inventory_snapshot_id INTEGER NULL REFERENCES player_inventory_snapshots(id) ON DELETE RESTRICT,
    before_skill_snapshot_id INTEGER NULL REFERENCES player_skill_snapshots(id) ON DELETE RESTRICT,
    after_skill_snapshot_id INTEGER NULL REFERENCES player_skill_snapshots(id) ON DELETE RESTRICT,
    danger_confirmed INTEGER NOT NULL CHECK (danger_confirmed IN (0, 1)),
    CHECK ((status = 'Pending' AND completed_at_utc IS NULL)
        OR (status <> 'Pending' AND completed_at_utc IS NOT NULL))
);

CREATE UNIQUE INDEX ux_player_reset_data_operations_operator_request
    ON player_reset_data_operations (operator_id, client_request_key);
CREATE INDEX ix_player_reset_data_operations_target_created
    ON player_reset_data_operations (target_crossplatform_id, created_at_utc DESC, operation_id DESC);

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
    'consoleCommand', audit_id, actor_subject, NULL,
    COALESCE(command_name, 'command'), started_utc, completion_kind, NULL, 0
FROM console_command_audit
UNION ALL
SELECT
    'serverOperation', operation_id, actor_subject, NULL, operation_type,
    CAST(strftime('%s', requested_utc) AS INTEGER) * 1000
        + CAST(substr(strftime('%f', requested_utc), 4, 3) AS INTEGER),
    status, NULL, 0
FROM server_operation_audit
UNION ALL
SELECT
    'chatOperation', CAST(id AS TEXT), actor_subject, target_crossplatform_id,
    operation, occurred_utc, result, business_key, 0
FROM chat_operation_audit
UNION ALL
SELECT
    'chatMuteOperation', operation_id, actor_subject, target_crossplatform_id,
    operation_kind, occurred_utc, result, correlation_id, 0
FROM chat_mute_operation
UNION ALL
SELECT
    'serverOperation', 'job:' || id, actor_subject, source_schedule_id,
    'job.' || kind, created_at_utc, status, correlation_id, 0
FROM jobs
UNION ALL
SELECT
    'serverOperation', 'scheduleRun:' || id, NULL, schedule_id,
    'schedule.run', created_at_utc, outcome, NULL, 0
FROM schedule_runs
UNION ALL
SELECT
    'serverOperation', 'jobAdminOperation:' || id, actor_subject,
    target_kind || ':' || target_id, action, occurred_utc, status, correlation_id, 0
FROM job_admin_operations
UNION ALL
SELECT
    'playerAction', operation_id, operator_id, target_crossplatform_id,
    'GrantItem', created_at_utc, status, correlation_id, 0
FROM player_grant_item_operations
UNION ALL
SELECT
    'playerAction', operation_id, operator_id, target_crossplatform_id,
    'RemoveItem', created_at_utc, status, correlation_id, 0
FROM player_remove_item_operations
UNION ALL
SELECT
    'playerAction', operation_id, operator_id, target_crossplatform_id,
    'ResetSkills', created_at_utc, status, correlation_id, 0
FROM player_reset_skills_operations
UNION ALL
SELECT
    'playerAction', operation_id, operator_id, target_crossplatform_id,
    'ClearInventory', created_at_utc, status, correlation_id, 0
FROM player_clear_inventory_operations
UNION ALL
SELECT
    'playerAction', operation_id, operator_id, target_crossplatform_id,
    'ResetPlayerData', created_at_utc, status, correlation_id, 0
FROM player_reset_data_operations;
