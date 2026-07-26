CREATE TABLE chat_messages (
    id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    sequence INTEGER NOT NULL CHECK (sequence >= 0),
    occurred_utc INTEGER NOT NULL,
    entity_id INTEGER NOT NULL,
    crossplatform_id TEXT NULL,
    sender_name TEXT NOT NULL,
    chat_type TEXT NOT NULL CHECK (chat_type IN ('Global', 'Friends', 'Party', 'Whisper', 'Unknown')),
    source_kind TEXT NOT NULL CHECK (source_kind IN ('Player', 'Administrator', 'System')),
    message TEXT NOT NULL CHECK (length(message) BETWEEN 1 AND 500)
);

CREATE INDEX ix_chat_messages_occurred
    ON chat_messages (occurred_utc DESC, id DESC);
CREATE INDEX ix_chat_messages_crossplatform
    ON chat_messages (crossplatform_id, occurred_utc DESC, id DESC);
CREATE INDEX ix_chat_messages_sender
    ON chat_messages (sender_name, occurred_utc DESC, id DESC);
CREATE INDEX ix_chat_messages_type
    ON chat_messages (chat_type, occurred_utc DESC, id DESC);
CREATE INDEX ix_chat_messages_source
    ON chat_messages (source_kind, occurred_utc DESC, id DESC);

CREATE TABLE chat_history_gaps (
    id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    started_utc INTEGER NOT NULL,
    ended_utc INTEGER NOT NULL CHECK (ended_utc >= started_utc),
    dropped_message_count INTEGER NOT NULL CHECK (dropped_message_count > 0),
    reason TEXT NOT NULL CHECK (length(reason) BETWEEN 1 AND 64)
);

CREATE INDEX ix_chat_history_gaps_range
    ON chat_history_gaps (started_utc, ended_utc, id);

CREATE TABLE chat_settings (
    singleton_id INTEGER NOT NULL PRIMARY KEY CHECK (singleton_id = 1),
    is_enabled INTEGER NOT NULL CHECK (is_enabled IN (0, 1)),
    global_server_name TEXT NULL,
    whisper_server_name TEXT NULL,
    command_prefixes TEXT NOT NULL,
    exclude_commands_from_history INTEGER NOT NULL CHECK (exclude_commands_from_history IN (0, 1)),
    history_retention_days INTEGER NOT NULL CHECK (history_retention_days BETWEEN 0 AND 3650)
);

INSERT INTO chat_settings (
    singleton_id, is_enabled, global_server_name, whisper_server_name,
    command_prefixes, exclude_commands_from_history, history_retention_days)
VALUES (1, 1, NULL, NULL, '/', 1, 30);

CREATE TABLE colored_chat_settings (
    singleton_id INTEGER NOT NULL PRIMARY KEY CHECK (singleton_id = 1),
    is_enabled INTEGER NOT NULL CHECK (is_enabled IN (0, 1)),
    global_default_color TEXT NULL,
    whisper_default_color TEXT NULL,
    friends_default_color TEXT NULL,
    party_default_color TEXT NULL,
    admin_default_color TEXT NULL,
    system_default_color TEXT NULL,
    player_color_tag_permission TEXT NOT NULL CHECK (player_color_tag_permission IN ('None', 'AdminOnly', 'All')),
    CHECK (global_default_color IS NULL OR global_default_color GLOB '[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]'),
    CHECK (whisper_default_color IS NULL OR whisper_default_color GLOB '[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]'),
    CHECK (friends_default_color IS NULL OR friends_default_color GLOB '[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]'),
    CHECK (party_default_color IS NULL OR party_default_color GLOB '[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]'),
    CHECK (admin_default_color IS NULL OR admin_default_color GLOB '[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]'),
    CHECK (system_default_color IS NULL OR system_default_color GLOB '[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]')
);

INSERT INTO colored_chat_settings (
    singleton_id, is_enabled, global_default_color, whisper_default_color,
    friends_default_color, party_default_color, admin_default_color,
    system_default_color, player_color_tag_permission)
VALUES (1, 0, NULL, NULL, NULL, NULL, NULL, NULL, 'None');

CREATE TABLE colored_chat_profiles (
    crossplatform_id TEXT NOT NULL PRIMARY KEY,
    custom_name TEXT NULL,
    name_color TEXT NULL,
    text_color TEXT NULL,
    description TEXT NULL,
    created_utc INTEGER NOT NULL,
    updated_utc INTEGER NOT NULL CHECK (updated_utc >= created_utc),
    CHECK (length(crossplatform_id) > 0),
    CHECK (name_color IS NULL OR name_color GLOB '[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]'),
    CHECK (text_color IS NULL OR text_color GLOB '[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]')
);

CREATE INDEX ix_colored_chat_profiles_updated
    ON colored_chat_profiles (updated_utc DESC, crossplatform_id ASC);
CREATE INDEX ix_colored_chat_profiles_created
    ON colored_chat_profiles (created_utc DESC, crossplatform_id ASC);

CREATE TABLE chat_operation_audit (
    id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    actor_subject TEXT NOT NULL,
    operation TEXT NOT NULL,
    occurred_utc INTEGER NOT NULL,
    result TEXT NOT NULL,
    channel TEXT NULL,
    target_crossplatform_id TEXT NULL,
    message_length INTEGER NULL CHECK (message_length IS NULL OR message_length BETWEEN 0 AND 500),
    business_key TEXT NULL,
    changed_fields TEXT NOT NULL,
    CHECK (length(actor_subject) BETWEEN 1 AND 128),
    CHECK (length(operation) BETWEEN 1 AND 64),
    CHECK (length(result) BETWEEN 1 AND 64)
);

CREATE INDEX ix_chat_operation_audit_occurred
    ON chat_operation_audit (occurred_utc DESC, id DESC);
