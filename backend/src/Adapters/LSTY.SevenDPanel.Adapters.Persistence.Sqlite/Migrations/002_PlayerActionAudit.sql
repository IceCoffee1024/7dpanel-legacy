CREATE TABLE player_action_audit (
    operation_id TEXT NOT NULL PRIMARY KEY,
    action_type TEXT NOT NULL CHECK (action_type = 'kick'),
    actor_subject TEXT NOT NULL,
    target_entity_id INTEGER NOT NULL CHECK (target_entity_id >= 0),
    target_name TEXT NULL,
    target_platform_id TEXT NOT NULL,
    target_platform TEXT NOT NULL,
    reason TEXT NOT NULL CHECK (length(reason) BETWEEN 1 AND 200),
    requested_utc INTEGER NOT NULL,
    completed_utc INTEGER NULL,
    status TEXT NOT NULL CHECK (status IN ('Pending', 'Succeeded', 'Failed', 'Unknown')),
    failure_code TEXT NULL,
    CONSTRAINT ck_player_action_audit_completion
        CHECK ((status = 'Pending' AND completed_utc IS NULL) OR
               (status <> 'Pending' AND completed_utc IS NOT NULL))
);