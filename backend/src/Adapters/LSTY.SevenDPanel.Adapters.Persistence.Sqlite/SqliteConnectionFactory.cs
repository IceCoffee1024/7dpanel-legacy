using System;
using System.IO;
using System.Threading;
using Microsoft.Data.Sqlite;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite
{
    public sealed class SqliteConnectionFactory : IDisposable
    {
        public const int DefaultTimeoutSeconds = 5;

        private int disposed;

        public SqliteConnectionFactory(
            string databasePath,
            int defaultTimeoutSeconds = DefaultTimeoutSeconds)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("A SQLite database path is required.", nameof(databasePath));
            if (defaultTimeoutSeconds <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(defaultTimeoutSeconds),
                    "The SQLite default timeout must be positive.");

            DatabasePath = Path.GetFullPath(databasePath);
            ConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                ForeignKeys = true,
                Pooling = true,
                DefaultTimeout = defaultTimeoutSeconds
            }.ToString();
        }

        public string DatabasePath { get; }

        public string ConnectionString { get; }

        public SqliteConnection Open()
        {
            ThrowIfDisposed();
            var connection = new SqliteConnection(ConnectionString);
            try
            {
                connection.Open();
                ThrowIfDisposed();
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                SqliteConnection.ClearAllPools();
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref disposed) != 0)
                throw new ObjectDisposedException(nameof(SqliteConnectionFactory));
        }
    }
}
