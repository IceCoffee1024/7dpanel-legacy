CREATE TABLE world_operations (
    operation_id TEXT PRIMARY KEY CHECK (length(trim(operation_id)) > 0),
    job_id TEXT NOT NULL UNIQUE REFERENCES jobs(id) ON DELETE RESTRICT,
    actor_subject TEXT NOT NULL CHECK (length(trim(actor_subject)) > 0),
    kind TEXT NOT NULL CHECK (kind IN (
        'DeleteLandClaim', 'MoveOnlinePlayer', 'MoveEntity',
        'RefreshMapResources', 'RenderExploredMap', 'RenderFullMap',
        'CopyRegion', 'FillRegion', 'ClearRegion', 'PasteRegion', 'SetBlock',
        'PlacePrefab', 'RemovePrefab', 'SpawnEntity', 'DeleteEntity',
        'CleanupEntities', 'ReloadBlocks', 'ReloadItems', 'ReloadEntityClasses',
        'ReloadPrefabs', 'CollectGarbage', 'UndoChangeSet')),
    world_id TEXT NOT NULL CHECK (length(trim(world_id)) > 0),
    world_version TEXT NOT NULL CHECK (length(trim(world_version)) > 0),
    map_resource_version TEXT NULL
        CHECK (map_resource_version IS NULL OR length(trim(map_resource_version)) > 0),
    correlation_id TEXT NOT NULL UNIQUE CHECK (length(trim(correlation_id)) > 0),
    confirmation_summary TEXT NOT NULL
        CHECK (length(trim(confirmation_summary)) BETWEEN 1 AND 256),
    is_reversible INTEGER NOT NULL CHECK (is_reversible IN (0, 1)),
    change_set_id TEXT NULL REFERENCES world_change_sets(change_set_id) ON DELETE RESTRICT,
    created_at_utc INTEGER NOT NULL CHECK (created_at_utc >= 0),
    submission_failure_code TEXT NULL,
    rollback_failure_code TEXT NULL,
    rollback_failed_at_utc INTEGER NULL
        CHECK (rollback_failed_at_utc IS NULL OR rollback_failed_at_utc >= created_at_utc),
    CHECK ((rollback_failure_code IS NULL) = (rollback_failed_at_utc IS NULL))
);

CREATE INDEX ix_world_operations_created_keyset
    ON world_operations(created_at_utc DESC, operation_id DESC);
CREATE INDEX ix_world_operations_kind_created
    ON world_operations(kind, created_at_utc DESC, operation_id DESC);

CREATE TABLE world_operation_entity_targets (
    operation_id TEXT PRIMARY KEY REFERENCES world_operations(operation_id) ON DELETE CASCADE,
    target_id TEXT NOT NULL CHECK (length(trim(target_id)) > 0),
    entity_id INTEGER NULL CHECK (entity_id IS NULL OR entity_id >= 0),
    stable_identity TEXT NULL,
    entity_type_resource_id TEXT NULL,
    owner_identity TEXT NULL,
    observed_x REAL NULL,
    observed_y REAL NULL,
    observed_z REAL NULL,
    destination_x REAL NULL,
    destination_y REAL NULL,
    destination_z REAL NULL,
    quantity INTEGER NULL CHECK (quantity IS NULL OR quantity BETWEEN 1 AND 1000),
    radius REAL NULL CHECK (radius IS NULL OR radius BETWEEN 0 AND 1000),
    entity_category TEXT NULL CHECK (entity_category IS NULL OR entity_category IN (
        'Animal', 'Hostile', 'Vehicle', 'Drone', 'DroppedItem')),
    CHECK ((observed_x IS NULL AND observed_y IS NULL AND observed_z IS NULL) OR
           (observed_x IS NOT NULL AND observed_y IS NOT NULL AND observed_z IS NOT NULL)),
    CHECK ((destination_x IS NULL AND destination_y IS NULL AND destination_z IS NULL) OR
           (destination_x IS NOT NULL AND destination_y IS NOT NULL AND destination_z IS NOT NULL))
);

CREATE TABLE world_operation_map_targets (
    operation_id TEXT PRIMARY KEY REFERENCES world_operations(operation_id) ON DELETE CASCADE,
    minimum_x INTEGER NULL,
    minimum_z INTEGER NULL,
    maximum_x INTEGER NULL,
    maximum_z INTEGER NULL,
    CHECK ((minimum_x IS NULL AND minimum_z IS NULL AND maximum_x IS NULL AND maximum_z IS NULL) OR
           (minimum_x IS NOT NULL AND minimum_z IS NOT NULL AND maximum_x IS NOT NULL AND maximum_z IS NOT NULL
            AND minimum_x <= maximum_x AND minimum_z <= maximum_z))
);

