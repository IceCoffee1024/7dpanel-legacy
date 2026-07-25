CREATE TABLE player_history_players (
    crossplatform_id TEXT NOT NULL PRIMARY KEY,
    latest_name TEXT NOT NULL,
    first_observed_utc INTEGER NOT NULL,
    last_observed_utc INTEGER NOT NULL,
    latest_snapshot_id INTEGER NOT NULL,
    total_observation_count INTEGER NOT NULL CHECK (total_observation_count > 0),
    retained_snapshot_count INTEGER NOT NULL CHECK (retained_snapshot_count > 0),
    compacted_snapshot_count INTEGER NOT NULL CHECK (compacted_snapshot_count >= 0),
    CHECK (last_observed_utc >= first_observed_utc),
    CHECK (total_observation_count = retained_snapshot_count + compacted_snapshot_count)
);

CREATE TABLE player_history_snapshots (
    snapshot_id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    crossplatform_id TEXT NOT NULL,
    observed_utc INTEGER NOT NULL,
    entity_id INTEGER NOT NULL CHECK (entity_id >= 0),
    name TEXT NOT NULL,
    platform_combined_id TEXT NOT NULL,
    platform_name TEXT NOT NULL,
    crossplatform_combined_id TEXT NOT NULL,
    crossplatform_name TEXT NOT NULL,
    device_type TEXT NOT NULL,
    ip TEXT NULL,
    ping INTEGER NOT NULL,
    compatibility_version TEXT NULL,
    discord_user_id TEXT NULL,
    permission_level INTEGER NOT NULL,
    position_x REAL NOT NULL,
    position_y REAL NOT NULL,
    position_z REAL NOT NULL,
    is_dead INTEGER NOT NULL CHECK (is_dead IN (0, 1)),
    health INTEGER NOT NULL,
    max_health INTEGER NOT NULL,
    level INTEGER NOT NULL,
    play_group TEXT NULL,
    last_login_utc INTEGER NULL,
    game_stage INTEGER NULL CHECK (game_stage IS NULL OR game_stage >= 0),
    exp_to_next_level INTEGER NULL CHECK (exp_to_next_level IS NULL OR exp_to_next_level >= 0),
    skill_points INTEGER NULL CHECK (skill_points IS NULL OR skill_points >= 0),
    bedroll_x REAL NULL,
    bedroll_y REAL NULL,
    bedroll_z REAL NULL,
    score INTEGER NOT NULL,
    zombie_kills INTEGER NOT NULL,
    player_kills INTEGER NOT NULL,
    deaths INTEGER NOT NULL,
    total_time_played_minutes REAL NOT NULL CHECK (total_time_played_minutes >= 0),
    distance_walked_meters REAL NOT NULL CHECK (distance_walked_meters >= 0),
    total_items_crafted INTEGER NOT NULL,
    longest_life_minutes REAL NOT NULL CHECK (longest_life_minutes >= 0),
    current_life_minutes REAL NOT NULL CHECK (current_life_minutes >= 0),
    CHECK (crossplatform_combined_id = crossplatform_id),
    CHECK ((bedroll_x IS NULL AND bedroll_y IS NULL AND bedroll_z IS NULL) OR
           (bedroll_x IS NOT NULL AND bedroll_y IS NOT NULL AND bedroll_z IS NOT NULL)),
    FOREIGN KEY (crossplatform_id) REFERENCES player_history_players(crossplatform_id)
        DEFERRABLE INITIALLY DEFERRED
);

CREATE TABLE player_history_gaps (
    gap_id TEXT NOT NULL PRIMARY KEY,
    crossplatform_id TEXT NOT NULL,
    started_utc INTEGER NOT NULL,
    completed_utc INTEGER NOT NULL CHECK (completed_utc >= started_utc),
    dropped_count INTEGER NOT NULL CHECK (dropped_count > 0),
    reason TEXT NOT NULL CHECK (reason IN ('queue_full', 'store_failure', 'shutdown_timeout')),
    recorded_utc INTEGER NOT NULL
);

CREATE INDEX ix_player_history_snapshots_player_id
    ON player_history_snapshots(crossplatform_id, snapshot_id DESC);

CREATE INDEX ix_player_history_snapshots_player_time
    ON player_history_snapshots(crossplatform_id, observed_utc DESC, snapshot_id DESC);

CREATE INDEX ix_player_history_players_first_observed
    ON player_history_players(first_observed_utc DESC, crossplatform_id ASC);

CREATE INDEX ix_player_history_gaps_player_range
    ON player_history_gaps(crossplatform_id, started_utc, completed_utc);
