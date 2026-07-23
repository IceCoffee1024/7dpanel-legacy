using System;
using System.Security.Cryptography;
using Dapper;
using LSTY.SevenDPanel.Hosting.Authentication;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite
{
    public sealed class SqliteAuthenticationStore :
        IPanelCredentialStore,
        IPanelAccessTokenStore,
        IPanelApiKeyStore
    {
        public const string BootstrapOwnerSubject = "owner";
        public const int MaximumAccessTokenCount = 128;
        public const int MaximumActiveApiKeyCount = 32;

        private const int PasswordIterationCount = 1000;
        private const int PasswordSaltSize = 16;
        private const int PasswordHashSize = 32;
        private const int TokenIdSize = 16;
        private const int TokenSecretSize = 32;
        private const int ApiKeyIdSize = 16;
        private const int ApiKeySecretSize = 32;
        private const int ApiKeyIdEncodedLength = 22;
        private const int ApiKeySecretEncodedLength = 43;
        private const string TokenPrefix = "7dp_t_";
        private const string ApiKeyPrefix = "7dp_k_";
        private static readonly TimeSpan ApiKeyLastUsedWriteInterval = TimeSpan.FromHours(1);

        private readonly SqliteConnectionFactory connectionFactory;

        public SqliteAuthenticationStore(SqliteConnectionFactory connectionFactory)
        {
            this.connectionFactory = connectionFactory ??
                throw new ArgumentNullException(nameof(connectionFactory));
        }

        public void EnsureBootstrapOwner(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("A bootstrap owner username is required.", nameof(username));
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("A bootstrap owner password is required.", nameof(password));

            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var current = connection.QuerySingleOrDefault<UserCredentialRow>(
                @"SELECT
                      subject AS Subject,
                      username AS Username,
                        role AS Role,
                      password_salt AS PasswordSalt,
                      password_hash AS PasswordHash,
                      password_iterations AS PasswordIterations,
                      enabled AS Enabled
                  FROM users
                  WHERE subject = @Subject;",
                new { Subject = BootstrapOwnerSubject },
                transaction);
            var passwordMatches = current != null &&
                current.PasswordIterations > 0 &&
                current.PasswordSalt.Length > 0 &&
                current.PasswordHash.Length == PasswordHashSize &&
                FixedTimeEquals(
                    current.PasswordHash,
                    HashPassword(password, current.PasswordSalt, current.PasswordIterations));
            if (current != null &&
                string.Equals(current.Username, username, StringComparison.Ordinal) &&
                string.Equals(current.Role, PanelUserIdentity.OwnerRole, StringComparison.Ordinal) &&
                passwordMatches &&
                current.Enabled == 1)
            {
                transaction.Commit();
                return;
            }

            var salt = RandomBytes(PasswordSaltSize);
            var passwordHash = HashPassword(password, salt, PasswordIterationCount);
            var updatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            connection.Execute(
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
                      @UpdatedUtc)
                  ON CONFLICT(subject) DO UPDATE SET
                      username = excluded.username,
                      role = excluded.role,
                      password_salt = excluded.password_salt,
                      password_hash = excluded.password_hash,
                      password_iterations = excluded.password_iterations,
                      enabled = 1,
                      updated_utc = excluded.updated_utc;",
                new
                {
                    Subject = BootstrapOwnerSubject,
                    Username = username,
                    Role = PanelUserIdentity.OwnerRole,
                    PasswordSalt = salt,
                    PasswordHash = passwordHash,
                    PasswordIterations = PasswordIterationCount,
                    UpdatedUtc = updatedUtc
                },
                transaction);
            connection.Execute(
                "DELETE FROM access_tokens WHERE subject = @Subject;",
                new { Subject = BootstrapOwnerSubject },
                transaction);
            transaction.Commit();
        }

        public bool TryVerify(
            string username,
            string password,
            out PanelUserIdentity identity)
        {
            identity = null!;
            if (string.IsNullOrEmpty(username) || password == null) return false;

            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<UserCredentialRow>(
                @"SELECT
                      subject AS Subject,
                      username AS Username,
                        role AS Role,
                      password_salt AS PasswordSalt,
                      password_hash AS PasswordHash,
                      password_iterations AS PasswordIterations
                  FROM users
                  WHERE username = @Username
                    AND enabled = 1;",
                new { Username = username });
            if (row == null ||
                row.PasswordIterations <= 0 ||
                row.PasswordSalt.Length == 0 ||
                row.PasswordHash.Length != PasswordHashSize)
            {
                return false;
            }

            var candidateHash = HashPassword(password, row.PasswordSalt, row.PasswordIterations);
            if (!FixedTimeEquals(row.PasswordHash, candidateHash)) return false;

            identity = new PanelUserIdentity(row.Subject, row.Username, row.Role);
            return true;
        }

        public bool TryGetActive(string subject, out PanelUserIdentity identity)
        {
            identity = null!;
            if (string.IsNullOrEmpty(subject)) return false;

            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<UserIdentityRow>(
                                @"SELECT subject AS Subject, username AS Username, role AS Role
                  FROM users
                  WHERE subject = @Subject
                    AND enabled = 1;",
                new { Subject = subject });
            if (row == null) return false;

            identity = new PanelUserIdentity(row.Subject, row.Username, row.Role);
            return true;
        }

        public string Issue(
            PanelUserIdentity identity,
            DateTimeOffset issuedUtc,
            DateTimeOffset expiresUtc)
        {
            if (identity == null) throw new ArgumentNullException(nameof(identity));

            var normalizedIssuedUtc = issuedUtc.ToUniversalTime();
            var normalizedExpiresUtc = expiresUtc.ToUniversalTime();
            if (normalizedExpiresUtc <= normalizedIssuedUtc)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(expiresUtc),
                    "The access token expiration must be later than its issue time.");
            }

            var tokenId = Base64UrlEncode(RandomBytes(TokenIdSize));
            var secret = RandomBytes(TokenSecretSize);
            var encodedSecret = Base64UrlEncode(secret);
            var secretHash = Sha256(secret);

            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            connection.Execute(
                "DELETE FROM access_tokens WHERE expires_utc <= @IssuedUtc;",
                new { IssuedUtc = normalizedIssuedUtc.ToUnixTimeMilliseconds() },
                transaction);

            var activeUser = connection.QuerySingleOrDefault<UserIdentityRow>(
                                @"SELECT subject AS Subject, username AS Username, role AS Role
                  FROM users
                  WHERE subject = @Subject
                    AND enabled = 1;",
                new { identity.Subject },
                transaction);
            if (activeUser == null)
            {
                throw new InvalidOperationException(
                    "An access token cannot be issued for an inactive panel user.");
            }

            var tokenCount = connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM access_tokens;",
                transaction: transaction);
            var tokensToRemove = Math.Max(
                0,
                tokenCount - (MaximumAccessTokenCount - 1));
            if (tokensToRemove > 0)
            {
                connection.Execute(
                    @"DELETE FROM access_tokens
                      WHERE token_id IN (
                          SELECT token_id
                          FROM access_tokens
                          ORDER BY issued_utc ASC, token_id ASC
                          LIMIT @TokensToRemove
                      );",
                    new { TokensToRemove = tokensToRemove },
                    transaction);
            }

            connection.Execute(
                @"INSERT INTO access_tokens (
                      token_id,
                      subject,
                      secret_hash,
                      issued_utc,
                      expires_utc)
                  VALUES (
                      @TokenId,
                      @Subject,
                      @SecretHash,
                      @IssuedUtc,
                      @ExpiresUtc);",
                new
                {
                    TokenId = tokenId,
                    identity.Subject,
                    SecretHash = secretHash,
                    IssuedUtc = normalizedIssuedUtc.ToUnixTimeMilliseconds(),
                    ExpiresUtc = normalizedExpiresUtc.ToUnixTimeMilliseconds()
                },
                transaction);
            transaction.Commit();

            return TokenPrefix + tokenId + "." + encodedSecret;
        }

        public bool TryValidate(
            string token,
            DateTimeOffset utcNow,
            out StoredAccessToken storedToken)
        {
            storedToken = null!;
            if (!TryParseToken(token, out var tokenId, out var secret)) return false;

            var normalizedNow = utcNow.ToUniversalTime();
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<AccessTokenRow>(
                @"SELECT
                      u.subject AS Subject,
                      u.username AS Username,
                        u.role AS Role,
                      t.secret_hash AS SecretHash,
                      t.issued_utc AS IssuedUtc,
                      t.expires_utc AS ExpiresUtc
                  FROM access_tokens AS t
                  INNER JOIN users AS u ON u.subject = t.subject
                  WHERE t.token_id = @TokenId
                    AND t.issued_utc <= @UtcNow
                    AND t.expires_utc > @UtcNow
                    AND u.enabled = 1;",
                new
                {
                    TokenId = tokenId,
                    UtcNow = normalizedNow.ToUnixTimeMilliseconds()
                });
            if (row == null ||
                row.SecretHash.Length != PasswordHashSize ||
                !FixedTimeEquals(row.SecretHash, Sha256(secret)))
            {
                return false;
            }

            storedToken = new StoredAccessToken(
                new PanelUserIdentity(row.Subject, row.Username, row.Role),
                DateTimeOffset.FromUnixTimeMilliseconds(row.IssuedUtc),
                DateTimeOffset.FromUnixTimeMilliseconds(row.ExpiresUtc));
            return true;
        }

        public ApiKeyCreateResult Create(
            string subject,
            string name,
            DateTimeOffset createdUtc,
            DateTimeOffset? expiresUtc)
        {
            if (string.IsNullOrWhiteSpace(subject))
                return ApiKeyCreateResult.Failed(ApiKeyCreateStatus.SubjectNotFound);

            var normalizedName = (name ?? string.Empty).Trim();
            if (GetUnicodeScalarCount(normalizedName) is < 1 or > 80)
                return ApiKeyCreateResult.Failed(ApiKeyCreateStatus.InvalidName);

            var normalizedCreatedUtc = createdUtc.ToUniversalTime();
            var normalizedExpiresUtc = expiresUtc?.ToUniversalTime();
            if (normalizedExpiresUtc.HasValue && normalizedExpiresUtc.Value <= normalizedCreatedUtc)
                return ApiKeyCreateResult.Failed(ApiKeyCreateStatus.InvalidExpiration);

            var keyId = Base64UrlEncode(RandomBytes(ApiKeyIdSize));
            var secret = RandomBytes(ApiKeySecretSize);
            var apiKey = ApiKeyPrefix + keyId + "_" + Base64UrlEncode(secret);
            var secretHash = Sha256(secret);

            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var user = connection.QuerySingleOrDefault<UserIdentityRow>(
                @"SELECT subject AS Subject, username AS Username, role AS Role
                  FROM users
                  WHERE subject = @Subject
                    AND enabled = 1;",
                new { Subject = subject },
                transaction);
            if (user == null)
                return ApiKeyCreateResult.Failed(ApiKeyCreateStatus.SubjectNotFound);

            var activeKeyCount = connection.ExecuteScalar<int>(
                @"SELECT COUNT(*)
                  FROM api_keys
                  WHERE subject = @Subject
                    AND revoked_utc IS NULL;",
                new { Subject = subject },
                transaction);
            if (activeKeyCount >= MaximumActiveApiKeyCount)
                return ApiKeyCreateResult.Failed(ApiKeyCreateStatus.CapacityReached);

            connection.Execute(
                @"INSERT INTO api_keys (
                      key_id,
                      subject,
                      name,
                      secret_hash,
                      created_utc,
                      last_used_utc,
                      expires_utc,
                      revoked_utc)
                  VALUES (
                      @KeyId,
                      @Subject,
                      @Name,
                      @SecretHash,
                      @CreatedUtc,
                      NULL,
                      @ExpiresUtc,
                      NULL);",
                new
                {
                    KeyId = keyId,
                    Subject = subject,
                    Name = normalizedName,
                    SecretHash = secretHash,
                    CreatedUtc = normalizedCreatedUtc.ToUnixTimeMilliseconds(),
                    ExpiresUtc = normalizedExpiresUtc?.ToUnixTimeMilliseconds()
                },
                transaction);
            transaction.Commit();

            var metadata = new StoredApiKey(
                keyId,
                new PanelUserIdentity(user.Subject, user.Username, user.Role),
                normalizedName,
                normalizedCreatedUtc,
                null,
                normalizedExpiresUtc,
                null,
                normalizedCreatedUtc);
            return ApiKeyCreateResult.Created(new CreatedApiKey(apiKey, metadata));
        }

        public IReadOnlyList<StoredApiKey> List(string subject, DateTimeOffset utcNow)
        {
            if (string.IsNullOrWhiteSpace(subject)) return Array.Empty<StoredApiKey>();

            var normalizedNow = utcNow.ToUniversalTime();
            using var connection = connectionFactory.Open();
            var rows = connection.Query<ApiKeyRow>(
                @"SELECT
                      k.key_id AS KeyId,
                      u.subject AS Subject,
                      u.username AS Username,
                      u.role AS Role,
                      k.name AS Name,
                      k.created_utc AS CreatedUtc,
                      k.last_used_utc AS LastUsedUtc,
                      k.expires_utc AS ExpiresUtc,
                      k.revoked_utc AS RevokedUtc
                  FROM api_keys AS k
                  INNER JOIN users AS u ON u.subject = k.subject
                  WHERE k.subject = @Subject
                  ORDER BY k.created_utc DESC, k.key_id DESC;",
                new { Subject = subject });

            return rows.Select(row => ToStoredApiKey(row, normalizedNow)).ToArray();
        }

        public bool Revoke(string subject, string keyId, DateTimeOffset revokedUtc)
        {
            if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(keyId)) return false;

            using var connection = connectionFactory.Open();
            var affected = connection.Execute(
                @"UPDATE api_keys
                  SET revoked_utc = CASE
                      WHEN revoked_utc IS NULL THEN @RevokedUtc
                      ELSE revoked_utc
                  END
                  WHERE subject = @Subject
                    AND key_id = @KeyId;",
                new
                {
                    Subject = subject,
                    KeyId = keyId,
                    RevokedUtc = revokedUtc.ToUniversalTime().ToUnixTimeMilliseconds()
                });
            return affected == 1;
        }

        bool IPanelApiKeyStore.TryValidate(
            string apiKey,
            DateTimeOffset utcNow,
            out StoredApiKey storedApiKey)
        {
            storedApiKey = null!;
            if (!TryParseApiKey(apiKey, out var keyId, out var secret)) return false;

            var normalizedNow = utcNow.ToUniversalTime();
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<ApiKeyRow>(
                @"SELECT
                      k.key_id AS KeyId,
                      u.subject AS Subject,
                      u.username AS Username,
                      u.role AS Role,
                      k.name AS Name,
                      k.secret_hash AS SecretHash,
                      k.created_utc AS CreatedUtc,
                      k.last_used_utc AS LastUsedUtc,
                      k.expires_utc AS ExpiresUtc,
                      k.revoked_utc AS RevokedUtc
                  FROM api_keys AS k
                  INNER JOIN users AS u ON u.subject = k.subject
                  WHERE k.key_id = @KeyId
                    AND k.revoked_utc IS NULL
                    AND (k.expires_utc IS NULL OR k.expires_utc > @UtcNow)
                    AND u.enabled = 1;",
                new
                {
                    KeyId = keyId,
                    UtcNow = normalizedNow.ToUnixTimeMilliseconds()
                });
            if (row == null ||
                row.SecretHash.Length != PasswordHashSize ||
                !FixedTimeEquals(row.SecretHash, Sha256(secret)))
            {
                return false;
            }

            storedApiKey = ToStoredApiKey(row, normalizedNow);
            TryUpdateApiKeyLastUsed(connection, keyId, normalizedNow);
            return true;
        }

        private static byte[] HashPassword(string password, byte[] salt, int iterations)
        {
            using var deriveBytes = new Rfc2898DeriveBytes(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256);
            return deriveBytes.GetBytes(PasswordHashSize);
        }

        private static byte[] Sha256(byte[] value)
        {
            using var algorithm = SHA256.Create();
            return algorithm.ComputeHash(value);
        }

        private static byte[] RandomBytes(int size)
        {
            var value = new byte[size];
            using var generator = RandomNumberGenerator.Create();
            generator.GetBytes(value);
            return value;
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;

            var difference = 0;
            for (var index = 0; index < left.Length; index++)
            {
                difference |= left[index] ^ right[index];
            }

            return difference == 0;
        }

        private static string Base64UrlEncode(byte[] value)
        {
            return Convert.ToBase64String(value)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static bool TryParseToken(
            string token,
            out string tokenId,
            out byte[] secret)
        {
            tokenId = string.Empty;
            secret = Array.Empty<byte>();
            if (string.IsNullOrEmpty(token) ||
                !token.StartsWith(TokenPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var separatorIndex = token.IndexOf('.', TokenPrefix.Length);
            if (separatorIndex <= TokenPrefix.Length ||
                token.IndexOf('.', separatorIndex + 1) >= 0)
            {
                return false;
            }

            var encodedId = token.Substring(
                TokenPrefix.Length,
                separatorIndex - TokenPrefix.Length);
            var encodedSecret = token.Substring(separatorIndex + 1);
            if (!TryBase64UrlDecode(encodedId, out var decodedId) ||
                decodedId.Length != TokenIdSize ||
                !TryBase64UrlDecode(encodedSecret, out secret) ||
                secret.Length != TokenSecretSize)
            {
                secret = Array.Empty<byte>();
                return false;
            }

            tokenId = encodedId;
            return true;
        }

        private static bool TryParseApiKey(
            string apiKey,
            out string keyId,
            out byte[] secret)
        {
            keyId = string.Empty;
            secret = Array.Empty<byte>();
            if (string.IsNullOrEmpty(apiKey) ||
                !apiKey.StartsWith(ApiKeyPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var separatorIndex = ApiKeyPrefix.Length + ApiKeyIdEncodedLength;
            if (apiKey.Length != separatorIndex + 1 + ApiKeySecretEncodedLength ||
                apiKey[separatorIndex] != '_')
            {
                return false;
            }

            var encodedId = apiKey.Substring(ApiKeyPrefix.Length, ApiKeyIdEncodedLength);
            var encodedSecret = apiKey.Substring(separatorIndex + 1);
            if (!TryBase64UrlDecode(encodedId, out var decodedId) ||
                decodedId.Length != ApiKeyIdSize ||
                !TryBase64UrlDecode(encodedSecret, out secret) ||
                secret.Length != ApiKeySecretSize)
            {
                secret = Array.Empty<byte>();
                return false;
            }

            keyId = encodedId;
            return true;
        }

        private static StoredApiKey ToStoredApiKey(ApiKeyRow row, DateTimeOffset utcNow) =>
            new StoredApiKey(
                row.KeyId,
                new PanelUserIdentity(row.Subject, row.Username, row.Role),
                row.Name,
                DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedUtc),
                row.LastUsedUtc.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(row.LastUsedUtc.Value)
                    : null,
                row.ExpiresUtc.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(row.ExpiresUtc.Value)
                    : null,
                row.RevokedUtc.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(row.RevokedUtc.Value)
                    : null,
                utcNow);

        private static void TryUpdateApiKeyLastUsed(
            Microsoft.Data.Sqlite.SqliteConnection connection,
            string keyId,
            DateTimeOffset utcNow)
        {
            try
            {
                connection.Execute(
                    @"UPDATE api_keys
                      SET last_used_utc = @UtcNow
                      WHERE key_id = @KeyId
                        AND (last_used_utc IS NULL OR last_used_utc <= @EligibleBefore);",
                    new
                    {
                        KeyId = keyId,
                        UtcNow = utcNow.ToUnixTimeMilliseconds(),
                        EligibleBefore = utcNow.Subtract(ApiKeyLastUsedWriteInterval).ToUnixTimeMilliseconds()
                    });
            }
            catch (Microsoft.Data.Sqlite.SqliteException)
            {
            }
        }

        private static bool TryBase64UrlDecode(string value, out byte[] decoded)
        {
            decoded = Array.Empty<byte>();
            if (string.IsNullOrEmpty(value) ||
                value.Length % 4 == 1 ||
                value.IndexOfAny(new[] { '+', '/', '=' }) >= 0)
            {
                return false;
            }

            var base64 = value.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2:
                    base64 += "==";
                    break;
                case 3:
                    base64 += "=";
                    break;
            }

            try
            {
                decoded = Convert.FromBase64String(base64);
                if (!string.Equals(Base64UrlEncode(decoded), value, StringComparison.Ordinal))
                {
                    decoded = Array.Empty<byte>();
                    return false;
                }

                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static int GetUnicodeScalarCount(string value)
        {
            var count = 0;
            for (var index = 0; index < value.Length; index++)
            {
                if (char.IsHighSurrogate(value[index]) &&
                    index + 1 < value.Length &&
                    char.IsLowSurrogate(value[index + 1]))
                {
                    index++;
                }

                count++;
            }

            return count;
        }

        private class UserIdentityRow
        {
            public string Subject { get; set; } = string.Empty;
            public string Username { get; set; } = string.Empty;
            public string Role { get; set; } = string.Empty;
        }

        private sealed class UserCredentialRow : UserIdentityRow
        {
            public byte[] PasswordSalt { get; set; } = Array.Empty<byte>();
            public byte[] PasswordHash { get; set; } = Array.Empty<byte>();
            public int PasswordIterations { get; set; }
            public int Enabled { get; set; }
        }

        private sealed class AccessTokenRow : UserIdentityRow
        {
            public byte[] SecretHash { get; set; } = Array.Empty<byte>();
            public long IssuedUtc { get; set; }
            public long ExpiresUtc { get; set; }
        }

        private sealed class ApiKeyRow : UserIdentityRow
        {
            public string KeyId { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public byte[] SecretHash { get; set; } = Array.Empty<byte>();
            public long CreatedUtc { get; set; }
            public long? LastUsedUtc { get; set; }
            public long? ExpiresUtc { get; set; }
            public long? RevokedUtc { get; set; }
        }
    }
}