CREATE TABLE world_operation_region_targets (
    operation_id TEXT PRIMARY KEY REFERENCES world_operations(operation_id) ON DELETE CASCADE,
    minimum_x INTEGER NOT NULL,
    minimum_y INTEGER NOT NULL,
    minimum_z INTEGER NOT NULL,
    maximum_x INTEGER NOT NULL,
    maximum_y INTEGER NOT NULL,
    maximum_z INTEGER NOT NULL,
    source_change_set_id TEXT NULL,
    block_internal_name TEXT NULL,
    CHECK (minimum_x <= maximum_x AND minimum_y <= maximum_y AND minimum_z <= maximum_z),
    CHECK (source_change_set_id IS NULL OR length(trim(source_change_set_id)) > 0),
    CHECK (block_internal_name IS NULL OR length(trim(block_internal_name)) > 0)
);

CREATE TABLE world_operation_block_targets (
    operation_id TEXT PRIMARY KEY REFERENCES world_operations(operation_id) ON DELETE CASCADE,
    x INTEGER NOT NULL,
    y INTEGER NOT NULL,
    z INTEGER NOT NULL,
    block_internal_name TEXT NOT NULL CHECK (length(trim(block_internal_name)) > 0),
    rotation INTEGER NOT NULL CHECK (rotation BETWEEN 0 AND 3),
    shape TEXT NULL CHECK (shape IS NULL OR length(trim(shape)) > 0)
);

CREATE TABLE world_operation_prefab_targets (
    operation_id TEXT PRIMARY KEY REFERENCES world_operations(operation_id) ON DELETE CASCADE,
    prefab_resource_id TEXT NOT NULL CHECK (length(trim(prefab_resource_id)) > 0),
    prefab_instance_id TEXT NULL
        CHECK (prefab_instance_id IS NULL OR length(trim(prefab_instance_id)) > 0),
    anchor_x INTEGER NOT NULL,
    anchor_y INTEGER NOT NULL,
    anchor_z INTEGER NOT NULL,
    rotation INTEGER NOT NULL CHECK (rotation BETWEEN 0 AND 3),
    minimum_x INTEGER NULL,
    minimum_y INTEGER NULL,
    minimum_z INTEGER NULL,
    maximum_x INTEGER NULL,
    maximum_y INTEGER NULL,
    maximum_z INTEGER NULL,
    CHECK ((minimum_x IS NULL AND minimum_y IS NULL AND minimum_z IS NULL AND
            maximum_x IS NULL AND maximum_y IS NULL AND maximum_z IS NULL) OR
           (minimum_x IS NOT NULL AND minimum_y IS NOT NULL AND minimum_z IS NOT NULL AND
            maximum_x IS NOT NULL AND maximum_y IS NOT NULL AND maximum_z IS NOT NULL AND
            minimum_x <= maximum_x AND minimum_y <= maximum_y AND minimum_z <= maximum_z))
);

CREATE TABLE world_operation_maintenance_targets (
    operation_id TEXT PRIMARY KEY REFERENCES world_operations(operation_id) ON DELETE CASCADE,
    entity_type_resource_id TEXT NULL
        CHECK (entity_type_resource_id IS NULL OR length(trim(entity_type_resource_id)) > 0)
);

CREATE TRIGGER world_entity_target_exclusive BEFORE INSERT ON world_operation_entity_targets
WHEN EXISTS (SELECT 1 FROM world_operation_map_targets WHERE operation_id = NEW.operation_id)
  OR EXISTS (SELECT 1 FROM world_operation_region_targets WHERE operation_id = NEW.operation_id)
  OR EXISTS (SELECT 1 FROM world_operation_block_targets WHERE operation_id = NEW.operation_id)
  OR EXISTS (SELECT 1 FROM world_operation_prefab_targets WHERE operation_id = NEW.operation_id)
  OR EXISTS (SELECT 1 FROM world_operation_maintenance_targets WHERE operation_id = NEW.operation_id)
BEGIN SELECT RAISE(ABORT, 'world_operation_target_already_exists'); END;

