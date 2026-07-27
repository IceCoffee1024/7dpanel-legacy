using System;
using LSTY.SevenDPanel.Adapters.Local.Restore;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;

namespace LSTY.SevenDPanel.DependencyInjection
{
    internal sealed class PendingRestoreStartupStep
    {
        private readonly Action applyPendingRestore;
        private readonly Action migrateDatabase;
        private readonly Action reconcileRestoreResult;

        public PendingRestoreStartupStep(
            PendingRestoreApplier applier,
            SqliteDatabaseBootstrapper bootstrapper,
            RestoreResultReconciler reconciler)
            : this(
                () => (applier ?? throw new ArgumentNullException(nameof(applier))).ApplyPending(),
                (bootstrapper ?? throw new ArgumentNullException(nameof(bootstrapper))).Upgrade,
                () => (reconciler ?? throw new ArgumentNullException(nameof(reconciler))).Reconcile())
        {
        }

        internal PendingRestoreStartupStep(
            Action applyPendingRestore,
            Action migrateDatabase,
            Action reconcileRestoreResult)
        {
            this.applyPendingRestore = applyPendingRestore ??
                throw new ArgumentNullException(nameof(applyPendingRestore));
            this.migrateDatabase = migrateDatabase ??
                throw new ArgumentNullException(nameof(migrateDatabase));
            this.reconcileRestoreResult = reconcileRestoreResult ??
                throw new ArgumentNullException(nameof(reconcileRestoreResult));
        }

        public void Execute()
        {
            applyPendingRestore();
            migrateDatabase();
            reconcileRestoreResult();
        }
    }
}
