using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Dapper;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Hosting.Authentication;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class SqliteAuthenticationStoreTests
    {
        [Fact]
        public void Upgrade_creates_schema_and_can_be_repeated()
        {
            using var database = new TemporaryDatabase();
            var bootstrapper = new SqliteDatabaseBootstrapper(database.ConnectionFactory);

            bootstrapper.Upgrade();
            bootstrapper.Upgrade();

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(
                2,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('users', 'access_tokens');"));
            Assert.Equal(
                4,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND (name LIKE 'ix_%' OR name = 'ux_users_username');"));
            Assert.Equal(
                1,
                connection.ExecuteScalar<int>("SELECT COUNT(*) FROM SchemaVersions;"));
            Assert.Equal(
                "wal",
                connection.ExecuteScalar<string>("PRAGMA journal_mode;")!.ToLowerInvariant());
        }

        [Fact]
        public void EnsureBootstrapOwner_synchronizes_the_stable_owner_credentials()
        {
            using var database = new TemporaryDatabase();
            database.Upgrade();
            var store = new SqliteAuthenticationStore(database.ConnectionFactory);

            store.EnsureBootstrapOwner("FirstOwner", "first-password");
            Assert.True(store.TryVerify("FirstOwner", "first-password", out var firstIdentity));
            Assert.Equal(SqliteAuthenticationStore.BootstrapOwnerSubject, firstIdentity.Subject);

            store.EnsureBootstrapOwner("CurrentOwner", "current-password");

            Assert.False(store.TryVerify("FirstOwner", "first-password", out _));
            Assert.False(store.TryVerify("CurrentOwner", "first-password", out _));
            Assert.True(store.TryVerify("CurrentOwner", "current-password", out var currentIdentity));
            Assert.Equal(SqliteAuthenticationStore.BootstrapOwnerSubject, currentIdentity.Subject);
            Assert.True(store.TryGetActive(SqliteAuthenticationStore.BootstrapOwnerSubject, out var activeIdentity));
            Assert.Equal("CurrentOwner", activeIdentity.Username);
            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM users;"));
        }

        [Fact]
        public void EnsureBootstrapOwner_preserves_tokens_until_credentials_change()
        {
            using var database = new TemporaryDatabase();
            var store = database.CreateOwnerStore();
            Assert.True(store.TryGetActive(SqliteAuthenticationStore.BootstrapOwnerSubject, out var owner));
            var issuedUtc = new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero);
            var token = store.Issue(owner, issuedUtc, issuedUtc.AddHours(1));

            store.EnsureBootstrapOwner("Owner", "owner-password");
            Assert.True(store.TryValidate(token, issuedUtc.AddMinutes(1), out _));

            store.EnsureBootstrapOwner("Owner", "rotated-password");
            Assert.False(store.TryValidate(token, issuedUtc.AddMinutes(1), out _));
        }

        [Fact]
        public void Disposed_connection_factory_rejects_new_connections()
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-sqlite-tests",
                Guid.NewGuid().ToString("N"),
                "panel.db");
            var connectionFactory = new SqliteConnectionFactory(databasePath);

            connectionFactory.Dispose();

            Assert.Throws<ObjectDisposedException>(() => connectionFactory.Open());
        }

        [Fact]
        public void Issued_token_validates_until_expiration()
        {
            using var database = new TemporaryDatabase();
            var store = database.CreateOwnerStore();
            Assert.True(store.TryGetActive(SqliteAuthenticationStore.BootstrapOwnerSubject, out var owner));
            var issuedUtc = new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero);
            var expiresUtc = issuedUtc.AddMinutes(30);

            var token = store.Issue(owner, issuedUtc, expiresUtc);

            Assert.StartsWith("7dp_", token);
            Assert.True(store.TryValidate(token, issuedUtc, out var storedToken));
            Assert.Equal(owner.Subject, storedToken.Identity.Subject);
            Assert.Equal(owner.Username, storedToken.Identity.Username);
            Assert.Equal(issuedUtc, storedToken.IssuedUtc);
            Assert.Equal(expiresUtc, storedToken.ExpiresUtc);
            Assert.False(store.TryValidate(token, expiresUtc, out _));
        }

        [Fact]
        public void Issued_token_survives_store_and_connection_factory_recreation()
        {
            using var database = new TemporaryDatabase();
            var store = database.CreateOwnerStore();
            Assert.True(store.TryGetActive(SqliteAuthenticationStore.BootstrapOwnerSubject, out var owner));
            var issuedUtc = new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero);
            var token = store.Issue(owner, issuedUtc, issuedUtc.AddMinutes(30));

            using var reopenedFactory = new SqliteConnectionFactory(database.DatabasePath);
            var reopenedStore = new SqliteAuthenticationStore(reopenedFactory);

            Assert.True(reopenedStore.TryValidate(token, issuedUtc.AddMinutes(1), out var storedToken));
            Assert.Equal(owner.Subject, storedToken.Identity.Subject);
        }

        [Fact]
        public void Issuing_beyond_capacity_removes_the_oldest_token()
        {
            using var database = new TemporaryDatabase();
            var store = database.CreateOwnerStore();
            Assert.True(store.TryGetActive(SqliteAuthenticationStore.BootstrapOwnerSubject, out var owner));
            var issuedUtc = new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero);
            var tokens = new List<string>();

            for (var index = 0; index <= SqliteAuthenticationStore.MaximumAccessTokenCount; index++)
            {
                var tokenIssuedUtc = issuedUtc.AddMilliseconds(index);
                tokens.Add(store.Issue(owner, tokenIssuedUtc, tokenIssuedUtc.AddHours(1)));
            }

            Assert.False(store.TryValidate(tokens[0], issuedUtc.AddMinutes(1), out _));
            Assert.True(store.TryValidate(tokens[1], issuedUtc.AddMinutes(1), out _));
            Assert.True(store.TryValidate(tokens[tokens.Count - 1], issuedUtc.AddMinutes(1), out _));
            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(
                SqliteAuthenticationStore.MaximumAccessTokenCount,
                connection.ExecuteScalar<int>("SELECT COUNT(*) FROM access_tokens;"));
        }

        [Fact]
        public void Database_does_not_contain_plaintext_password_or_token_secret()
        {
            const string username = "RawStorageOwner";
            const string password = "plaintext-password-marker-64ad8a66";
            using var database = new TemporaryDatabase();
            database.Upgrade();
            var store = new SqliteAuthenticationStore(database.ConnectionFactory);
            store.EnsureBootstrapOwner(username, password);
            Assert.True(store.TryVerify(username, password, out var owner));
            var issuedUtc = new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero);
            var token = store.Issue(owner, issuedUtc, issuedUtc.AddMinutes(30));
            var encodedSecret = token.Substring(token.IndexOf('.') + 1);

            using (var connection = database.ConnectionFactory.Open())
            {
                connection.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
            }

            SqliteConnection.ClearAllPools();
            var databaseBytes = File.ReadAllBytes(database.DatabasePath);
            Assert.False(Contains(databaseBytes, Encoding.UTF8.GetBytes(password)));
            Assert.False(Contains(databaseBytes, Encoding.UTF8.GetBytes(token)));
            Assert.False(Contains(databaseBytes, Encoding.UTF8.GetBytes(encodedSecret)));
        }

        private static bool Contains(byte[] value, byte[] candidate)
        {
            for (var start = 0; start <= value.Length - candidate.Length; start++)
            {
                var matches = true;
                for (var index = 0; index < candidate.Length; index++)
                {
                    if (value[start + index] == candidate[index]) continue;
                    matches = false;
                    break;
                }

                if (matches) return true;
            }

            return false;
        }

        private sealed class TemporaryDatabase : IDisposable
        {
            private readonly string directoryPath;

            public TemporaryDatabase()
            {
                directoryPath = Path.Combine(
                    Path.GetTempPath(),
                    "7dpanel-sqlite-tests",
                    Guid.NewGuid().ToString("N"));
                DatabasePath = Path.Combine(directoryPath, "panel.db");
                ConnectionFactory = new SqliteConnectionFactory(DatabasePath);
            }

            public string DatabasePath { get; }

            public SqliteConnectionFactory ConnectionFactory { get; }

            public void Upgrade()
            {
                new SqliteDatabaseBootstrapper(ConnectionFactory).Upgrade();
            }

            public SqliteAuthenticationStore CreateOwnerStore()
            {
                Upgrade();
                var store = new SqliteAuthenticationStore(ConnectionFactory);
                store.EnsureBootstrapOwner("Owner", "owner-password");
                return store;
            }

            public void Dispose()
            {
                ConnectionFactory.Dispose();
                if (Directory.Exists(directoryPath)) Directory.Delete(directoryPath, recursive: true);
            }
        }
    }
}
