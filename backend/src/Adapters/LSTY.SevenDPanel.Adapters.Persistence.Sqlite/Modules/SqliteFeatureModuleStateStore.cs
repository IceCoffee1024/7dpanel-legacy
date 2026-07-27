using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using LSTY.SevenDPanel.Application.Modules;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Modules
{
    public sealed class SqliteFeatureModuleStateStore : IFeatureModuleStateStore
    {
        private const string SelectColumns = @"SELECT
            module_id AS ModuleId, is_enabled AS IsEnabled,
            lifecycle_state AS LifecycleState, updated_by AS UpdatedBy,
            correlation_id AS CorrelationId, updated_at_utc AS UpdatedAtUtc,
            row_version AS RowVersion FROM feature_module_states";

        private readonly SqliteConnectionFactory connectionFactory;

        public SqliteFeatureModuleStateStore(SqliteConnectionFactory connectionFactory) =>
            this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

        public FeatureModuleState Get(FeatureModuleId moduleId)
        {
            RequireId(moduleId);
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<Row>(
                SelectColumns + " WHERE module_id = @ModuleId;",
                new { ModuleId = moduleId.ToString() });
            return row == null ? FeatureModuleState.DefaultEnabled(moduleId) : ToState(row);
        }

        public IReadOnlyList<FeatureModuleState> List()
        {
            using var connection = connectionFactory.Open();
            var persisted = connection.Query<Row>(SelectColumns + ";")
                .Select(ToState)
                .ToDictionary(state => state.ModuleId);
            return Enum.GetValues(typeof(FeatureModuleId))
                .Cast<FeatureModuleId>()
                .Select(id => persisted.TryGetValue(id, out var state)
                    ? state
                    : FeatureModuleState.DefaultEnabled(id))
                .ToArray();
        }

        public FeatureModuleState Save(FeatureModuleStateChange change)
        {
            if (change == null) throw new ArgumentNullException(nameof(change));
            RequireId(change.ModuleId);
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);

            var duplicate = connection.QuerySingleOrDefault<Row>(
                SelectColumns + " WHERE correlation_id = @CorrelationId;",
                new { change.CorrelationId },
                transaction);
            if (duplicate != null)
            {
                var existing = ToState(duplicate);
                if (existing.ModuleId != change.ModuleId ||
                    existing.IsEnabled != change.IsEnabled ||
                    existing.LifecycleState != change.LifecycleState)
                {
                    throw new FeatureModuleStateConflictException(change.ModuleId);
                }
                transaction.Commit();
                return existing;
            }

            int changed;
            if (change.ExpectedRowVersion == 0)
            {
                changed = connection.Execute(
                    @"INSERT INTO feature_module_states (
                          module_id, is_enabled, lifecycle_state, updated_by,
                          correlation_id, updated_at_utc, row_version)
                      VALUES (@ModuleId, @IsEnabled, @LifecycleState, @UpdatedBy,
                          @CorrelationId, @UpdatedAtUtc, 1)
                      ON CONFLICT(module_id) DO NOTHING;",
                    Parameters(change),
                    transaction);
            }
            else
            {
                changed = connection.Execute(
                    @"UPDATE feature_module_states
                      SET is_enabled = @IsEnabled,
                          lifecycle_state = @LifecycleState,
                          updated_by = @UpdatedBy,
                          correlation_id = @CorrelationId,
                          updated_at_utc = @UpdatedAtUtc,
                          row_version = row_version + 1
                      WHERE module_id = @ModuleId AND row_version = @ExpectedRowVersion;",
                    Parameters(change),
                    transaction);
            }

            if (changed != 1)
                throw new FeatureModuleStateConflictException(change.ModuleId);
            var saved = connection.QuerySingle<Row>(
                SelectColumns + " WHERE module_id = @ModuleId;",
                new { ModuleId = change.ModuleId.ToString() },
                transaction);
            transaction.Commit();
            return ToState(saved);
        }

        private static object Parameters(FeatureModuleStateChange change) => new
        {
            ModuleId = change.ModuleId.ToString(),
            IsEnabled = change.IsEnabled ? 1 : 0,
            LifecycleState = change.LifecycleState.ToString(),
            change.UpdatedBy,
            change.CorrelationId,
            UpdatedAtUtc = change.UpdatedAtUtc.ToUnixTimeMilliseconds(),
            change.ExpectedRowVersion
        };

        private static FeatureModuleState ToState(Row row)
        {
            if (!Enum.TryParse(row.ModuleId, out FeatureModuleId moduleId) ||
                !Enum.IsDefined(typeof(FeatureModuleId), moduleId) ||
                !Enum.TryParse(row.LifecycleState, out FeatureModuleLifecycleState lifecycle) ||
                !Enum.IsDefined(typeof(FeatureModuleLifecycleState), lifecycle))
            {
                throw new InvalidOperationException("feature_module_state_invalid");
            }
            return new FeatureModuleState(
                moduleId,
                row.IsEnabled != 0,
                lifecycle,
                row.UpdatedBy,
                row.CorrelationId,
                DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAtUtc),
                row.RowVersion);
        }

        private static void RequireId(FeatureModuleId moduleId)
        {
            if (!Enum.IsDefined(typeof(FeatureModuleId), moduleId))
                throw new ArgumentOutOfRangeException(nameof(moduleId));
        }

        private sealed class Row
        {
            public string ModuleId { get; set; } = string.Empty;
            public int IsEnabled { get; set; }
            public string LifecycleState { get; set; } = string.Empty;
            public string UpdatedBy { get; set; } = string.Empty;
            public string CorrelationId { get; set; } = string.Empty;
            public long UpdatedAtUtc { get; set; }
            public long RowVersion { get; set; }
        }
    }
}
