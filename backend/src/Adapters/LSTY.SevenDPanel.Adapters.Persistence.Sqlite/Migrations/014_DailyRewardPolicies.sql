CREATE TABLE daily_reward_policies (
    rule_id TEXT PRIMARY KEY
        CHECK (length(trim(rule_id)) > 0),
    reward_package_id TEXT NOT NULL
        CHECK (length(trim(reward_package_id)) > 0),
    enabled INTEGER NOT NULL DEFAULT 1
        CHECK (enabled IN (0, 1)),
    created_at_utc INTEGER NOT NULL
        CHECK (created_at_utc >= 0),
    updated_at_utc INTEGER NOT NULL
        CHECK (updated_at_utc >= created_at_utc),
    row_version INTEGER NOT NULL DEFAULT 0
        CHECK (row_version >= 0),
    FOREIGN KEY (reward_package_id) REFERENCES reward_packages(package_id) ON DELETE RESTRICT
);

CREATE INDEX ix_daily_reward_policies_enabled
    ON daily_reward_policies(enabled, rule_id);
