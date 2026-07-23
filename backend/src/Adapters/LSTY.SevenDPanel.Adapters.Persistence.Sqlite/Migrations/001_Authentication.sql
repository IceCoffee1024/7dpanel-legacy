CREATE TABLE users (
    subject TEXT NOT NULL PRIMARY KEY,
    username TEXT NOT NULL COLLATE BINARY,
    role TEXT NOT NULL CHECK (role IN ('Owner', 'Admin', 'Viewer')),
    password_salt BLOB NOT NULL,
    password_hash BLOB NOT NULL,
    password_iterations INTEGER NOT NULL CHECK (password_iterations = 1000),
    enabled INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0, 1)),
    updated_utc INTEGER NOT NULL
);

CREATE UNIQUE INDEX ux_users_username
    ON users (username);

CREATE TABLE access_tokens (
    token_id TEXT NOT NULL PRIMARY KEY,
    subject TEXT NOT NULL,
    secret_hash BLOB NOT NULL,
    issued_utc INTEGER NOT NULL,
    expires_utc INTEGER NOT NULL,
    CONSTRAINT fk_access_tokens_users
        FOREIGN KEY (subject) REFERENCES users (subject) ON DELETE CASCADE,
    CONSTRAINT ck_access_tokens_lifetime
        CHECK (expires_utc > issued_utc)
);

CREATE INDEX ix_access_tokens_subject
    ON access_tokens (subject);

CREATE INDEX ix_access_tokens_expiration
    ON access_tokens (expires_utc);

CREATE INDEX ix_access_tokens_oldest
    ON access_tokens (issued_utc, token_id);

CREATE TABLE api_keys (
    key_id TEXT NOT NULL PRIMARY KEY,
    subject TEXT NOT NULL,
    name TEXT NOT NULL CHECK (length(name) BETWEEN 1 AND 80),
    secret_hash BLOB NOT NULL,
    created_utc INTEGER NOT NULL,
    last_used_utc INTEGER NULL,
    expires_utc INTEGER NULL,
    revoked_utc INTEGER NULL,
    CONSTRAINT fk_api_keys_users
        FOREIGN KEY (subject) REFERENCES users (subject) ON DELETE CASCADE,
    CONSTRAINT ck_api_keys_expiration
        CHECK (expires_utc IS NULL OR expires_utc > created_utc)
);

CREATE INDEX ix_api_keys_subject_created
    ON api_keys (subject, created_utc DESC, key_id DESC);

CREATE INDEX ix_api_keys_active_subject
    ON api_keys (subject, revoked_utc);