CREATE TRIGGER world_map_target_exclusive BEFORE INSERT ON world_operation_map_targets
WHEN EXISTS (SELECT 1 FROM world_operation_entity_targets WHERE operation_id = NEW.operation_id)
  OR EXISTS (SELECT 1 FROM world_operation_region_targets WHERE operation_id = NEW.operation_id)
  OR EXISTS (SELECT 1 FROM world_operation_block_targets WHERE operation_id = NEW.operation_id)
  OR EXISTS (SELECT 1 FROM world_operation_prefab_targets WHERE operation_id = NEW.operation_id)
  OR EXISTS (SELECT 1 FROM world_operation_maintenance_targets WHERE operation_id = NEW.operation_id)
BEGIN SELECT RAISE(ABORT, 'world_operation_target_already_exists'); END;

CREATE TRIGGER world_region_target_exclusive BEFORE INSERT ON world_operation_region_targets
WHEN EXISTS (SELECT 1 FROM world_operation_entity_targets WHERE operation_id = NEW.operation_id)
  OR EXISTS (SELECT 1 FROM world_operation_map_targets WHERE operation_id = NEW.operation_id)
  OR EXISTS (SELECT 1 FROM world_operation_block_targets WHERE operation_id = NEW.operation_id)
  OR EXISTS (SELECT 1 FROM world_operation_prefab_targets WHERE operation_id = NEW.operation_id)
  OR EXISTS (SELECT 1 FROM world_operation_maintenance_targets WHERE operation_id = NEW.operation_id)
BEGIN SELECT RAISE(ABORT, 'world_operation_target_already_exists'); END;

CREATE TRIGGER world_block_target_exclusive BEFORE INSERT ON world_operation_block_targets
WHEN EXISTS (SELECT 1 FROM world_operation_entity_targets WHERE operation_id = NEW.operation_id)
  OR EXISTS (SELECT 1 FROM world_operation_map_targets WHERE operation_id = NEW.operation_id)
  OR EXISTS (SELECT 1 FROM world_operation_region_targets WHERE operation_id = NEW.operation_id)
  OR EXISTS (SELECT 1 FROM world_operation_prefab_targets WHERE operation_id = NEW.operation_id)
  OR EXISTS (SELECT 1 FROM world_operation_maintenance_targets WHERE operation_id = NEW.operation_id)
BEGIN SELECT RAISE(ABORT, 'world_operation_target_already_exists'); END;

CREATE TRIGGER world_prefab_target_exclusive BEFORE INSERT ON world_operation_prefab_targets
WHEN EXISTS (SELECT 1 FROM world_operation_entity_targets WHERE operation_id = NEW.operation_id)
  OR EXISTS (SELECT 1 FROM world_operation_map_targets WHERE operation_id = NEW.operation_id)
  OR EXISTS (SELECT 1 FROM world_operation_region_targets WHERE operation_id = NEW.operation_id)
  OR EXISTS (SELECT 1 FROM world_operation_block_targets WHERE operation_id = NEW.operation_id)
  OR EXISTS (SELECT 1 FROM world_operation_maintenance_targets WHERE operation_id = NEW.operation_id)
BEGIN SELECT RAISE(ABORT, 'world_operation_target_already_exists'); END;

CREATE TRIGGER world_maintenance_target_exclusive BEFORE INSERT ON world_operation_maintenance_targets
WHEN EXISTS (SELECT 1 FROM world_operation_entity_targets WHERE operation_id = NEW.operation_id)
  OR EXISTS (SELECT 1 FROM world_operation_map_targets WHERE operation_id = NEW.operation_id)
  OR EXISTS (SELECT 1 FROM world_operation_region_targets WHERE operation_id = NEW.operation_id)
  OR EXISTS (SELECT 1 FROM world_operation_block_targets WHERE operation_id = NEW.operation_id)
  OR EXISTS (SELECT 1 FROM world_operation_prefab_targets WHERE operation_id = NEW.operation_id)
BEGIN SELECT RAISE(ABORT, 'world_operation_target_already_exists'); END;

