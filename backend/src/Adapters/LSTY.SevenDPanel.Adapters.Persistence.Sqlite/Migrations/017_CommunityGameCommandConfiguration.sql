CREATE TABLE community_game_command_configurations (
    configuration_id INTEGER NOT NULL PRIMARY KEY CHECK (configuration_id = 1),
    updated_at_utc INTEGER NOT NULL,
    row_version INTEGER NOT NULL DEFAULT 0 CHECK (row_version >= 0)
);

CREATE TABLE community_game_command_tokens (
    token TEXT NOT NULL COLLATE NOCASE PRIMARY KEY,
    command_id TEXT NOT NULL,
    is_primary INTEGER NOT NULL CHECK (is_primary IN (0, 1)),
    sort_order INTEGER NOT NULL CHECK (sort_order >= 0),
    CHECK (length(trim(token)) > 0),
    CHECK (lower(token) <> 'help'),
    CHECK (instr(token, ' ') = 0 AND instr(token, char(9)) = 0 AND
           instr(token, char(10)) = 0 AND instr(token, char(13)) = 0),
    CHECK (command_id IN (
        'Balance', 'Pay', 'MoneyTop', 'Daily', 'Shop', 'Buy', 'Redeem',
        'Homes', 'SetHome', 'DeleteHome', 'Home', 'Cities', 'City',
        'TeleportAsk', 'TeleportAccept', 'TeleportReject', 'Back',
        'VoteKick', 'VoteRestart')),
    UNIQUE (command_id, is_primary, sort_order)
);

CREATE UNIQUE INDEX ux_community_game_command_primary
    ON community_game_command_tokens (command_id)
    WHERE is_primary = 1;

INSERT INTO community_game_command_configurations (
    configuration_id, updated_at_utc, row_version)
VALUES (1, CAST(strftime('%s', 'now') AS INTEGER) * 1000, 0);

INSERT INTO community_game_command_tokens (token, command_id, is_primary, sort_order) VALUES
    ('bal', 'Balance', 1, 0),
    ('balance', 'Balance', 0, 0),
    ('money', 'Balance', 0, 1),
    ('pay', 'Pay', 1, 0),
    ('transfer', 'Pay', 0, 0),
    ('send', 'Pay', 0, 1),
    ('moneytop', 'MoneyTop', 1, 0),
    ('baltop', 'MoneyTop', 0, 0),
    ('ecotop', 'MoneyTop', 0, 1),
    ('daily', 'Daily', 1, 0),
    ('claim', 'Daily', 0, 0),
    ('shop', 'Shop', 1, 0),
    ('buy', 'Buy', 1, 0),
    ('redeem', 'Redeem', 1, 0),
    ('cities', 'Cities', 1, 0),
    ('city', 'City', 1, 0),
    ('tpa', 'TeleportAsk', 1, 0),
    ('tpaccept', 'TeleportAccept', 1, 0),
    ('tpreject', 'TeleportReject', 1, 0),
    ('back', 'Back', 1, 0),
    ('votekick', 'VoteKick', 1, 0),
    ('voterestart', 'VoteRestart', 1, 0);

INSERT INTO community_game_command_tokens (token, command_id, is_primary, sort_order)
SELECT list_command_name, 'Homes', 1, 0
FROM teleport_settings WHERE teleport_kind = 'Home';

INSERT INTO community_game_command_tokens (token, command_id, is_primary, sort_order)
SELECT set_command_name, 'SetHome', 1, 0
FROM teleport_settings WHERE teleport_kind = 'Home';

INSERT INTO community_game_command_tokens (token, command_id, is_primary, sort_order)
SELECT delete_command_name, 'DeleteHome', 1, 0
FROM teleport_settings WHERE teleport_kind = 'Home';

INSERT INTO community_game_command_tokens (token, command_id, is_primary, sort_order)
SELECT teleport_command_name, 'Home', 1, 0
FROM teleport_settings WHERE teleport_kind = 'Home';
