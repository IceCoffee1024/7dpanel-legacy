using System;
using System.IO;
using LSTY.SevenDPanel.Adapters.Local.Files;
using LSTY.SevenDPanel.Application.Backups;
using Microsoft.Data.Sqlite;

namespace LSTY.SevenDPanel.Adapters.Local.Backups
{
    public sealed class PanelDatabaseBackupJobHandler
    {
        public const string OnlineBackupFailedError = "panel_database_backup_failed";

        private readonly ApprovedStorageRoots roots;
        private readonly FileSystemBackupArchiveStore archives;
        private readonly string databasePath;

        public PanelDatabaseBackupJobHandler(
            ApprovedStorageRoots roots,
            FileSystemBackupArchiveStore archives,
            string databasePath)
        {
            this.roots = roots ?? throw new ArgumentNullException(nameof(roots));
            this.archives = archives ?? throw new ArgumentNullException(nameof(archives));
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("A Panel database path is required.", nameof(databasePath));
            this.databasePath = Path.GetFullPath(databasePath);
            roots.ValidatePanelStatePath(this.databasePath);
        }

        public CompletedBackup Execute(Guid sourceJobId, DateTimeOffset createdAtUtc)
        {
            var snapshotPath = Path.Combine(
                roots.PanelStateRoot,
                ".panel-database-" + sourceJobId.ToString("N") + "-" +
                Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                CreateConsistentSnapshot(snapshotPath);
                return archives.CreatePanelDatabaseArchive(
                    sourceJobId,
                    snapshotPath,
                    createdAtUtc);
            }
            finally
            {
                if (File.Exists(snapshotPath)) File.Delete(snapshotPath);
            }
        }

        private void CreateConsistentSnapshot(string snapshotPath)
        {
            try
            {
                roots.ValidatePanelStatePath(databasePath);
                roots.ValidatePanelStatePath(snapshotPath);
                if (!File.Exists(databasePath))
                    throw new FileNotFoundException("The Panel database does not exist.", databasePath);

                using var source = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = databasePath,
                    Mode = SqliteOpenMode.ReadOnly,
                    Pooling = false
                }.ToString());
                using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = snapshotPath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Pooling = false
                }.ToString());
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);
            }
            catch (BackupArchiveException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new BackupArchiveException(OnlineBackupFailedError, exception);
            }
        }
    }
}
