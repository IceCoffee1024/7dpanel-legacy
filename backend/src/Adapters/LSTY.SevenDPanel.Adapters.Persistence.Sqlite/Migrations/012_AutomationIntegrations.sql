CREATE TABLE automation_rules (
    rule_id TEXT PRIMARY KEY
        CHECK (length(trim(rule_id)) > 0),
    version INTEGER NOT NULL
        CHECK (version > 0),
    name TEXT NOT NULL COLLATE NOCASE
        CHECK (length(trim(name)) > 0 AND length(name) <= 128),
    trigger_type TEXT NOT NULL
        CHECK (length(trim(trigger_type)) > 0),
    enabled INTEGER NOT NULL
        CHECK (enabled IN (0, 1)),
    deleted INTEGER NOT NULL DEFAULT 0
        CHECK (deleted IN (0, 1)),
    cooldown_seconds INTEGER NOT NULL
        CHECK (cooldown_seconds >= 0),
    cooldown_scope TEXT NOT NULL
        CHECK (cooldown_scope IN ('Rule', 'RulePlayer')),
    concurrency_policy TEXT NOT NULL
        CHECK (concurrency_policy IN ('SkipIfRunning', 'QueueOne')),
    failure_policy TEXT NOT NULL
        CHECK (failure_policy IN ('StopOnFailure', 'Continue')),
    created_utc INTEGER NOT NULL
        CHECK (created_utc >= 0),
    updated_utc INTEGER NOT NULL
        CHECK (updated_utc >= created_utc),
    CHECK (deleted = 0 OR enabled = 0)
);

CREATE INDEX ix_automation_rules_enabled_updated
    ON automation_rules(enabled DESC, updated_utc DESC, rule_id ASC);

CREATE TABLE automation_condition_nodes (
    node_id TEXT PRIMARY KEY
        CHECK (length(trim(node_id)) > 0),
    rule_id TEXT NOT NULL,
    parent_node_id TEXT NULL,
    ordinal INTEGER NOT NULL
        CHECK (ordinal >= 0),
    node_kind TEXT NOT NULL
        CHECK (node_kind IN ('All', 'Any', 'Not', 'Predicate')),
    field_key TEXT NULL,
    operator TEXT NULL
        CHECK (operator IS NULL OR operator IN (
            'Equals', 'NotEquals', 'InSet', 'NumberRange', 'TimeWindow',
            'PlayerGroup', 'Permission', 'Cooldown')),
    scalar_value TEXT NULL,
    min_value INTEGER NULL,
    max_value INTEGER NULL,
    CHECK (operator <> 'NumberRange' OR
        (min_value IS NOT NULL AND max_value IS NOT NULL AND min_value <= max_value)),
    CHECK (operator <> 'TimeWindow' OR
        (scalar_value IS NOT NULL AND length(trim(scalar_value)) > 0
            AND min_value BETWEEN 0 AND 1439 AND max_value BETWEEN 0 AND 1439)),
    CHECK (
        (node_kind = 'Predicate'
            AND field_key IS NOT NULL AND length(trim(field_key)) > 0
            AND operator IS NOT NULL)
        OR
        (node_kind <> 'Predicate'
            AND field_key IS NULL AND operator IS NULL
            AND scalar_value IS NULL AND min_value IS NULL AND max_value IS NULL)),
    FOREIGN KEY (rule_id) REFERENCES automation_rules(rule_id) ON DELETE CASCADE,
    FOREIGN KEY (parent_node_id) REFERENCES automation_condition_nodes(node_id) ON DELETE CASCADE,
    UNIQUE (rule_id, parent_node_id, ordinal)
);

CREATE UNIQUE INDEX ux_automation_condition_root
    ON automation_condition_nodes(rule_id)
    WHERE parent_node_id IS NULL;
CREATE INDEX ix_automation_condition_children
    ON automation_condition_nodes(rule_id, parent_node_id, ordinal ASC);

CREATE TABLE automation_condition_set_values (
    node_id TEXT NOT NULL,
    ordinal INTEGER NOT NULL
        CHECK (ordinal >= 0),
    value TEXT NOT NULL
        CHECK (length(value) <= 128),
    PRIMARY KEY (node_id, ordinal),
    FOREIGN KEY (node_id) REFERENCES automation_condition_nodes(node_id) ON DELETE CASCADE
);