CREATE TABLE world_change_sets (
    change_set_id TEXT PRIMARY KEY CHECK (length(trim(change_set_id)) > 0),
    source_operation_id TEXT NOT NULL UNIQUE
        REFERENCES world_operations(operation_id) ON DELETE RESTRICT,
    world_id TEXT NOT NULL CHECK (length(trim(world_id)) > 0),
    world_version TEXT NOT NULL CHECK (length(trim(world_version)) > 0),
    minimum_x INTEGER NOT NULL,
    minimum_y INTEGER NOT NULL,
    minimum_z INTEGER NOT NULL,
    maximum_x INTEGER NOT NULL,
    maximum_y INTEGER NOT NULL,
    maximum_z INTEGER NOT NULL,
    before_hash TEXT NOT NULL CHECK (length(before_hash) = 64),
    after_hash TEXT NOT NULL CHECK (length(after_hash) = 64),
    storage_resource_id TEXT NOT NULL UNIQUE CHECK (length(trim(storage_resource_id)) > 0),
    created_at_utc INTEGER NOT NULL CHECK (created_at_utc >= 0),
    expires_at_utc INTEGER NOT NULL,
    CHECK (minimum_x <= maximum_x AND minimum_y <= maximum_y AND minimum_z <= maximum_z),
    CHECK (expires_at_utc > created_at_utc)
);

CREATE INDEX ix_world_change_sets_expiry
    ON world_change_sets(expires_at_utc ASC, change_set_id ASC);

CREATE TABLE world_change_set_chunks (
    change_set_id TEXT NOT NULL REFERENCES world_change_sets(change_set_id) ON DELETE CASCADE,
    chunk_index INTEGER NOT NULL CHECK (chunk_index >= 0),
    chunk_x INTEGER NOT NULL,
    chunk_z INTEGER NOT NULL,
    content_hash TEXT NOT NULL CHECK (length(content_hash) = 64),
    uncompressed_byte_count INTEGER NOT NULL CHECK (uncompressed_byte_count >= 0),
    PRIMARY KEY (change_set_id, chunk_index),
    UNIQUE (change_set_id, chunk_x, chunk_z)
);

CREATE TABLE feature_module_states (
    module_id TEXT PRIMARY KEY CHECK (length(trim(module_id)) > 0),
    is_enabled INTEGER NOT NULL CHECK (is_enabled IN (0, 1)),
    lifecycle_state TEXT NOT NULL
        CHECK (lifecycle_state IN ('Enabled', 'Disabled', 'Draining', 'RestartRequired')),
    updated_by TEXT NOT NULL CHECK (length(trim(updated_by)) > 0),
    correlation_id TEXT NOT NULL UNIQUE CHECK (length(trim(correlation_id)) > 0),
    updated_at_utc INTEGER NOT NULL CHECK (updated_at_utc >= 0),
    row_version INTEGER NOT NULL DEFAULT 1 CHECK (row_version > 0),
    CHECK ((is_enabled = 1 AND lifecycle_state IN ('Enabled', 'RestartRequired')) OR
           (is_enabled = 0 AND lifecycle_state IN ('Disabled', 'Draining', 'RestartRequired')))
);

DROP VIEW IF EXISTS unified_audit_projection;

CREATE VIEW unified_audit_projection AS
SELECT 'playerAction' AS source_kind, operation_id AS source_id, actor_subject,
    target_platform_id AS target_ref, action_type AS action, requested_utc AS occurred_utc,
    status, NULL AS correlation_id, 0 AS has_details
FROM player_action_audit
UNION ALL
SELECT 'consoleCommand', audit_id, actor_subject, NULL,
    COALESCE(command_name, 'command'), started_utc, completion_kind, NULL, 0
FROM console_command_audit
UNION ALL
SELECT 'serverOperation', operation_id, actor_subject, NULL, operation_type,
    CAST(strftime('%s', requested_utc) AS INTEGER) * 1000
        + CAST(substr(strftime('%f', requested_utc), 4, 3) AS INTEGER),
    status, NULL, 0
FROM server_operation_audit
UNION ALL
SELECT 'chatOperation', CAST(id AS TEXT), actor_subject, target_crossplatform_id,
    operation, occurred_utc, result, business_key, 0
FROM chat_operation_audit
UNION ALL
SELECT 'chatMuteOperation', operation_id, actor_subject, target_crossplatform_id,
    operation_kind, occurred_utc, result, correlation_id, 0
FROM chat_mute_operation
UNION ALL
SELECT 'serverOperation', 'job:' || id, actor_subject, source_schedule_id,
    'job.' || kind, created_at_utc, status, correlation_id, 0
FROM jobs
UNION ALL
SELECT 'serverOperation', 'scheduleRun:' || id, NULL, schedule_id,
    'schedule.run', created_at_utc, outcome, NULL, 0
FROM schedule_runs
UNION ALL
SELECT 'serverOperation', 'jobAdminOperation:' || id, actor_subject,
    target_kind || ':' || target_id, action, occurred_utc, status, correlation_id, 0
