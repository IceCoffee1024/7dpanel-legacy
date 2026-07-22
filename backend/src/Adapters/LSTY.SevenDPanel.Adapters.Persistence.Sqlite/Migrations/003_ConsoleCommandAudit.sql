CREATE TABLE console_command_audit (
    audit_id TEXT NOT NULL PRIMARY KEY,
    raw_command TEXT NOT NULL,
    command_name TEXT NULL,
    source TEXT NOT NULL,
    actor_subject TEXT NULL,
    started_utc INTEGER NOT NULL,
    completed_utc INTEGER NOT NULL CHECK (completed_utc >= started_utc),
    completion_kind TEXT NOT NULL CHECK (completion_kind IN ('Completed', 'Threw')),
    exception_type TEXT NULL,
    CONSTRAINT ck_console_command_audit_exception
        CHECK ((completion_kind = 'Completed' AND exception_type IS NULL) OR
               (completion_kind = 'Threw' AND exception_type IS NOT NULL))
);

CREATE TABLE console_command_audit_argument (
    audit_id TEXT NOT NULL,
    ordinal INTEGER NOT NULL CHECK (ordinal >= 0),
    value TEXT NOT NULL,
    PRIMARY KEY (audit_id, ordinal),
    FOREIGN KEY (audit_id) REFERENCES console_command_audit(audit_id) ON DELETE CASCADE
);

CREATE TABLE console_command_audit_output (
    audit_id TEXT NOT NULL,
    ordinal INTEGER NOT NULL CHECK (ordinal >= 0),
    value TEXT NOT NULL,
    PRIMARY KEY (audit_id, ordinal),
    FOREIGN KEY (audit_id) REFERENCES console_command_audit(audit_id) ON DELETE CASCADE
);

CREATE TABLE console_command_audit_gap (
    gap_id TEXT NOT NULL PRIMARY KEY,
    started_utc INTEGER NOT NULL,
    completed_utc INTEGER NOT NULL CHECK (completed_utc >= started_utc),
    dropped_count INTEGER NOT NULL CHECK (dropped_count > 0),
    reason TEXT NOT NULL
);

CREATE INDEX ix_console_command_audit_started_utc
    ON console_command_audit(started_utc);

CREATE INDEX ix_console_command_audit_gap_started_utc
    ON console_command_audit_gap(started_utc);