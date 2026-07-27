using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Domain.Backups;

namespace LSTY.SevenDPanel.Application.Backups
{
    public sealed class BackupPolicyService
    {
        private static readonly BackupKind[] FixedKinds =
        {
            BackupKind.World,
            BackupKind.PanelDatabase,
            BackupKind.ServerConfiguration
        };

        private readonly IBackupPolicyStore store;
        private readonly IReadOnlyList<string> approvedRootIds;
        private readonly HashSet<string> approvedRootLookup;

        public BackupPolicyService(
            IBackupPolicyStore store,
            IEnumerable<string> approvedRootIds)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            if (approvedRootIds == null) throw new ArgumentNullException(nameof(approvedRootIds));
            var roots = approvedRootIds.Select(RequireRootId).Distinct(StringComparer.Ordinal).ToArray();
            if (roots.Length == 0)
                throw new ArgumentException("At least one approved backup root is required.", nameof(approvedRootIds));
            this.approvedRootIds = roots;
            approvedRootLookup = new HashSet<string>(roots, StringComparer.Ordinal);
        }

        public IReadOnlyList<BackupPolicyDefinition> List()
        {
            var stored = store.List().ToDictionary(policy => policy.Kind);
            return FixedKinds
                .Select(kind => stored.TryGetValue(kind, out var policy)
                    ? policy
                    : Default(kind, approvedRootIds[0]))
                .ToArray();
        }

        public BackupPolicyDefinition Get(BackupKind kind)
        {
            if (!Enum.IsDefined(typeof(BackupKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            return store.Get(kind) ?? Default(kind, approvedRootIds[0]);
        }

        public BackupPolicyDefinition Save(BackupPolicyDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (!approvedRootLookup.Contains(definition.BackupRootId))
                throw new ArgumentException("backup_root_not_approved", nameof(definition));
            return store.Upsert(definition);
        }

        private static BackupPolicyDefinition Default(BackupKind kind, string rootId) =>
            new BackupPolicyDefinition(
                kind,
                false,
                "0 3 * * *",
                "UTC",
                rootId,
                3,
                7,
                true,
                0);

        private static string RequireRootId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("backup_root_id_required", nameof(value));
            var normalized = value.Trim();
            if (normalized.IndexOf('/') >= 0 ||
                normalized.IndexOf('\\') >= 0 ||
                normalized.IndexOf("..", StringComparison.Ordinal) >= 0)
            {
                throw new ArgumentException("backup_root_id_invalid", nameof(value));
            }
            return normalized;
        }
    }
}
