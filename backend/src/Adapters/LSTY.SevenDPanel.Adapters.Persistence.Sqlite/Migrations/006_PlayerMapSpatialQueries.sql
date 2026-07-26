CREATE INDEX ix_player_history_snapshots_spatial_x_z_time
    ON player_history_snapshots(
        position_x,
        position_z,
        observed_utc DESC,
        snapshot_id DESC,
        crossplatform_id);