CREATE TABLE automation_actions (
    action_id TEXT PRIMARY KEY
        CHECK (length(trim(action_id)) > 0),
    rule_id TEXT NOT NULL,
    ordinal INTEGER NOT NULL
        CHECK (ordinal >= 0),
    action_type TEXT NOT NULL
        CHECK (length(trim(action_type)) > 0),
    target_kind TEXT NOT NULL
        CHECK (length(trim(target_kind)) > 0),
    text_value TEXT NULL
        CHECK (text_value IS NULL OR length(text_value) <= 512),
    reference_id TEXT NULL
        CHECK (reference_id IS NULL OR length(trim(reference_id)) > 0),
    amount INTEGER NULL,
    duration_seconds INTEGER NULL
        CHECK (duration_seconds IS NULL OR duration_seconds >= 0),
    FOREIGN KEY (rule_id) REFERENCES automation_rules(rule_id) ON DELETE CASCADE,
    UNIQUE (rule_id, ordinal)
);

CREATE INDEX ix_automation_actions_rule
    ON automation_actions(rule_id, ordinal ASC);

CREATE TABLE automation_triggers (
    trigger_id TEXT PRIMARY KEY
        CHECK (length(trim(trigger_id)) > 0),
    trigger_type TEXT NOT NULL
        CHECK (length(trim(trigger_type)) > 0),
    occurred_utc INTEGER NOT NULL
        CHECK (occurred_utc >= 0),
    actor_crossplatform_id TEXT NULL
        CHECK (actor_crossplatform_id IS NULL OR length(trim(actor_crossplatform_id)) > 0),
    actor_entity_id INTEGER NULL
        CHECK (actor_entity_id IS NULL OR actor_entity_id >= 0),
    actor_group TEXT NULL,
    permission_level INTEGER NULL,
    chat_text TEXT NULL,
    scheduled_for_utc INTEGER NULL
        CHECK (scheduled_for_utc IS NULL OR scheduled_for_utc >= 0),
    blood_moon_phase TEXT NULL
);

CREATE INDEX ix_automation_triggers_occurred_keyset
    ON automation_triggers(occurred_utc DESC, trigger_id DESC);

CREATE TABLE automation_trigger_gaps (
    trigger_id TEXT NOT NULL,
    gap_id TEXT NOT NULL,
    PRIMARY KEY (trigger_id, gap_id),
    FOREIGN KEY (trigger_id) REFERENCES automation_triggers(trigger_id) ON DELETE CASCADE,
    FOREIGN KEY (gap_id) REFERENCES game_event_gaps(gap_id) ON DELETE RESTRICT
);

CREATE TABLE automation_executions (
    execution_id TEXT PRIMARY KEY
        CHECK (length(trim(execution_id)) > 0),
    rule_id TEXT NOT NULL,
    trigger_id TEXT NOT NULL,
    status TEXT NOT NULL
        CHECK (status IN (
            'Pending', 'Running', 'Queued', 'Skipped', 'Succeeded', 'Failed',
            'ResultUnknown')),
    correlation_id TEXT NOT NULL
        CHECK (length(trim(correlation_id)) > 0),
    started_utc INTEGER NULL
        CHECK (started_utc IS NULL OR started_utc >= 0),
    completed_utc INTEGER NULL
        CHECK (completed_utc IS NULL OR completed_utc >= 0),
    error_code TEXT NULL,
    CHECK (started_utc IS NULL OR completed_utc IS NULL OR completed_utc >= started_utc),
    FOREIGN KEY (rule_id) REFERENCES automation_rules(rule_id) ON DELETE RESTRICT,
    FOREIGN KEY (trigger_id) REFERENCES automation_triggers(trigger_id) ON DELETE RESTRICT,
    UNIQUE (rule_id, trigger_id)
);

CREATE INDEX ix_automation_executions_started_keyset
    ON automation_executions(started_utc DESC, execution_id DESC);
CREATE INDEX ix_automation_executions_status
    ON automation_executions(status, started_utc ASC, execution_id ASC);

