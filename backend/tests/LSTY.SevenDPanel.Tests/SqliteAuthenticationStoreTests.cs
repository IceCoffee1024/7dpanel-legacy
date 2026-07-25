using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
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
                3,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('users', 'access_tokens', 'api_keys');"));
            Assert.Equal(
                12,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND (name LIKE 'ix_%' OR name = 'ux_users_username');"));
            Assert.Equal(
                4,
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
            Assert.Equal(
                1000,
                connection.ExecuteScalar<int>(
                    "SELECT password_iterations FROM users WHERE subject = 'owner';"));
        }

        [Fact]
        public void Authentication_schema_records_the_bootstrap_owner_role()
        {
            using var database = new TemporaryDatabase();
            database.Upgrade();
            var store = new SqliteAuthenticationStore(database.ConnectionFactory);
            store.EnsureBootstrapOwner("Owner", "owner-password");

            using var connection = database.ConnectionFactory.Open();
            var columns = connection.Query<string>("SELECT name FROM pragma_table_info('users');");

            Assert.Contains("role", columns);
            Assert.Equal(
                "Owner",
                connection.ExecuteScalar<string>(
                    "SELECT role FROM users WHERE subject = 'owner';"));
        }

        [Theory]
        [InlineData("Operator", 1000)]
        [InlineData("Owner", 999)]
        public void Authentication_schema_rejects_unsupported_role_or_password_iterations(
            string role,
            int passwordIterations)
        {
            using var database = new TemporaryDatabase();
            database.Upgrade();
            using var connection = database.ConnectionFactory.Open();

            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO users (
                      subject,
                      username,
                      role,
                      password_salt,
                      password_hash,
                      password_iterations,
                      enabled,
                      updated_utc)
                  VALUES (
                      @Subject,
                      @Username,
                      @Role,
                      @PasswordSalt,
                      @PasswordHash,
                      @PasswordIterations,
                      1,
                      @UpdatedUtc);",
                new
                {
                    Subject = "invalid-user-" + passwordIterations,
                    Username = "InvalidUser" + passwordIterations,
                    Role = role,
                    PasswordSalt = new byte[16],
                    PasswordHash = new byte[32],
                    PasswordIterations = passwordIterations,
                    UpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }));
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
        public void Api_key_creation_persists_only_a_digest_and_rebuilds_current_identity()
        {
            using var database = new TemporaryDatabase();
            var store = database.CreateOwnerStore();
            var apiKeys = Assert.IsAssignableFrom<IPanelApiKeyStore>(store);
            var createdUtc = new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero);
            Assert.True(store.TryGetActive(SqliteAuthenticationStore.BootstrapOwnerSubject, out var owner));

            var result = apiKeys.Create(owner.Subject, " deployment ", createdUtc, null);

            Assert.Equal(ApiKeyCreateStatus.Created, result.Status);
            Assert.NotNull(result.CreatedApiKey);
            var created = result.CreatedApiKey!;
            Assert.Matches("^7dp_k_[A-Za-z0-9_-]{22}_[A-Za-z0-9_-]{43}$", created.ApiKey);
            Assert.Equal("deployment", created.Metadata.Name);
            Assert.Equal(createdUtc, created.Metadata.CreatedUtc);
            Assert.Null(created.Metadata.ExpiresUtc);
            Assert.Equal(ApiKeyStatus.Active, created.Metadata.Status);
            Assert.True(apiKeys.TryValidate(created.ApiKey, createdUtc, out var validated));
            Assert.Equal(owner.Subject, validated.Identity.Subject);
            Assert.Equal(PanelUserIdentity.OwnerRole, validated.Identity.Role);

            var nonCanonicalApiKey = ReplaceLastBase64UrlCharacter(created.ApiKey);
            Assert.False(apiKeys.TryValidate(nonCanonicalApiKey, createdUtc, out _));

            using (var connection = database.ConnectionFactory.Open())
            {
                connection.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
            }

            SqliteConnection.ClearAllPools();
            var databaseBytes = File.ReadAllBytes(database.DatabasePath);
            Assert.False(Contains(databaseBytes, Encoding.UTF8.GetBytes(created.ApiKey)));
            Assert.False(Contains(databaseBytes, Encoding.UTF8.GetBytes(GetApiKeySecret(created.ApiKey))));
        }

        [Fact]
        public void Api_key_create_reports_validation_and_capacity_failures_without_returning_a_secret()
        {
            using var database = new TemporaryDatabase();
            var store = database.CreateOwnerStore();
            var apiKeys = Assert.IsAssignableFrom<IPanelApiKeyStore>(store);
            var now = new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero);

            Assert.Equal(
                ApiKeyCreateStatus.InvalidName,
                apiKeys.Create(SqliteAuthenticationStore.BootstrapOwnerSubject, "  ", now, null).Status);
            Assert.Equal(
                ApiKeyCreateStatus.InvalidName,
                apiKeys.Create(
                    SqliteAuthenticationStore.BootstrapOwnerSubject,
                    new string('x', 81),
                    now,
                    null).Status);
            Assert.Equal(
                ApiKeyCreateStatus.InvalidExpiration,
                apiKeys.Create(
                    SqliteAuthenticationStore.BootstrapOwnerSubject,
                    "expired",
                    now,
                    now).Status);
            Assert.Equal(
                ApiKeyCreateStatus.SubjectNotFound,
                apiKeys.Create("missing", "missing", now, null).Status);

            CreatedApiKey? firstCreated = null;
            for (var index = 0; index < 32; index++)
            {
                var result = apiKeys.Create(
                    SqliteAuthenticationStore.BootstrapOwnerSubject,
                    "key-" + index,
                    now.AddMilliseconds(index),
                    null);
                Assert.Equal(ApiKeyCreateStatus.Created, result.Status);
                firstCreated ??= result.CreatedApiKey;
            }

            var capacity = apiKeys.Create(
                SqliteAuthenticationStore.BootstrapOwnerSubject,
                "one-too-many",
                now.AddMinutes(1),
                null);
            Assert.Equal(ApiKeyCreateStatus.CapacityReached, capacity.Status);
            Assert.Null(capacity.CreatedApiKey);
            Assert.NotNull(firstCreated);
            Assert.True(apiKeys.Revoke(
                SqliteAuthenticationStore.BootstrapOwnerSubject,
                firstCreated!.Metadata.KeyId,
                now.AddMinutes(2)));
            Assert.True(apiKeys.Revoke(
                SqliteAuthenticationStore.BootstrapOwnerSubject,
                firstCreated.Metadata.KeyId,
                now.AddMinutes(3)));
            Assert.False(apiKeys.Revoke("other-subject", firstCreated.Metadata.KeyId, now.AddMinutes(3)));
            Assert.Equal(
                ApiKeyCreateStatus.Created,
                apiKeys.Create(
                    SqliteAuthenticationStore.BootstrapOwnerSubject,
                    "replacement",
                    now.AddMinutes(4),
                    null).Status);
        }

        [Fact]
        public void Api_key_name_limits_unicode_scalars()
        {
            using var database = new TemporaryDatabase();
            var store = database.CreateOwnerStore();
            var apiKeys = Assert.IsAssignableFrom<IPanelApiKeyStore>(store);
            var now = new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero);

            Assert.Equal(
                ApiKeyCreateStatus.Created,
                apiKeys.Create(
                    SqliteAuthenticationStore.BootstrapOwnerSubject,
                    CreateUnicodeScalarName(80),
                    now,
                    null).Status);
            Assert.Equal(
                ApiKeyCreateStatus.InvalidName,
                apiKeys.Create(
                    SqliteAuthenticationStore.BootstrapOwnerSubject,
                    CreateUnicodeScalarName(81),
                    now,
                    null).Status);
        }

        [Fact]
        public void Api_key_validation_reflects_expiration_revocation_and_current_user_role()
        {
            using var database = new TemporaryDatabase();
            var store = database.CreateOwnerStore();
            var apiKeys = Assert.IsAssignableFrom<IPanelApiKeyStore>(store);
            var now = new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero);

            var expiring = apiKeys.Create(
                SqliteAuthenticationStore.BootstrapOwnerSubject,
                "expires",
                now,
                now.AddMinutes(1)).CreatedApiKey!;
            Assert.True(apiKeys.TryValidate(expiring.ApiKey, now, out _));
            Assert.False(apiKeys.TryValidate(expiring.ApiKey, now.AddMinutes(1), out _));

            var revocable = apiKeys.Create(
                SqliteAuthenticationStore.BootstrapOwnerSubject,
                "revoke",
                now,
                null).CreatedApiKey!;
            Assert.True(apiKeys.Revoke(
                SqliteAuthenticationStore.BootstrapOwnerSubject,
                revocable.Metadata.KeyId,
                now.AddMinutes(1)));
            Assert.False(apiKeys.TryValidate(revocable.ApiKey, now.AddMinutes(1), out _));

            var roleChanging = apiKeys.Create(
                SqliteAuthenticationStore.BootstrapOwnerSubject,
                "role",
                now,
                null).CreatedApiKey!;
            using (var connection = database.ConnectionFactory.Open())
            {
                connection.Execute(
                    "UPDATE users SET role = @Role WHERE subject = @Subject;",
                    new
                    {
                        Role = PanelUserIdentity.ViewerRole,
                        Subject = SqliteAuthenticationStore.BootstrapOwnerSubject
                    });
            }

            Assert.True(apiKeys.TryValidate(roleChanging.ApiKey, now.AddMinutes(1), out var updated));
            Assert.Equal(PanelUserIdentity.ViewerRole, updated.Identity.Role);

            using (var connection = database.ConnectionFactory.Open())
            {
                connection.Execute(
                    "UPDATE users SET enabled = 0 WHERE subject = @Subject;",
                    new { Subject = SqliteAuthenticationStore.BootstrapOwnerSubject });
            }

            Assert.False(apiKeys.TryValidate(roleChanging.ApiKey, now.AddMinutes(2), out _));
        }

        [Fact]
        public void Api_key_survives_store_and_connection_factory_recreation()
        {
            using var database = new TemporaryDatabase();
            var store = database.CreateOwnerStore();
            var apiKeys = Assert.IsAssignableFrom<IPanelApiKeyStore>(store);
            var now = new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero);
            var created = apiKeys.Create("owner", "reopen", now, null).CreatedApiKey!;

            using var reopenedFactory = new SqliteConnectionFactory(database.DatabasePath);
            var reopenedStore = new SqliteAuthenticationStore(reopenedFactory);
            var reopenedApiKeys = Assert.IsAssignableFrom<IPanelApiKeyStore>(reopenedStore);

            Assert.True(reopenedApiKeys.TryValidate(created.ApiKey, now.AddMinutes(1), out var validated));
            Assert.Equal(SqliteAuthenticationStore.BootstrapOwnerSubject, validated.Identity.Subject);
        }

        [Fact]
        public async Task Api_key_revoke_is_concurrently_idempotent()
        {
            using var database = new TemporaryDatabase();
            var store = database.CreateOwnerStore();
            var apiKeys = Assert.IsAssignableFrom<IPanelApiKeyStore>(store);
            var now = new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero);
            var created = apiKeys.Create("owner", "revoke", now, null).CreatedApiKey!;

            using var firstFactory = new SqliteConnectionFactory(database.DatabasePath);
            using var secondFactory = new SqliteConnectionFactory(database.DatabasePath);
            var firstStore = Assert.IsAssignableFrom<IPanelApiKeyStore>(
                new SqliteAuthenticationStore(firstFactory));
            var secondStore = Assert.IsAssignableFrom<IPanelApiKeyStore>(
                new SqliteAuthenticationStore(secondFactory));

            var results = await Task.WhenAll(
                Task.Run(() => firstStore.Revoke("owner", created.Metadata.KeyId, now.AddMinutes(1))),
                Task.Run(() => secondStore.Revoke("owner", created.Metadata.KeyId, now.AddMinutes(1))));

            Assert.All(results, Assert.True);
            Assert.False(apiKeys.TryValidate(created.ApiKey, now.AddMinutes(2), out _));
        }

        [Fact]
        public void Api_key_list_is_stably_sorted_and_reports_active_expired_and_revoked_metadata()
        {
            using var database = new TemporaryDatabase();
            var store = database.CreateOwnerStore();
            var apiKeys = Assert.IsAssignableFrom<IPanelApiKeyStore>(store);
            var now = new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero);

            var active = apiKeys.Create("owner", "active", now, null).CreatedApiKey!;
            var expired = apiKeys.Create("owner", "expired", now, now.AddMinutes(1)).CreatedApiKey!;
            var revoked = apiKeys.Create("owner", "revoked", now, null).CreatedApiKey!;
            Assert.True(apiKeys.Revoke("owner", revoked.Metadata.KeyId, now.AddMinutes(1)));

            var listed = apiKeys.List("owner", now.AddMinutes(2));

            Assert.Equal(3, listed.Count);
            Assert.Equal(ApiKeyStatus.Active, Assert.Single(listed, key => key.KeyId == active.Metadata.KeyId).Status);
            Assert.Equal(ApiKeyStatus.Expired, Assert.Single(listed, key => key.KeyId == expired.Metadata.KeyId).Status);
            Assert.Equal(ApiKeyStatus.Revoked, Assert.Single(listed, key => key.KeyId == revoked.Metadata.KeyId).Status);
            for (var index = 1; index < listed.Count; index++)
            {
                Assert.True(
                    string.CompareOrdinal(listed[index - 1].KeyId, listed[index].KeyId) > 0,
                    "API Key IDs must break equal creation timestamps in descending order.");
            }
        }

        [Fact]
        public void Api_key_last_used_updates_at_most_once_per_hour_and_update_failure_is_ignored()
        {
            using var database = new TemporaryDatabase();
            var store = database.CreateOwnerStore();
            var apiKeys = Assert.IsAssignableFrom<IPanelApiKeyStore>(store);
            var now = new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero);
            var created = apiKeys.Create("owner", "usage", now, null).CreatedApiKey!;

            Assert.True(apiKeys.TryValidate(created.ApiKey, now, out _));
            Assert.True(apiKeys.TryValidate(created.ApiKey, now.AddMinutes(30), out _));
            using (var connection = database.ConnectionFactory.Open())
            {
                Assert.Equal(
                    now.ToUnixTimeMilliseconds(),
                    connection.ExecuteScalar<long>(
                        "SELECT last_used_utc FROM api_keys WHERE key_id = @KeyId;",
                        new { created.Metadata.KeyId }));
            }

            Assert.True(apiKeys.TryValidate(created.ApiKey, now.AddHours(1), out _));
            using (var connection = database.ConnectionFactory.Open())
            {
                Assert.Equal(
                    now.AddHours(1).ToUnixTimeMilliseconds(),
                    connection.ExecuteScalar<long>(
                        "SELECT last_used_utc FROM api_keys WHERE key_id = @KeyId;",
                        new { created.Metadata.KeyId }));
                connection.Execute(
                    @"CREATE TRIGGER fail_api_key_last_used
                      BEFORE UPDATE OF last_used_utc ON api_keys
                      BEGIN
                          SELECT RAISE(FAIL, 'last used write rejected');
                      END;");
            }

            Assert.True(apiKeys.TryValidate(created.ApiKey, now.AddHours(2), out _));
        }

        [Theory]
        [InlineData("")]
        [InlineData("7dp_k_invalid")]
        [InlineData("7dp_not-an-api-key")]
        public void Invalid_api_key_formats_are_rejected(string apiKey)
        {
            using var database = new TemporaryDatabase();
            var store = database.CreateOwnerStore();
            var apiKeys = Assert.IsAssignableFrom<IPanelApiKeyStore>(store);

            Assert.False(apiKeys.TryValidate(apiKey, DateTimeOffset.UtcNow, out _));
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

        private static string CreateUnicodeScalarName(int scalarCount) =>
            new StringBuilder().Insert(0, char.ConvertFromUtf32(0x1F642), scalarCount).ToString();

        private static string ReplaceLastBase64UrlCharacter(string apiKey)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
            var finalIndex = apiKey.Length - 1;
            var canonicalIndex = alphabet.IndexOf(apiKey[finalIndex]);
            Assert.NotEqual(-1, canonicalIndex);
            Assert.Equal(0, canonicalIndex & 0x03);
            var alternateCharacter = alphabet[(canonicalIndex & 0x3C) | 0x01];
            return apiKey.Substring(0, finalIndex) + alternateCharacter;
        }

        private static string GetApiKeySecret(string apiKey) =>
            apiKey.Substring("7dp_k_".Length + 22 + 1);

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
