using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using Dapper;
using DbUp;
using DbUp.Builder;
using DbUp.Engine.Output;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite
{
    public sealed class SqliteDatabaseBootstrapper
    {
        private readonly SqliteConnectionFactory connectionFactory;
        private readonly Action<string>? log;

        public SqliteDatabaseBootstrapper(
            SqliteConnectionFactory connectionFactory,
            Action<string>? log = null)
        {
            this.connectionFactory = connectionFactory ??
                throw new ArgumentNullException(nameof(connectionFactory));
            this.log = log;
        }

        public void Upgrade()
        {
            var directory = Path.GetDirectoryName(connectionFactory.DatabasePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            using (var connection = connectionFactory.Open())
            {
                var journalMode = connection.ExecuteScalar<string>("PRAGMA journal_mode = WAL;");
                if (!string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "SQLite did not enable write-ahead logging for the panel database.");
                }
            }

            try
            {
                var builder = DeployChanges.To
                    .SqliteDatabase(connectionFactory.ConnectionString)
                    .WithScriptsEmbeddedInAssembly(
                        typeof(SqliteDatabaseBootstrapper).Assembly,
                        IsMigrationResource)
                    .WithTransactionPerScript();
                if (log != null) builder.LogTo(new ActionUpgradeLog(log));

                var result = builder.Build().PerformUpgrade();
                if (!result.Successful)
                {
                    throw new InvalidOperationException(
                        "SQLite database migration failed.",
                        result.Error);
                }
            }
            catch (InvalidOperationException exception)
                when (exception.Message == "SQLite database migration failed.")
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "SQLite database migration failed.",
                    exception);
            }
        }

        private static bool IsMigrationResource(string resourceName)
        {
            var prefix = typeof(SqliteDatabaseBootstrapper).Namespace + ".Migrations.";
            return resourceName.StartsWith(prefix, StringComparison.Ordinal) &&
                resourceName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class ActionUpgradeLog : IUpgradeLog
        {
            private readonly Action<string> write;

            public ActionUpgradeLog(Action<string> write)
            {
                this.write = write;
            }

            public void LogTrace(string format, params object[] args) => Write(format, args);

            public void LogDebug(string format, params object[] args) => Write(format, args);

            public void LogInformation(string format, params object[] args) => Write(format, args);

            public void LogWarning(string format, params object[] args) => Write(format, args);

            public void LogError(string format, params object[] args) => Write(format, args);

            public void LogError(Exception exception, string format, params object[] args)
            {
                Write(format, args);
                write(exception.Message);
            }

            private void Write(string format, object[] args)
            {
                try
                {
                    write(string.Format(CultureInfo.InvariantCulture, format, args));
                }
                catch (FormatException)
                {
                    write(format);
                }
            }
        }
    }
}