CREATE TABLE automation_condition_results (
    execution_id TEXT NOT NULL,
    node_id TEXT NOT NULL,
    truth TEXT NOT NULL
        CHECK (truth IN ('Matched', 'NotMatched', 'Unknown')),
    value_summary TEXT NULL
        CHECK (value_summary IS NULL OR length(value_summary) <= 512),
    PRIMARY KEY (execution_id, node_id),
    FOREIGN KEY (execution_id) REFERENCES automation_executions(execution_id) ON DELETE CASCADE
);

CREATE TABLE automation_action_results (
    execution_id TEXT NOT NULL,
    ordinal INTEGER NOT NULL
        CHECK (ordinal >= 0),
    action_type TEXT NOT NULL
        CHECK (length(trim(action_type)) > 0),
    status TEXT NOT NULL
        CHECK (status IN ('Pending', 'Running', 'Succeeded', 'Failed', 'ResultUnknown')),
    consumer_idempotency_key TEXT NOT NULL
        CHECK (length(trim(consumer_idempotency_key)) > 0),
    error_code TEXT NULL,
    started_utc INTEGER NOT NULL
        CHECK (started_utc >= 0),
    completed_utc INTEGER NULL
        CHECK (completed_utc IS NULL OR completed_utc >= started_utc),
    PRIMARY KEY (execution_id, ordinal),
    FOREIGN KEY (execution_id) REFERENCES automation_executions(execution_id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX ux_automation_action_consumer_key
    ON automation_action_results(consumer_idempotency_key);

CREATE TABLE discord_settings (
    singleton_id INTEGER PRIMARY KEY
        CHECK (singleton_id = 1),
    version INTEGER NOT NULL
        CHECK (version > 0),
    enabled INTEGER NOT NULL
        CHECK (enabled IN (0, 1)),
    mode TEXT NOT NULL
        CHECK (mode IN ('Webhook', 'Bot')),
    application_id TEXT NULL,
    guild_id TEXT NULL,
    public_channel_id TEXT NULL,
    bridge_game_to_discord INTEGER NOT NULL
        CHECK (bridge_game_to_discord IN (0, 1)),
    bridge_discord_to_game INTEGER NOT NULL
        CHECK (bridge_discord_to_game IN (0, 1)),
    proxy_enabled INTEGER NOT NULL
        CHECK (proxy_enabled IN (0, 1)),
    proxy_uri TEXT NULL,
    updated_utc INTEGER NOT NULL
        CHECK (updated_utc >= 0),
    CHECK (proxy_enabled = 1 OR proxy_uri IS NULL)
);

CREATE TABLE discord_secrets (
    secret_key TEXT PRIMARY KEY
        CHECK (length(trim(secret_key)) > 0),
    secret_value TEXT NOT NULL
        CHECK (length(secret_value) > 0),
    fingerprint TEXT NOT NULL
        CHECK (length(trim(fingerprint)) > 0),
    updated_utc INTEGER NOT NULL
        CHECK (updated_utc >= 0)
);

CREATE TABLE discord_targets (
    target_key TEXT PRIMARY KEY
        CHECK (length(trim(target_key)) > 0),
    delivery_mode TEXT NOT NULL
        CHECK (delivery_mode IN ('Webhook', 'Bot')),
    channel_id TEXT NULL,
    enabled INTEGER NOT NULL
        CHECK (enabled IN (0, 1))
);

CREATE TABLE discord_command_settings (
    command_key TEXT PRIMARY KEY
        CHECK (length(trim(command_key)) > 0),
    enabled INTEGER NOT NULL
        CHECK (enabled IN (0, 1)),
    remote_allowed INTEGER NOT NULL
        CHECK (remote_allowed IN (0, 1))
);

CREATE TABLE discord_deliveries (
    delivery_id TEXT PRIMARY KEY
        CHECK (length(trim(delivery_id)) > 0),
    business_key TEXT NOT NULL UNIQUE
        CHECK (length(trim(business_key)) > 0),
    target_key TEXT NOT NULL,
    status TEXT NOT NULL
        CHECK (status IN (
            'Pending', 'Sending', 'RetryScheduled', 'Succeeded', 'Failed',
            'ResultUnknown', 'Cancelled')),
    content_text TEXT NULL,
    content_summary TEXT NOT NULL
        CHECK (length(trim(content_summary)) > 0),
    next_attempt_utc INTEGER NULL
        CHECK (next_attempt_utc IS NULL OR next_attempt_utc >= 0),
    retry_count INTEGER NOT NULL
        CHECK (retry_count >= 0),
    created_utc INTEGER NOT NULL
        CHECK (created_utc >= 0),
    completed_utc INTEGER NULL
        CHECK (completed_utc IS NULL OR completed_utc >= created_utc),
    CHECK (status = 'RetryScheduled' OR next_attempt_utc IS NULL),
    FOREIGN KEY (target_key) REFERENCES discord_targets(target_key) ON DELETE RESTRICT
);

CREATE INDEX ix_discord_deliveries_due
    ON discord_deliveries(status, next_attempt_utc ASC, delivery_id ASC);
CREATE INDEX ix_discord_deliveries_created_keyset
    ON discord_deliveries(created_utc DESC, delivery_id DESC);

CREATE TABLE discord_delivery_attempts (
    delivery_id TEXT NOT NULL,
    attempt_no INTEGER NOT NULL
        CHECK (attempt_no > 0),
    status TEXT NOT NULL
        CHECK (status IN ('Sending', 'Succeeded', 'Failed', 'ResultUnknown', 'Cancelled')),
    started_utc INTEGER NOT NULL
        CHECK (started_utc >= 0),
    completed_utc INTEGER NULL
        CHECK (completed_utc IS NULL OR completed_utc >= started_utc),
    error_code TEXT NULL,
    PRIMARY KEY (delivery_id, attempt_no),
    FOREIGN KEY (delivery_id) REFERENCES discord_deliveries(delivery_id) ON DELETE CASCADE
);

CREATE TABLE discord_bindings (
    discord_subject TEXT PRIMARY KEY
        CHECK (length(trim(discord_subject)) > 0),
    crossplatform_id TEXT NOT NULL UNIQUE
        CHECK (length(trim(crossplatform_id)) > 0),
    active INTEGER NOT NULL
        CHECK (active IN (0, 1)),
    created_utc INTEGER NOT NULL
        CHECK (created_utc >= 0),
    updated_utc INTEGER NOT NULL
        CHECK (updated_utc >= created_utc)
);

CREATE TABLE discord_binding_codes (
    code_id TEXT PRIMARY KEY
        CHECK (length(trim(code_id)) > 0),
    crossplatform_id TEXT NOT NULL
        CHECK (length(trim(crossplatform_id)) > 0),
    code_prefix TEXT NOT NULL
        CHECK (length(code_prefix) = 4),
    code_hash BLOB NOT NULL UNIQUE
        CHECK (length(code_hash) > 0),
    expires_utc INTEGER NOT NULL
        CHECK (expires_utc >= 0),
    consumed_utc INTEGER NULL
        CHECK (consumed_utc IS NULL OR consumed_utc >= 0)
);

CREATE INDEX ix_discord_binding_codes_expiry
    ON discord_binding_codes(expires_utc ASC, code_id ASC)
    WHERE consumed_utc IS NULL;

CREATE TABLE discord_interactions (
    interaction_id TEXT PRIMARY KEY
        CHECK (length(trim(interaction_id)) > 0),
    command_key TEXT NOT NULL
        CHECK (length(trim(command_key)) > 0),
    status TEXT NOT NULL
        CHECK (status IN (
            'Pending', 'Succeeded', 'Rejected', 'Failed', 'Expired', 'ResultUnknown')),
    expires_utc INTEGER NOT NULL
        CHECK (expires_utc >= 0),
    completed_utc INTEGER NULL
        CHECK (completed_utc IS NULL OR completed_utc >= 0)
);

CREATE INDEX ix_discord_interactions_expiry
    ON discord_interactions(status, expires_utc ASC, interaction_id ASC);

CREATE TABLE discord_interaction_secrets (
    interaction_id TEXT PRIMARY KEY,
    token_value TEXT NOT NULL
        CHECK (length(token_value) > 0),
    expires_utc INTEGER NOT NULL
        CHECK (expires_utc >= 0),
    FOREIGN KEY (interaction_id) REFERENCES discord_interactions(interaction_id) ON DELETE CASCADE
);

CREATE TABLE discord_bridge_messages (
    bridge_message_id TEXT PRIMARY KEY
        CHECK (length(trim(bridge_message_id)) > 0),
    source TEXT NOT NULL
        CHECK (source IN ('Game', 'Discord')),
    source_message_id TEXT NOT NULL
        CHECK (length(trim(source_message_id)) > 0),
    expires_utc INTEGER NOT NULL
        CHECK (expires_utc >= 0),
    UNIQUE (source, source_message_id)
);

CREATE INDEX ix_discord_bridge_messages_expiry
    ON discord_bridge_messages(expires_utc ASC, bridge_message_id ASC);

CREATE TABLE geoip_settings (
    singleton_id INTEGER PRIMARY KEY
        CHECK (singleton_id = 1),
    version INTEGER NOT NULL
        CHECK (version > 0),
    enabled INTEGER NOT NULL
        CHECK (enabled IN (0, 1)),
    provider TEXT NOT NULL
        CHECK (length(trim(provider)) > 0),
    failure_mode TEXT NOT NULL
        CHECK (failure_mode IN ('FailOpen', 'FailClosed')),
    bypass_admins INTEGER NOT NULL
        CHECK (bypass_admins IN (0, 1)),
    rejection_message TEXT NOT NULL
        CHECK (length(trim(rejection_message)) > 0)
);

CREATE TABLE geoip_secrets (
    secret_key TEXT PRIMARY KEY
        CHECK (secret_key IN ('maxmind.account-id', 'maxmind.license-key')),
    secret_value TEXT NOT NULL
        CHECK (length(secret_value) > 0),
    fingerprint TEXT NOT NULL
        CHECK (length(trim(fingerprint)) > 0),
    updated_utc INTEGER NOT NULL
        CHECK (updated_utc >= 0)
);

CREATE TABLE geoip_network_rules (
    rule_id TEXT PRIMARY KEY
        CHECK (length(trim(rule_id)) > 0),
    network_cidr TEXT NOT NULL UNIQUE
        CHECK (length(trim(network_cidr)) > 0),
    effect TEXT NOT NULL
        CHECK (effect IN ('Allow', 'Deny')),
    ordinal INTEGER NOT NULL
        CHECK (ordinal >= 0)
);

CREATE UNIQUE INDEX ux_geoip_network_rules_ordinal
    ON geoip_network_rules(ordinal);

CREATE TABLE geoip_country_rules (
    country_code TEXT PRIMARY KEY
        CHECK (length(country_code) = 2 AND country_code = upper(country_code)),
    effect TEXT NOT NULL
        CHECK (effect IN ('Allow', 'Deny'))
);

CREATE TABLE geoip_cache (
    canonical_ip TEXT PRIMARY KEY
        CHECK (length(trim(canonical_ip)) > 0),
    lookup_status TEXT NOT NULL
        CHECK (lookup_status IN ('Found', 'Unknown', 'Private', 'Invalid', 'Unavailable')),
    country_code TEXT NULL
        CHECK (country_code IS NULL OR length(country_code) = 2),
    source TEXT NOT NULL
        CHECK (length(trim(source)) > 0),
    source_version TEXT NULL,
    queried_utc INTEGER NOT NULL
        CHECK (queried_utc >= 0),
    expires_utc INTEGER NOT NULL
        CHECK (expires_utc >= queried_utc)
);

CREATE INDEX ix_geoip_cache_expiry
    ON geoip_cache(expires_utc ASC, canonical_ip ASC);

CREATE TABLE geoip_decisions (
    decision_id TEXT PRIMARY KEY
        CHECK (length(trim(decision_id)) > 0),
    occurred_utc INTEGER NOT NULL
        CHECK (occurred_utc >= 0),
    masked_ip TEXT NOT NULL
        CHECK (length(trim(masked_ip)) > 0),
    crossplatform_id TEXT NULL
        CHECK (crossplatform_id IS NULL OR length(trim(crossplatform_id)) > 0),
    decision TEXT NOT NULL
        CHECK (decision IN ('Allowed', 'Denied', 'Bypassed', 'Unknown')),
    reason_code TEXT NOT NULL
        CHECK (length(trim(reason_code)) > 0),
    lookup_status TEXT NOT NULL
        CHECK (lookup_status IN ('Found', 'Unknown', 'Private', 'Invalid', 'Unavailable'))
);

CREATE INDEX ix_geoip_decisions_occurred_keyset
    ON geoip_decisions(occurred_utc DESC, decision_id DESC);

CREATE TABLE automation_integration_operation_audit (
    operation_id TEXT PRIMARY KEY
        CHECK (length(trim(operation_id)) > 0),
    actor_subject TEXT NOT NULL
        CHECK (length(trim(actor_subject)) > 0),
    action TEXT NOT NULL
        CHECK (length(trim(action)) > 0),
    target_kind TEXT NOT NULL
        CHECK (length(trim(target_kind)) > 0),
    target_id TEXT NULL,
    status TEXT NOT NULL
        CHECK (status IN ('Succeeded', 'Failed', 'Rejected')),
    occurred_utc INTEGER NOT NULL
        CHECK (occurred_utc >= 0),
    correlation_id TEXT NULL
);

CREATE INDEX ix_automation_integration_audit_occurred
    ON automation_integration_operation_audit(occurred_utc DESC, operation_id DESC);

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
FROM player_reset_data_operations
UNION ALL
SELECT
    'economyTransaction', 'economyTransaction:' || transaction_id, actor_id,
    related_crossplatform_id, 'economy.' || transaction_type, occurred_utc,
    status, correlation_id, 0
FROM economy_transactions
UNION ALL
SELECT
    'rewardGrant', 'grantOperation:' || operation_id, actor_id, crossplatform_id,
    'reward.grant', created_at_utc, state, correlation_id, 0
FROM grant_operations
UNION ALL
SELECT
    'commerceOperation', 'purchase:' || purchase_id, crossplatform_id,
    crossplatform_id, 'shop.purchase', created_at_utc, state, correlation_id, 0
FROM shop_purchases
UNION ALL
SELECT
    'commerceOperation', 'redeemAttempt:' || attempt_id, crossplatform_id,
    crossplatform_id, 'redeem.attempt', attempted_at_utc, result, correlation_id, 0
FROM redeem_attempts
UNION ALL
SELECT
    'rewardEligibility', 'rewardEligibility:' || eligibility_id, NULL,
    crossplatform_id, 'reward.' || rule_kind, created_at_utc, state, correlation_id, 0
FROM reward_eligibilities
UNION ALL
SELECT
    'teleportOperation', 'teleportOperation:' || operation_id, actor_id,
    crossplatform_id, 'teleport.' || teleport_kind, created_at_utc,
    state, correlation_id, 0
FROM teleport_operations
UNION ALL
SELECT
    'voteRound', 'voteRound:' || round_id, initiator_crossplatform_id,
    target_crossplatform_id, 'vote.' || vote_kind, opened_at_utc,
    state, correlation_id, 0
FROM vote_rounds
UNION ALL
SELECT
    'automationExecution', execution_id, NULL, trigger.actor_crossplatform_id,
    'automation.execute', COALESCE(execution.started_utc, trigger.occurred_utc),
    execution.status, execution.correlation_id, 0
FROM automation_executions AS execution
INNER JOIN automation_triggers AS trigger ON trigger.trigger_id = execution.trigger_id
UNION ALL
SELECT
    'integrationOperation', operation_id, actor_subject, target_id,
    action, occurred_utc, status, correlation_id, 0
FROM automation_integration_operation_audit
UNION ALL
SELECT
    'discordDelivery', delivery_id, NULL, target_key,
    'discord.delivery', created_utc, status, NULL, 0
FROM discord_deliveries
UNION ALL
SELECT
    'geoIpDecision', decision_id, NULL, crossplatform_id,
    'geoip.decision', occurred_utc, decision, NULL, 0
FROM geoip_decisions;
