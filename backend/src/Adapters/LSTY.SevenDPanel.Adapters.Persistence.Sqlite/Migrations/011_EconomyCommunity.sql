CREATE TABLE economy_accounts (
    account_id TEXT PRIMARY KEY
        CHECK (length(trim(account_id)) > 0),
    account_kind TEXT NOT NULL
        CHECK (account_kind IN ('Player', 'System')),
    crossplatform_id TEXT NULL,
    enabled INTEGER NOT NULL DEFAULT 1
        CHECK (enabled IN (0, 1)),
    is_frozen INTEGER NOT NULL DEFAULT 0
        CHECK (is_frozen IN (0, 1)),
    posted_balance INTEGER NOT NULL DEFAULT 0,
    reserved_debit INTEGER NOT NULL DEFAULT 0
        CHECK (reserved_debit >= 0),
    created_at_utc INTEGER NOT NULL
        CHECK (created_at_utc >= 0),
    updated_at_utc INTEGER NOT NULL
        CHECK (updated_at_utc >= created_at_utc),
    row_version INTEGER NOT NULL DEFAULT 0
        CHECK (row_version >= 0),
    CHECK (
        (account_kind = 'Player' AND crossplatform_id IS NOT NULL
            AND length(trim(crossplatform_id)) > 0
            AND posted_balance >= reserved_debit)
        OR
        (account_kind = 'System' AND crossplatform_id IS NULL))
);

CREATE UNIQUE INDEX ux_economy_accounts_crossplatform
    ON economy_accounts(crossplatform_id)
    WHERE crossplatform_id IS NOT NULL;
CREATE INDEX ix_economy_accounts_balance_keyset
    ON economy_accounts(posted_balance DESC, account_id ASC);

CREATE TABLE economy_transactions (
    transaction_id TEXT PRIMARY KEY
        CHECK (length(trim(transaction_id)) > 0),
    transaction_type TEXT NOT NULL
        CHECK (length(trim(transaction_type)) > 0),
    idempotency_key TEXT NOT NULL
        CHECK (length(trim(idempotency_key)) > 0),
    occurred_utc INTEGER NOT NULL
        CHECK (occurred_utc >= 0),
    actor_kind TEXT NOT NULL
        CHECK (length(trim(actor_kind)) > 0),
    actor_id TEXT NOT NULL
        CHECK (length(trim(actor_id)) > 0),
    related_crossplatform_id TEXT NULL,
    business_kind TEXT NULL,
    business_id TEXT NULL,
    correlation_id TEXT NULL,
    reason TEXT NULL,
    status TEXT NOT NULL
        CHECK (status IN ('Committed', 'Reversed')),
    CHECK ((business_kind IS NULL AND business_id IS NULL) OR
           (business_kind IS NOT NULL AND length(trim(business_kind)) > 0 AND
            business_id IS NOT NULL AND length(trim(business_id)) > 0))
);

CREATE UNIQUE INDEX ux_economy_transaction_idempotency
    ON economy_transactions(idempotency_key);
CREATE INDEX ix_economy_transactions_occurred_keyset
    ON economy_transactions(occurred_utc DESC, transaction_id DESC);
CREATE INDEX ix_economy_transactions_player_occurred
    ON economy_transactions(related_crossplatform_id, occurred_utc DESC, transaction_id DESC);

CREATE TABLE economy_entries (
    entry_id TEXT PRIMARY KEY
        CHECK (length(trim(entry_id)) > 0),
    transaction_id TEXT NOT NULL,
    account_id TEXT NOT NULL,
    ordinal INTEGER NOT NULL
        CHECK (ordinal >= 0),
    side TEXT NOT NULL
        CHECK (side IN ('Debit', 'Credit')),
    amount INTEGER NOT NULL
        CHECK (amount >= 0),
    balance_after INTEGER NOT NULL,
    FOREIGN KEY (transaction_id) REFERENCES economy_transactions(transaction_id) ON DELETE RESTRICT,
    FOREIGN KEY (account_id) REFERENCES economy_accounts(account_id) ON DELETE RESTRICT,
    UNIQUE (transaction_id, ordinal)
);

CREATE INDEX ix_economy_entries_account_transaction
    ON economy_entries(account_id, transaction_id);