FROM job_admin_operations
UNION ALL
SELECT 'playerAction', operation_id, operator_id, target_crossplatform_id,
    'GrantItem', created_at_utc, status, correlation_id, 0
FROM player_grant_item_operations
UNION ALL
SELECT 'playerAction', operation_id, operator_id, target_crossplatform_id,
    'RemoveItem', created_at_utc, status, correlation_id, 0
FROM player_remove_item_operations
UNION ALL
SELECT 'playerAction', operation_id, operator_id, target_crossplatform_id,
    'ResetSkills', created_at_utc, status, correlation_id, 0
FROM player_reset_skills_operations
UNION ALL
SELECT 'playerAction', operation_id, operator_id, target_crossplatform_id,
    'ClearInventory', created_at_utc, status, correlation_id, 0
FROM player_clear_inventory_operations
UNION ALL
SELECT 'playerAction', operation_id, operator_id, target_crossplatform_id,
    'ResetPlayerData', created_at_utc, status, correlation_id, 0
FROM player_reset_data_operations
UNION ALL
SELECT 'economyTransaction', 'economyTransaction:' || transaction_id, actor_id,
    related_crossplatform_id, 'economy.' || transaction_type, occurred_utc,
    status, correlation_id, 0
FROM economy_transactions
UNION ALL
SELECT 'rewardGrant', 'grantOperation:' || operation_id, actor_id, crossplatform_id,
    'reward.grant', created_at_utc, state, correlation_id, 0
FROM grant_operations
UNION ALL
SELECT 'commerceOperation', 'purchase:' || purchase_id, crossplatform_id,
    crossplatform_id, 'shop.purchase', created_at_utc, state, correlation_id, 0
FROM shop_purchases
UNION ALL
SELECT 'commerceOperation', 'redeemAttempt:' || attempt_id, crossplatform_id,
    crossplatform_id, 'redeem.attempt', attempted_at_utc, result, correlation_id, 0
FROM redeem_attempts
UNION ALL
SELECT 'rewardEligibility', 'rewardEligibility:' || eligibility_id, NULL,
    crossplatform_id, 'reward.' || rule_kind, created_at_utc, state, correlation_id, 0
FROM reward_eligibilities
UNION ALL
SELECT 'teleportOperation', 'teleportOperation:' || operation_id, actor_id,
    crossplatform_id, 'teleport.' || teleport_kind, created_at_utc,
    state, correlation_id, 0
FROM teleport_operations
UNION ALL
SELECT 'voteRound', 'voteRound:' || round_id, initiator_crossplatform_id,
    target_crossplatform_id, 'vote.' || vote_kind, opened_at_utc,
    state, correlation_id, 0
FROM vote_rounds
UNION ALL
SELECT 'automationExecution', execution.execution_id, NULL, trigger.actor_crossplatform_id,
    'automation.execute', COALESCE(execution.started_utc, trigger.occurred_utc),
    execution.status, execution.correlation_id, 0
FROM automation_executions AS execution
INNER JOIN automation_triggers AS trigger ON trigger.trigger_id = execution.trigger_id
UNION ALL
SELECT 'integrationOperation', operation_id, actor_subject, target_id,
    action, occurred_utc, status, correlation_id, 0
FROM automation_integration_operation_audit
UNION ALL
SELECT 'discordDelivery', delivery_id, NULL, target_key,
    'discord.delivery', created_utc, status, NULL, 0
FROM discord_deliveries
UNION ALL
SELECT 'geoIpDecision', decision_id, NULL, crossplatform_id,
    'geoip.decision', occurred_utc, decision, NULL, 0
FROM geoip_decisions
UNION ALL
SELECT 'worldOperation', operation.operation_id, operation.actor_subject, operation.world_id,
    'world.' || operation.kind, operation.created_at_utc,
    CASE
        WHEN operation.rollback_failure_code IS NOT NULL THEN 'RollbackFailed'
        WHEN operation.submission_failure_code IS NOT NULL THEN 'Failed'
        WHEN job.status = 'PendingRestart' THEN 'Interrupted'
        ELSE job.status
    END,
    operation.correlation_id, 0
FROM world_operations AS operation
INNER JOIN jobs AS job ON job.id = operation.job_id
UNION ALL
SELECT 'featureModule', 'featureModule:' || module_id, updated_by, module_id,
    CASE WHEN is_enabled = 1 THEN 'module.enable' ELSE 'module.disable' END,
    updated_at_utc, lifecycle_state, correlation_id, 0
FROM feature_module_states;
