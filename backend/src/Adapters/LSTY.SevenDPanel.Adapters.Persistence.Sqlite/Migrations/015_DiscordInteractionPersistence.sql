CREATE TABLE discord_interaction_secrets_015 (
    interaction_id TEXT PRIMARY KEY,
    token_value TEXT NOT NULL
        CHECK (length(token_value) > 0),
    expires_utc INTEGER NOT NULL
        CHECK (expires_utc >= 0)
);

INSERT INTO discord_interaction_secrets_015 (
    interaction_id, token_value, expires_utc)
SELECT interaction_id, token_value, expires_utc
FROM discord_interaction_secrets;

DROP TABLE discord_interaction_secrets;

CREATE TABLE discord_interactions_015 (
    interaction_id TEXT PRIMARY KEY
        CHECK (length(trim(interaction_id)) > 0),
    command_key TEXT NOT NULL
        CHECK (length(trim(command_key)) > 0),
    status TEXT NOT NULL
        CHECK (status IN (
            'Pending', 'Running', 'Succeeded', 'Rejected', 'Failed', 'Expired',
            'ResultUnknown')),
    expires_utc INTEGER NOT NULL
        CHECK (expires_utc >= 0),
    completed_utc INTEGER NULL
        CHECK (completed_utc IS NULL OR completed_utc >= 0),
    guild_id TEXT NULL
        CHECK (guild_id IS NULL OR length(trim(guild_id)) > 0),
    channel_id TEXT NULL
        CHECK (channel_id IS NULL OR length(trim(channel_id)) > 0),
    discord_subject TEXT NULL
        CHECK (discord_subject IS NULL OR length(trim(discord_subject)) > 0),
    binding_code_hash BLOB NULL
        CHECK (binding_code_hash IS NULL OR length(binding_code_hash) > 0)
);

INSERT INTO discord_interactions_015 (
    interaction_id, command_key, status, expires_utc, completed_utc)
SELECT interaction_id, command_key, status, expires_utc, completed_utc
FROM discord_interactions;

DROP TABLE discord_interactions;
ALTER TABLE discord_interactions_015 RENAME TO discord_interactions;

CREATE INDEX ix_discord_interactions_expiry
    ON discord_interactions(status, expires_utc ASC, interaction_id ASC);
CREATE INDEX ix_discord_interactions_claim
    ON discord_interactions(status, expires_utc ASC, interaction_id ASC)
    WHERE discord_subject IS NOT NULL;

CREATE TABLE discord_interaction_secrets (
    interaction_id TEXT PRIMARY KEY,
    token_value TEXT NOT NULL
        CHECK (length(token_value) > 0),
    expires_utc INTEGER NOT NULL
        CHECK (expires_utc >= 0),
    FOREIGN KEY (interaction_id) REFERENCES discord_interactions(interaction_id) ON DELETE CASCADE
);

INSERT INTO discord_interaction_secrets (
    interaction_id, token_value, expires_utc)
SELECT interaction_id, token_value, expires_utc
FROM discord_interaction_secrets_015;

DROP TABLE discord_interaction_secrets_015;