CREATE TABLE economy_reservations (
    reservation_id TEXT PRIMARY KEY
        CHECK (length(trim(reservation_id)) > 0),
    account_id TEXT NOT NULL,
    amount INTEGER NOT NULL
        CHECK (amount >= 0),
    state TEXT NOT NULL
        CHECK (state IN ('Reserved', 'Captured', 'Released')),
    idempotency_key TEXT NOT NULL
        CHECK (length(trim(idempotency_key)) > 0),
    business_kind TEXT NOT NULL
        CHECK (length(trim(business_kind)) > 0),
    business_id TEXT NOT NULL
        CHECK (length(trim(business_id)) > 0),
    captured_transaction_id TEXT NULL,
    created_at_utc INTEGER NOT NULL
        CHECK (created_at_utc >= 0),
    updated_at_utc INTEGER NOT NULL
        CHECK (updated_at_utc >= created_at_utc),
    expires_at_utc INTEGER NULL
        CHECK (expires_at_utc IS NULL OR expires_at_utc >= created_at_utc),
    row_version INTEGER NOT NULL DEFAULT 0
        CHECK (row_version >= 0),
    CHECK ((state = 'Captured' AND captured_transaction_id IS NOT NULL) OR
           (state IN ('Reserved', 'Released') AND captured_transaction_id IS NULL)),
    FOREIGN KEY (account_id) REFERENCES economy_accounts(account_id) ON DELETE RESTRICT,
    FOREIGN KEY (captured_transaction_id) REFERENCES economy_transactions(transaction_id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX ux_economy_reservation_idempotency
    ON economy_reservations(idempotency_key);
CREATE INDEX ix_economy_reservations_account_state
    ON economy_reservations(account_id, state, created_at_utc DESC);
CREATE INDEX ix_economy_reservations_business
    ON economy_reservations(business_kind, business_id);

CREATE TABLE reward_packages (
    package_id TEXT PRIMARY KEY
        CHECK (length(trim(package_id)) > 0),
    name TEXT NOT NULL COLLATE NOCASE
        CHECK (length(trim(name)) > 0),
    description TEXT NOT NULL DEFAULT '',
    enabled INTEGER NOT NULL DEFAULT 1
        CHECK (enabled IN (0, 1)),
    sort_order INTEGER NOT NULL DEFAULT 0,
    created_at_utc INTEGER NOT NULL
        CHECK (created_at_utc >= 0),
    updated_at_utc INTEGER NOT NULL
        CHECK (updated_at_utc >= created_at_utc),
    row_version INTEGER NOT NULL DEFAULT 0
        CHECK (row_version >= 0)
);

CREATE UNIQUE INDEX ux_reward_packages_name ON reward_packages(name);
CREATE INDEX ix_reward_packages_enabled_sort
    ON reward_packages(enabled DESC, sort_order ASC, package_id ASC);

CREATE TABLE reward_package_entries (
    entry_id TEXT PRIMARY KEY
        CHECK (length(trim(entry_id)) > 0),
    package_id TEXT NOT NULL,
    ordinal INTEGER NOT NULL
        CHECK (ordinal >= 0),
    entry_kind TEXT NOT NULL
        CHECK (entry_kind IN ('Item', 'Currency', 'RegisteredAction')),
    item_internal_name TEXT NULL,
    item_kind TEXT NULL,
    quantity INTEGER NULL,
    min_quality INTEGER NULL,
    max_quality INTEGER NULL,
    catalog_version TEXT NULL,
    currency_amount INTEGER NULL,
    registered_action TEXT NULL,
    CHECK (
        (entry_kind = 'Item'
            AND item_internal_name IS NOT NULL AND length(trim(item_internal_name)) > 0
            AND item_kind IN ('Item', 'Block')
            AND quantity IS NOT NULL AND quantity > 0
            AND catalog_version IS NOT NULL AND length(trim(catalog_version)) > 0
            AND ((min_quality IS NULL AND max_quality IS NULL) OR
                 (min_quality IS NOT NULL AND max_quality IS NOT NULL
                    AND min_quality >= 0 AND max_quality >= min_quality))
            AND currency_amount IS NULL AND registered_action IS NULL)
        OR
        (entry_kind = 'Currency'
            AND currency_amount IS NOT NULL AND currency_amount >= 0
            AND item_internal_name IS NULL AND item_kind IS NULL AND quantity IS NULL
            AND min_quality IS NULL AND max_quality IS NULL AND catalog_version IS NULL
            AND registered_action IS NULL)
        OR
        (entry_kind = 'RegisteredAction'
            AND registered_action = 'ResetSkills'
            AND item_internal_name IS NULL AND item_kind IS NULL AND quantity IS NULL
            AND min_quality IS NULL AND max_quality IS NULL AND catalog_version IS NULL
            AND currency_amount IS NULL)),
    FOREIGN KEY (package_id) REFERENCES reward_packages(package_id) ON DELETE CASCADE,
    UNIQUE (package_id, ordinal)
);

CREATE TABLE grant_operations (
    operation_id TEXT PRIMARY KEY
        CHECK (length(trim(operation_id)) > 0),
    package_id TEXT NOT NULL,
    crossplatform_id TEXT NOT NULL
        CHECK (length(trim(crossplatform_id)) > 0),
    expected_entity_id INTEGER NOT NULL
        CHECK (expected_entity_id >= 0),
    expected_world_id TEXT NOT NULL
        CHECK (length(trim(expected_world_id)) > 0),
    state TEXT NOT NULL
        CHECK (state IN (
            'Reserved', 'Dispatching', 'PendingReconciliation', 'Completed',
            'Failed', 'Refunded', 'Compensated')),
    idempotency_key TEXT NOT NULL
        CHECK (length(trim(idempotency_key)) > 0),
    eligibility_key TEXT NULL,
    source_kind TEXT NULL,
    source_id TEXT NULL,
    actor_kind TEXT NOT NULL
        CHECK (length(trim(actor_kind)) > 0),
    actor_id TEXT NOT NULL
        CHECK (length(trim(actor_id)) > 0),
    reservation_id TEXT NULL,
    compensates_operation_id TEXT NULL,
    correlation_id TEXT NULL,
    error_code TEXT NULL,
    created_at_utc INTEGER NOT NULL
        CHECK (created_at_utc >= 0),
    updated_at_utc INTEGER NOT NULL
        CHECK (updated_at_utc >= created_at_utc),
    completed_at_utc INTEGER NULL
        CHECK (completed_at_utc IS NULL OR completed_at_utc >= created_at_utc),
    reconciled_at_utc INTEGER NULL
        CHECK (reconciled_at_utc IS NULL OR reconciled_at_utc >= created_at_utc),
    reconciled_by TEXT NULL,
    row_version INTEGER NOT NULL DEFAULT 0
        CHECK (row_version >= 0),
    CHECK ((source_kind IS NULL AND source_id IS NULL) OR
           (source_kind IS NOT NULL AND length(trim(source_kind)) > 0 AND
            source_id IS NOT NULL AND length(trim(source_id)) > 0)),
    FOREIGN KEY (package_id) REFERENCES reward_packages(package_id) ON DELETE RESTRICT,
    FOREIGN KEY (reservation_id) REFERENCES economy_reservations(reservation_id) ON DELETE RESTRICT,
    FOREIGN KEY (compensates_operation_id) REFERENCES grant_operations(operation_id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX ux_grant_operation_idempotency
    ON grant_operations(idempotency_key);
CREATE UNIQUE INDEX ux_grant_operation_eligibility
    ON grant_operations(source_kind, source_id, crossplatform_id, eligibility_key)
    WHERE eligibility_key IS NOT NULL;
CREATE INDEX ix_grant_operations_created_keyset
    ON grant_operations(created_at_utc DESC, operation_id DESC);
CREATE INDEX ix_grant_operations_state_updated
    ON grant_operations(state, updated_at_utc ASC, operation_id ASC);

CREATE TABLE grant_operation_entries (
    operation_entry_id TEXT PRIMARY KEY
        CHECK (length(trim(operation_entry_id)) > 0),
    operation_id TEXT NOT NULL,
    package_entry_id TEXT NOT NULL,
    ordinal INTEGER NOT NULL
        CHECK (ordinal >= 0),
    entry_kind TEXT NOT NULL
        CHECK (entry_kind IN ('Item', 'Currency', 'RegisteredAction')),
    state TEXT NOT NULL
        CHECK (state IN (
            'Reserved', 'Dispatching', 'PendingReconciliation', 'Completed',
            'Failed', 'Refunded', 'Compensated')),
    delivery_operation_id TEXT NULL,
    ledger_transaction_id TEXT NULL,
    error_code TEXT NULL,
    updated_at_utc INTEGER NOT NULL
        CHECK (updated_at_utc >= 0),
    row_version INTEGER NOT NULL DEFAULT 0
        CHECK (row_version >= 0),
    FOREIGN KEY (operation_id) REFERENCES grant_operations(operation_id) ON DELETE CASCADE,
    FOREIGN KEY (package_entry_id) REFERENCES reward_package_entries(entry_id) ON DELETE RESTRICT,
    FOREIGN KEY (ledger_transaction_id) REFERENCES economy_transactions(transaction_id) ON DELETE RESTRICT,
    UNIQUE (operation_id, ordinal)
);

CREATE TABLE shop_products (
    product_id TEXT PRIMARY KEY
        CHECK (length(trim(product_id)) > 0),
    name TEXT NOT NULL COLLATE NOCASE
        CHECK (length(trim(name)) > 0),
    description TEXT NOT NULL DEFAULT '',
    enabled INTEGER NOT NULL DEFAULT 1
        CHECK (enabled IN (0, 1)),
    price_amount INTEGER NOT NULL
        CHECK (price_amount >= 0),
    stock_remaining INTEGER NULL
        CHECK (stock_remaining IS NULL OR stock_remaining >= 0),
    per_player_limit INTEGER NULL
        CHECK (per_player_limit IS NULL OR per_player_limit > 0),
    reward_package_id TEXT NOT NULL,
    sort_order INTEGER NOT NULL DEFAULT 0,
    created_at_utc INTEGER NOT NULL
        CHECK (created_at_utc >= 0),
    updated_at_utc INTEGER NOT NULL
        CHECK (updated_at_utc >= created_at_utc),
    row_version INTEGER NOT NULL DEFAULT 0
        CHECK (row_version >= 0),
    FOREIGN KEY (reward_package_id) REFERENCES reward_packages(package_id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX ux_shop_products_name ON shop_products(name);
CREATE INDEX ix_shop_products_enabled_sort
    ON shop_products(enabled DESC, sort_order ASC, product_id ASC);

CREATE TABLE shop_purchases (
    purchase_id TEXT PRIMARY KEY
        CHECK (length(trim(purchase_id)) > 0),
    product_id TEXT NOT NULL,
    crossplatform_id TEXT NOT NULL
        CHECK (length(trim(crossplatform_id)) > 0),
    quantity INTEGER NOT NULL
        CHECK (quantity > 0),
    unit_price INTEGER NOT NULL
        CHECK (unit_price >= 0),
    total_amount INTEGER NOT NULL
        CHECK (total_amount >= 0),
    state TEXT NOT NULL
        CHECK (state IN (
            'Reserved', 'Dispatching', 'PendingReconciliation',
            'Completed', 'Failed', 'Refunded')),
    idempotency_key TEXT NOT NULL
        CHECK (length(trim(idempotency_key)) > 0),
    reservation_id TEXT NULL,
    captured_transaction_id TEXT NULL,
    grant_operation_id TEXT NULL,
    correlation_id TEXT NULL,
    error_code TEXT NULL,
    created_at_utc INTEGER NOT NULL
        CHECK (created_at_utc >= 0),
    updated_at_utc INTEGER NOT NULL
        CHECK (updated_at_utc >= created_at_utc),
    completed_at_utc INTEGER NULL
        CHECK (completed_at_utc IS NULL OR completed_at_utc >= created_at_utc),
    row_version INTEGER NOT NULL DEFAULT 0
        CHECK (row_version >= 0),
    FOREIGN KEY (product_id) REFERENCES shop_products(product_id) ON DELETE RESTRICT,
    FOREIGN KEY (reservation_id) REFERENCES economy_reservations(reservation_id) ON DELETE RESTRICT,
    FOREIGN KEY (captured_transaction_id) REFERENCES economy_transactions(transaction_id) ON DELETE RESTRICT,
    FOREIGN KEY (grant_operation_id) REFERENCES grant_operations(operation_id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX ux_shop_purchase_idempotency
    ON shop_purchases(idempotency_key);
CREATE INDEX ix_shop_purchases_created_keyset
    ON shop_purchases(created_at_utc DESC, purchase_id DESC);
CREATE INDEX ix_shop_purchases_player_product
    ON shop_purchases(crossplatform_id, product_id, created_at_utc DESC);

CREATE TABLE redeem_codes (
    code_id TEXT PRIMARY KEY
        CHECK (length(trim(code_id)) > 0),
    normalized_code_digest TEXT NOT NULL
        CHECK (length(normalized_code_digest) = 64 AND
               normalized_code_digest NOT GLOB '*[^0-9A-Fa-f]*'),
    masked_prefix TEXT NOT NULL
        CHECK (length(masked_prefix) = 4),
    normalization_version INTEGER NOT NULL
        CHECK (normalization_version > 0),
    reward_package_id TEXT NOT NULL,
    enabled INTEGER NOT NULL DEFAULT 1
        CHECK (enabled IN (0, 1)),
    valid_from_utc INTEGER NULL
        CHECK (valid_from_utc IS NULL OR valid_from_utc >= 0),
    expires_at_utc INTEGER NULL
        CHECK (expires_at_utc IS NULL OR expires_at_utc >= 0),
    max_redemptions INTEGER NULL
        CHECK (max_redemptions IS NULL OR max_redemptions > 0),
    per_player_limit INTEGER NULL
        CHECK (per_player_limit IS NULL OR per_player_limit > 0),
    redemption_count INTEGER NOT NULL DEFAULT 0
        CHECK (redemption_count >= 0),
    created_at_utc INTEGER NOT NULL
        CHECK (created_at_utc >= 0),
    updated_at_utc INTEGER NOT NULL
        CHECK (updated_at_utc >= created_at_utc),
    row_version INTEGER NOT NULL DEFAULT 0
        CHECK (row_version >= 0),
    CHECK (valid_from_utc IS NULL OR expires_at_utc IS NULL OR expires_at_utc > valid_from_utc),
    CHECK (max_redemptions IS NULL OR redemption_count <= max_redemptions),
    FOREIGN KEY (reward_package_id) REFERENCES reward_packages(package_id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX ux_redeem_code_digest
    ON redeem_codes(normalization_version, normalized_code_digest);
CREATE INDEX ix_redeem_codes_enabled_expiry
    ON redeem_codes(enabled, expires_at_utc ASC, code_id ASC);

CREATE TABLE redeem_attempts (
    attempt_id TEXT PRIMARY KEY
        CHECK (length(trim(attempt_id)) > 0),
    code_id TEXT NOT NULL,
    crossplatform_id TEXT NOT NULL
        CHECK (length(trim(crossplatform_id)) > 0),
    normalized_code_digest TEXT NOT NULL
        CHECK (length(normalized_code_digest) = 64 AND
               normalized_code_digest NOT GLOB '*[^0-9A-Fa-f]*'),
    result TEXT NOT NULL
        CHECK (result IN ('Pending', 'Succeeded', 'Rejected', 'PendingReconciliation', 'Failed')),
    result_code TEXT NULL,
    grant_operation_id TEXT NULL,
    correlation_id TEXT NULL,
    attempted_at_utc INTEGER NOT NULL
        CHECK (attempted_at_utc >= 0),
    FOREIGN KEY (code_id) REFERENCES redeem_codes(code_id) ON DELETE RESTRICT,
    FOREIGN KEY (grant_operation_id) REFERENCES grant_operations(operation_id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX ux_redeem_player
    ON redeem_attempts(code_id, crossplatform_id, normalized_code_digest);
CREATE INDEX ix_redeem_attempts_attempted_keyset
    ON redeem_attempts(attempted_at_utc DESC, attempt_id DESC);

CREATE TABLE achievement_definitions (
    achievement_id TEXT PRIMARY KEY
        CHECK (length(trim(achievement_id)) > 0),
    name TEXT NOT NULL COLLATE NOCASE
        CHECK (length(trim(name)) > 0),
    description TEXT NOT NULL DEFAULT '',
    statistic_key TEXT NOT NULL
        CHECK (statistic_key IN ('Level', 'ZombieKills', 'PlayerKills', 'Deaths')),
    threshold_value INTEGER NOT NULL
        CHECK (threshold_value >= 0),
    reward_package_id TEXT NOT NULL,
    enabled INTEGER NOT NULL DEFAULT 1
        CHECK (enabled IN (0, 1)),
    sort_order INTEGER NOT NULL DEFAULT 0,
    created_at_utc INTEGER NOT NULL
        CHECK (created_at_utc >= 0),
    updated_at_utc INTEGER NOT NULL
        CHECK (updated_at_utc >= created_at_utc),
    row_version INTEGER NOT NULL DEFAULT 0
        CHECK (row_version >= 0),
    FOREIGN KEY (reward_package_id) REFERENCES reward_packages(package_id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX ux_achievement_definitions_name ON achievement_definitions(name);
CREATE INDEX ix_achievement_definitions_enabled_sort
    ON achievement_definitions(enabled DESC, sort_order ASC, achievement_id ASC);

CREATE TABLE achievement_progress (
    achievement_id TEXT NOT NULL,
    crossplatform_id TEXT NOT NULL
        CHECK (length(trim(crossplatform_id)) > 0),
    current_value INTEGER NOT NULL DEFAULT 0
        CHECK (current_value >= 0),
    eligibility_key TEXT NULL,
    grant_operation_id TEXT NULL,
    completed_at_utc INTEGER NULL
        CHECK (completed_at_utc IS NULL OR completed_at_utc >= 0),
    updated_at_utc INTEGER NOT NULL
        CHECK (updated_at_utc >= 0),
    row_version INTEGER NOT NULL DEFAULT 0
        CHECK (row_version >= 0),
    PRIMARY KEY (achievement_id, crossplatform_id),
    FOREIGN KEY (achievement_id) REFERENCES achievement_definitions(achievement_id) ON DELETE CASCADE,
    FOREIGN KEY (grant_operation_id) REFERENCES grant_operations(operation_id) ON DELETE RESTRICT
);

CREATE INDEX ix_achievement_progress_player
    ON achievement_progress(crossplatform_id, updated_at_utc DESC);

CREATE TABLE online_reward_rules (
    rule_id TEXT PRIMARY KEY
        CHECK (length(trim(rule_id)) > 0),
    name TEXT NOT NULL COLLATE NOCASE
        CHECK (length(trim(name)) > 0),
    required_online_ms INTEGER NOT NULL
        CHECK (required_online_ms > 0),
    repeat_interval_ms INTEGER NULL
        CHECK (repeat_interval_ms IS NULL OR repeat_interval_ms > 0),
    evidence_gap_policy TEXT NOT NULL
        CHECK (evidence_gap_policy IN ('Paused', 'Incomplete')),
    reward_package_id TEXT NOT NULL,
    enabled INTEGER NOT NULL DEFAULT 1
        CHECK (enabled IN (0, 1)),
    sort_order INTEGER NOT NULL DEFAULT 0,
    created_at_utc INTEGER NOT NULL
        CHECK (created_at_utc >= 0),
    updated_at_utc INTEGER NOT NULL
        CHECK (updated_at_utc >= created_at_utc),
    row_version INTEGER NOT NULL DEFAULT 0
        CHECK (row_version >= 0),
    FOREIGN KEY (reward_package_id) REFERENCES reward_packages(package_id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX ux_online_reward_rules_name ON online_reward_rules(name);
CREATE INDEX ix_online_reward_rules_enabled_sort
    ON online_reward_rules(enabled DESC, sort_order ASC, rule_id ASC);

CREATE TABLE reward_eligibilities (
    eligibility_id TEXT PRIMARY KEY
        CHECK (length(trim(eligibility_id)) > 0),
    rule_kind TEXT NOT NULL
        CHECK (rule_kind IN ('Achievement', 'OnlineReward', 'Redeem', 'Purchase', 'Manual')),
    rule_id TEXT NOT NULL
        CHECK (length(trim(rule_id)) > 0),
    crossplatform_id TEXT NOT NULL
        CHECK (length(trim(crossplatform_id)) > 0),
    eligibility_key TEXT NOT NULL
        CHECK (length(trim(eligibility_key)) > 0),
    state TEXT NOT NULL
        CHECK (state IN (
            'Eligible', 'GrantReserved', 'Granted', 'Paused', 'Incomplete',
            'PendingReconciliation', 'Failed')),
    grant_operation_id TEXT NULL,
    correlation_id TEXT NULL,
    evidence_from_utc INTEGER NULL
        CHECK (evidence_from_utc IS NULL OR evidence_from_utc >= 0),
    evidence_to_utc INTEGER NULL
        CHECK (evidence_to_utc IS NULL OR evidence_to_utc >= 0),
    created_at_utc INTEGER NOT NULL
        CHECK (created_at_utc >= 0),
    updated_at_utc INTEGER NOT NULL
        CHECK (updated_at_utc >= created_at_utc),
    row_version INTEGER NOT NULL DEFAULT 0
        CHECK (row_version >= 0),
    CHECK (evidence_from_utc IS NULL OR evidence_to_utc IS NULL OR evidence_to_utc >= evidence_from_utc),
    FOREIGN KEY (grant_operation_id) REFERENCES grant_operations(operation_id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX ux_reward_eligibility
    ON reward_eligibilities(rule_kind, rule_id, crossplatform_id, eligibility_key);
CREATE INDEX ix_reward_eligibilities_state_updated
    ON reward_eligibilities(state, updated_at_utc ASC, eligibility_id ASC);

CREATE TABLE daily_reward_claims (
    claim_id TEXT PRIMARY KEY
        CHECK (length(trim(claim_id)) > 0),
    rule_id TEXT NOT NULL
        CHECK (length(trim(rule_id)) > 0),
    reward_package_id TEXT NOT NULL,
    crossplatform_id TEXT NOT NULL
        CHECK (length(trim(crossplatform_id)) > 0),
    period_key TEXT NOT NULL
        CHECK (length(trim(period_key)) > 0),
    period_start_utc INTEGER NOT NULL
        CHECK (period_start_utc >= 0),
    period_end_utc INTEGER NOT NULL
        CHECK (period_end_utc > period_start_utc),
    state TEXT NOT NULL
        CHECK (state IN (
            'Reserved', 'Dispatching', 'PendingReconciliation',
            'Completed', 'Failed')),
    idempotency_key TEXT NOT NULL
        CHECK (length(trim(idempotency_key)) > 0),
    expected_entity_id INTEGER NOT NULL
        CHECK (expected_entity_id >= 0),
    expected_world_id TEXT NOT NULL
        CHECK (length(trim(expected_world_id)) > 0),
    grant_operation_id TEXT NULL,
    correlation_id TEXT NULL,
    error_code TEXT NULL,
    created_at_utc INTEGER NOT NULL
        CHECK (created_at_utc >= 0),
    updated_at_utc INTEGER NOT NULL
        CHECK (updated_at_utc >= created_at_utc),
    completed_at_utc INTEGER NULL
        CHECK (completed_at_utc IS NULL OR completed_at_utc >= created_at_utc),
    row_version INTEGER NOT NULL DEFAULT 0
        CHECK (row_version >= 0),
    FOREIGN KEY (reward_package_id) REFERENCES reward_packages(package_id) ON DELETE RESTRICT,
    FOREIGN KEY (grant_operation_id) REFERENCES grant_operations(operation_id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX ux_daily_reward_claim_period
    ON daily_reward_claims(rule_id, crossplatform_id, period_key);
CREATE UNIQUE INDEX ux_daily_reward_claim_idempotency
    ON daily_reward_claims(idempotency_key);
CREATE INDEX ix_daily_reward_claims_state_updated
    ON daily_reward_claims(state, updated_at_utc ASC, claim_id ASC);

CREATE TABLE teleport_settings (
    teleport_kind TEXT PRIMARY KEY
        CHECK (teleport_kind IN ('Home', 'City', 'Friend', 'Return', 'Admin')),
    enabled INTEGER NOT NULL DEFAULT 1
        CHECK (enabled IN (0, 1)),
    max_homes INTEGER NULL
        CHECK (max_homes IS NULL OR max_homes >= 0),
    cooldown_ms INTEGER NOT NULL DEFAULT 0
        CHECK (cooldown_ms >= 0),
    global_cooldown_ms INTEGER NOT NULL DEFAULT 0
        CHECK (global_cooldown_ms >= 0),
    deny_during_blood_moon INTEGER NOT NULL DEFAULT 1
        CHECK (deny_during_blood_moon IN (0, 1)),
    fee_amount INTEGER NOT NULL DEFAULT 0
        CHECK (fee_amount >= 0),
    updated_at_utc INTEGER NOT NULL
        CHECK (updated_at_utc >= 0),
    row_version INTEGER NOT NULL DEFAULT 0
        CHECK (row_version >= 0)
);

INSERT INTO teleport_settings (
    teleport_kind, enabled, max_homes, cooldown_ms, global_cooldown_ms,
    deny_during_blood_moon, fee_amount, updated_at_utc, row_version)
VALUES
    ('Home', 1, NULL, 0, 0, 1, 0, CAST(strftime('%s', 'now') AS INTEGER) * 1000, 0),
    ('City', 1, NULL, 0, 0, 1, 0, CAST(strftime('%s', 'now') AS INTEGER) * 1000, 0),
    ('Friend', 1, NULL, 0, 0, 1, 0, CAST(strftime('%s', 'now') AS INTEGER) * 1000, 0),
    ('Return', 1, NULL, 0, 0, 1, 0, CAST(strftime('%s', 'now') AS INTEGER) * 1000, 0),
    ('Admin', 1, NULL, 0, 0, 1, 0, CAST(strftime('%s', 'now') AS INTEGER) * 1000, 0);

CREATE TABLE player_homes (
    home_id TEXT PRIMARY KEY
        CHECK (length(trim(home_id)) > 0),
    crossplatform_id TEXT NOT NULL
        CHECK (length(trim(crossplatform_id)) > 0),
    name TEXT NOT NULL COLLATE NOCASE
        CHECK (length(trim(name)) > 0),
    world_id TEXT NOT NULL
        CHECK (length(trim(world_id)) > 0),
    x REAL NOT NULL,
    y REAL NOT NULL,
    z REAL NOT NULL,
    yaw REAL NOT NULL,
    created_at_utc INTEGER NOT NULL
        CHECK (created_at_utc >= 0),
    updated_at_utc INTEGER NOT NULL
        CHECK (updated_at_utc >= created_at_utc),
    row_version INTEGER NOT NULL DEFAULT 0
        CHECK (row_version >= 0)
);

CREATE UNIQUE INDEX ux_player_homes_name
    ON player_homes(crossplatform_id, name);
CREATE INDEX ix_player_homes_player_name
    ON player_homes(crossplatform_id, name ASC, home_id ASC);

CREATE TABLE cities (
    city_id TEXT PRIMARY KEY
        CHECK (length(trim(city_id)) > 0),
    name TEXT NOT NULL COLLATE NOCASE
        CHECK (length(trim(name)) > 0),
    description TEXT NOT NULL DEFAULT '',
    enabled INTEGER NOT NULL DEFAULT 1
        CHECK (enabled IN (0, 1)),
    world_id TEXT NOT NULL
        CHECK (length(trim(world_id)) > 0),
    x REAL NOT NULL,
    y REAL NOT NULL,
    z REAL NOT NULL,
    yaw REAL NOT NULL,
    sort_order INTEGER NOT NULL DEFAULT 0,
    created_at_utc INTEGER NOT NULL
        CHECK (created_at_utc >= 0),
    updated_at_utc INTEGER NOT NULL
        CHECK (updated_at_utc >= created_at_utc),
    row_version INTEGER NOT NULL DEFAULT 0
        CHECK (row_version >= 0)
);

CREATE UNIQUE INDEX ux_cities_name ON cities(name);
CREATE INDEX ix_cities_enabled_sort
    ON cities(enabled DESC, sort_order ASC, city_id ASC);

CREATE TABLE friendships (
    friendship_id TEXT PRIMARY KEY
        CHECK (length(trim(friendship_id)) > 0),
    member_a_crossplatform_id TEXT NOT NULL
        CHECK (length(trim(member_a_crossplatform_id)) > 0),
    member_b_crossplatform_id TEXT NOT NULL
        CHECK (length(trim(member_b_crossplatform_id)) > 0),
    created_by_crossplatform_id TEXT NOT NULL
        CHECK (length(trim(created_by_crossplatform_id)) > 0),
    accepted_at_utc INTEGER NOT NULL
        CHECK (accepted_at_utc >= 0),
    CHECK (member_a_crossplatform_id < member_b_crossplatform_id)
);

CREATE UNIQUE INDEX ux_friendships_pair
    ON friendships(member_a_crossplatform_id, member_b_crossplatform_id);
CREATE INDEX ix_friendships_member_b
    ON friendships(member_b_crossplatform_id, member_a_crossplatform_id);

CREATE TABLE friend_requests (
    request_id TEXT PRIMARY KEY
        CHECK (length(trim(request_id)) > 0),
    requester_crossplatform_id TEXT NOT NULL
        CHECK (length(trim(requester_crossplatform_id)) > 0),
    target_crossplatform_id TEXT NOT NULL
        CHECK (length(trim(target_crossplatform_id)) > 0),
    state TEXT NOT NULL
        CHECK (state IN ('Pending', 'Accepted', 'Rejected', 'Cancelled', 'Expired')),
    friendship_id TEXT NULL,
    created_at_utc INTEGER NOT NULL
        CHECK (created_at_utc >= 0),
    expires_at_utc INTEGER NOT NULL
        CHECK (expires_at_utc > created_at_utc),
    responded_at_utc INTEGER NULL
        CHECK (responded_at_utc IS NULL OR responded_at_utc >= created_at_utc),
    row_version INTEGER NOT NULL DEFAULT 0
        CHECK (row_version >= 0),
    CHECK (requester_crossplatform_id <> target_crossplatform_id),
    FOREIGN KEY (friendship_id) REFERENCES friendships(friendship_id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX ux_friend_requests_pending
    ON friend_requests(requester_crossplatform_id, target_crossplatform_id)
    WHERE state = 'Pending';
CREATE INDEX ix_friend_requests_target_state
    ON friend_requests(target_crossplatform_id, state, created_at_utc DESC);

CREATE TABLE teleport_friend_requests (
    request_id TEXT PRIMARY KEY
        CHECK (length(trim(request_id)) > 0),
    idempotency_key TEXT NOT NULL
        CHECK (length(trim(idempotency_key)) > 0),
    requester_crossplatform_id TEXT NOT NULL
        CHECK (length(trim(requester_crossplatform_id)) > 0),
    requester_entity_id INTEGER NOT NULL
        CHECK (requester_entity_id >= 0),
    requester_world_id TEXT NOT NULL
        CHECK (length(trim(requester_world_id)) > 0),
    target_crossplatform_id TEXT NOT NULL
        CHECK (length(trim(target_crossplatform_id)) > 0),
    target_entity_id INTEGER NOT NULL
        CHECK (target_entity_id >= 0),
    target_world_id TEXT NOT NULL
        CHECK (length(trim(target_world_id)) > 0),
    state TEXT NOT NULL
        CHECK (state IN ('Pending', 'Accepted', 'Rejected', 'Expired', 'Cancelled')),
    teleport_operation_id TEXT NULL,
    created_at_utc INTEGER NOT NULL
        CHECK (created_at_utc >= 0),
    expires_at_utc INTEGER NOT NULL
        CHECK (expires_at_utc > created_at_utc),
    responded_at_utc INTEGER NULL
        CHECK (responded_at_utc IS NULL OR responded_at_utc >= created_at_utc),
    row_version INTEGER NOT NULL DEFAULT 0
        CHECK (row_version >= 0),
    CHECK (requester_crossplatform_id <> target_crossplatform_id),
    CHECK ((state = 'Pending' AND responded_at_utc IS NULL AND teleport_operation_id IS NULL) OR
           (state = 'Accepted' AND responded_at_utc IS NOT NULL AND teleport_operation_id IS NOT NULL) OR
           (state IN ('Rejected', 'Expired', 'Cancelled') AND responded_at_utc IS NOT NULL AND teleport_operation_id IS NULL))
);

CREATE UNIQUE INDEX ux_teleport_friend_requests_idempotency
    ON teleport_friend_requests(idempotency_key);
CREATE UNIQUE INDEX ux_teleport_friend_requests_target_pending
    ON teleport_friend_requests(target_crossplatform_id)
    WHERE state = 'Pending';
CREATE INDEX ix_teleport_friend_requests_target_state_expiry
    ON teleport_friend_requests(target_crossplatform_id, state, expires_at_utc ASC, request_id ASC);

CREATE TABLE teleport_operations (
    operation_id TEXT PRIMARY KEY
        CHECK (length(trim(operation_id)) > 0),
    teleport_kind TEXT NOT NULL
        CHECK (teleport_kind IN ('Home', 'City', 'Friend', 'Return', 'Admin')),
    crossplatform_id TEXT NOT NULL
        CHECK (length(trim(crossplatform_id)) > 0),
    target_crossplatform_id TEXT NULL,
    expected_entity_id INTEGER NOT NULL
        CHECK (expected_entity_id >= 0),
    expected_world_id TEXT NOT NULL
        CHECK (length(trim(expected_world_id)) > 0),
    destination_world_id TEXT NOT NULL
        CHECK (length(trim(destination_world_id)) > 0),
    destination_x REAL NOT NULL,
    destination_y REAL NOT NULL,
    destination_z REAL NOT NULL,
    destination_yaw REAL NOT NULL,
    origin_world_id TEXT NULL,
    origin_x REAL NULL,
    origin_y REAL NULL,
    origin_z REAL NULL,
    origin_yaw REAL NULL,
    state TEXT NOT NULL
        CHECK (state IN (
            'Reserved', 'Dispatching', 'PendingReconciliation',
            'Completed', 'Failed', 'Refunded')),
    idempotency_key TEXT NOT NULL
        CHECK (length(trim(idempotency_key)) > 0),
    reservation_id TEXT NULL,
    actor_kind TEXT NOT NULL
        CHECK (length(trim(actor_kind)) > 0),
    actor_id TEXT NOT NULL
        CHECK (length(trim(actor_id)) > 0),
    correlation_id TEXT NULL,
    error_code TEXT NULL,
    created_at_utc INTEGER NOT NULL
        CHECK (created_at_utc >= 0),
    updated_at_utc INTEGER NOT NULL
        CHECK (updated_at_utc >= created_at_utc),
    completed_at_utc INTEGER NULL
        CHECK (completed_at_utc IS NULL OR completed_at_utc >= created_at_utc),
    row_version INTEGER NOT NULL DEFAULT 0
        CHECK (row_version >= 0),
    CHECK (
        (origin_world_id IS NULL AND origin_x IS NULL AND origin_y IS NULL
            AND origin_z IS NULL AND origin_yaw IS NULL)
        OR
        (origin_world_id IS NOT NULL AND length(trim(origin_world_id)) > 0
            AND origin_x IS NOT NULL AND origin_y IS NOT NULL
            AND origin_z IS NOT NULL AND origin_yaw IS NOT NULL)),
    FOREIGN KEY (reservation_id) REFERENCES economy_reservations(reservation_id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX ux_teleport_operation_idempotency
    ON teleport_operations(idempotency_key);
CREATE INDEX ix_teleport_operations_created_keyset
    ON teleport_operations(created_at_utc DESC, operation_id DESC);
CREATE INDEX ix_teleport_operations_state_updated
    ON teleport_operations(state, updated_at_utc ASC, operation_id ASC);

CREATE TABLE teleport_cooldowns (
    crossplatform_id TEXT NOT NULL
        CHECK (length(trim(crossplatform_id)) > 0),
    cooldown_kind TEXT NOT NULL
        CHECK (cooldown_kind IN ('Global', 'Home', 'City', 'Friend', 'Return', 'Admin')),
    available_at_utc INTEGER NOT NULL
        CHECK (available_at_utc >= 0),
    last_operation_id TEXT NOT NULL,
    updated_at_utc INTEGER NOT NULL
        CHECK (updated_at_utc >= 0),
    PRIMARY KEY (crossplatform_id, cooldown_kind),
    FOREIGN KEY (last_operation_id) REFERENCES teleport_operations(operation_id) ON DELETE RESTRICT
);

CREATE INDEX ix_teleport_cooldowns_available
    ON teleport_cooldowns(available_at_utc ASC, crossplatform_id ASC);

CREATE TABLE player_return_points (
    crossplatform_id TEXT PRIMARY KEY
        CHECK (length(trim(crossplatform_id)) > 0),
    source_operation_id TEXT NOT NULL,
    world_id TEXT NOT NULL
        CHECK (length(trim(world_id)) > 0),
    x REAL NOT NULL,
    y REAL NOT NULL,
    z REAL NOT NULL,
    yaw REAL NOT NULL,
    saved_at_utc INTEGER NOT NULL
        CHECK (saved_at_utc >= 0),
    row_version INTEGER NOT NULL DEFAULT 0
        CHECK (row_version >= 0),
    FOREIGN KEY (source_operation_id) REFERENCES teleport_operations(operation_id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX ux_player_return_points_operation
    ON player_return_points(source_operation_id);

CREATE TABLE vote_configurations (
    configuration_id TEXT PRIMARY KEY
        CHECK (length(trim(configuration_id)) > 0),
    vote_kind TEXT NOT NULL
        CHECK (vote_kind IN ('Kick', 'Restart')),
    enabled INTEGER NOT NULL DEFAULT 1
        CHECK (enabled IN (0, 1)),
    duration_ms INTEGER NOT NULL
        CHECK (duration_ms > 0),
    threshold_percent INTEGER NOT NULL
        CHECK (threshold_percent BETWEEN 1 AND 100),
    minimum_participants INTEGER NOT NULL
        CHECK (minimum_participants > 0),
    initiator_minimum_online_ms INTEGER NOT NULL DEFAULT 0
        CHECK (initiator_minimum_online_ms >= 0),
    participant_minimum_online_ms INTEGER NOT NULL DEFAULT 0
        CHECK (participant_minimum_online_ms >= 0),
    initiator_cooldown_ms INTEGER NOT NULL DEFAULT 0
        CHECK (initiator_cooldown_ms >= 0),
    target_cooldown_ms INTEGER NOT NULL DEFAULT 0
        CHECK (target_cooldown_ms >= 0),
    global_cooldown_ms INTEGER NOT NULL DEFAULT 0
        CHECK (global_cooldown_ms >= 0),
    mutual_exclusion_scope TEXT NOT NULL
        CHECK (length(trim(mutual_exclusion_scope)) > 0),
    allow_vote_change INTEGER NOT NULL DEFAULT 0
        CHECK (allow_vote_change IN (0, 1)),
    updated_at_utc INTEGER NOT NULL
        CHECK (updated_at_utc >= 0),
    row_version INTEGER NOT NULL DEFAULT 0
        CHECK (row_version >= 0)
);

CREATE UNIQUE INDEX ux_vote_configurations_kind
    ON vote_configurations(vote_kind);

INSERT INTO vote_configurations (
    configuration_id, vote_kind, enabled, duration_ms, threshold_percent,
    minimum_participants, initiator_minimum_online_ms, participant_minimum_online_ms,
    initiator_cooldown_ms, target_cooldown_ms, global_cooldown_ms,
    mutual_exclusion_scope, allow_vote_change, updated_at_utc, row_version)
VALUES
    ('configuration-kick', 'Kick', 0, 60000, 60, 2, 0, 0, 0, 0, 0,
        'global', 1, CAST(strftime('%s', 'now') AS INTEGER) * 1000, 0),
    ('configuration-restart', 'Restart', 0, 60000, 60, 2, 0, 0, 0, 0, 0,
        'global', 1, CAST(strftime('%s', 'now') AS INTEGER) * 1000, 0);

CREATE TABLE vote_rounds (
    round_id TEXT PRIMARY KEY
        CHECK (length(trim(round_id)) > 0),
    configuration_id TEXT NOT NULL,
    vote_kind TEXT NOT NULL
        CHECK (vote_kind IN ('Kick', 'Restart')),
    state TEXT NOT NULL
        CHECK (state IN (
            'Open', 'Passed', 'Rejected', 'Expired', 'Cancelled',
            'ActionQueued', 'ActionSucceeded', 'ActionFailed', 'ActionResultUnknown')),
    initiator_crossplatform_id TEXT NOT NULL
        CHECK (length(trim(initiator_crossplatform_id)) > 0),
    target_crossplatform_id TEXT NULL,
    scope_key TEXT NOT NULL
        CHECK (length(trim(scope_key)) > 0),
    eligible_count INTEGER NOT NULL
        CHECK (eligible_count >= 0),
    threshold_percent INTEGER NOT NULL
        CHECK (threshold_percent BETWEEN 1 AND 100),
    minimum_participants INTEGER NOT NULL
        CHECK (minimum_participants > 0),
    allow_vote_change INTEGER NOT NULL
        CHECK (allow_vote_change IN (0, 1)),
    idempotency_key TEXT NOT NULL
        CHECK (length(trim(idempotency_key)) > 0),
    action_job_id TEXT NULL,
    action_operation_id TEXT NULL,
    correlation_id TEXT NULL,
    opened_at_utc INTEGER NOT NULL
        CHECK (opened_at_utc >= 0),
    expires_at_utc INTEGER NOT NULL
        CHECK (expires_at_utc > opened_at_utc),
    settled_at_utc INTEGER NULL
        CHECK (settled_at_utc IS NULL OR settled_at_utc >= opened_at_utc),
    action_completed_at_utc INTEGER NULL
        CHECK (action_completed_at_utc IS NULL OR action_completed_at_utc >= opened_at_utc),
    row_version INTEGER NOT NULL DEFAULT 0
        CHECK (row_version >= 0),
    CHECK ((vote_kind = 'Kick' AND target_crossplatform_id IS NOT NULL
                AND length(trim(target_crossplatform_id)) > 0
                AND target_crossplatform_id <> initiator_crossplatform_id)
           OR (vote_kind = 'Restart' AND target_crossplatform_id IS NULL)),
    FOREIGN KEY (configuration_id) REFERENCES vote_configurations(configuration_id) ON DELETE RESTRICT,
    FOREIGN KEY (action_job_id) REFERENCES jobs(id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX ux_vote_round_idempotency
    ON vote_rounds(idempotency_key);
CREATE UNIQUE INDEX ux_vote_round_open_scope
    ON vote_rounds(scope_key)
    WHERE state = 'Open';
CREATE INDEX ix_vote_rounds_opened_keyset
    ON vote_rounds(opened_at_utc DESC, round_id DESC);
CREATE INDEX ix_vote_rounds_state_expiry
    ON vote_rounds(state, expires_at_utc ASC, round_id ASC);

CREATE TABLE vote_eligible_players (
    round_id TEXT NOT NULL,
    crossplatform_id TEXT NOT NULL
        CHECK (length(trim(crossplatform_id)) > 0),
    snapshotted_at_utc INTEGER NOT NULL
        CHECK (snapshotted_at_utc >= 0),
    PRIMARY KEY (round_id, crossplatform_id),
    FOREIGN KEY (round_id) REFERENCES vote_rounds(round_id) ON DELETE CASCADE
);

CREATE TABLE vote_ballots (
    ballot_id TEXT PRIMARY KEY
        CHECK (length(trim(ballot_id)) > 0),
    round_id TEXT NOT NULL,
    crossplatform_id TEXT NOT NULL
        CHECK (length(trim(crossplatform_id)) > 0),
    choice TEXT NOT NULL
        CHECK (choice IN ('Yes', 'No')),
    change_count INTEGER NOT NULL DEFAULT 0
        CHECK (change_count BETWEEN 0 AND 1),
    cast_at_utc INTEGER NOT NULL
        CHECK (cast_at_utc >= 0),
    updated_at_utc INTEGER NOT NULL
        CHECK (updated_at_utc >= cast_at_utc),
    FOREIGN KEY (round_id, crossplatform_id)
        REFERENCES vote_eligible_players(round_id, crossplatform_id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX ux_vote_ballot
    ON vote_ballots(round_id, crossplatform_id);
CREATE INDEX ix_vote_ballots_round_choice
    ON vote_ballots(round_id, choice, ballot_id ASC);

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
FROM vote_rounds;
