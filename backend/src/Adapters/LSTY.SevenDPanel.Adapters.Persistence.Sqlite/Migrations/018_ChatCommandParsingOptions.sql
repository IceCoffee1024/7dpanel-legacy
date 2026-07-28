ALTER TABLE chat_settings
    ADD COLUMN allow_no_prefix INTEGER NOT NULL DEFAULT 0
    CHECK (allow_no_prefix IN (0, 1));

ALTER TABLE chat_settings
    ADD COLUMN command_parameter_separator TEXT NOT NULL DEFAULT ' '
    CHECK (length(command_parameter_separator) = 1);

ALTER TABLE chat_settings
    ADD COLUMN hide_registered_command_global_messages INTEGER NOT NULL DEFAULT 1
    CHECK (hide_registered_command_global_messages IN (0, 1));
