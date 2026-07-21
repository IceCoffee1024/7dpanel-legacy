using System;
using System.Security.Cryptography;
using Dapper;
using LSTY.SevenDPanel.Hosting.Authentication;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite
{
    public sealed class SqliteAuthenticationStore :
        IPanelCredentialStore,
        IPanelAccessTokenStore
    {
        public const string BootstrapOwnerSubject = "owner";
        public const int MaximumAccessTokenCount = 128;

        private const int PasswordIterationCount = 600000;
        private const int PasswordSaltSize = 16;
        private const int PasswordHashSize = 32;
        private const int TokenIdSize = 16;
        private const int TokenSecretSize = 32;
        private const string TokenPrefix = "7dp_";

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
                      password_salt,
                      password_hash,
                      password_iterations,
                      enabled,
                      updated_utc)
                  VALUES (
                      @Subject,
                      @Username,
                      @PasswordSalt,
                      @PasswordHash,
                      @PasswordIterations,
                      1,
                      @UpdatedUtc)
                  ON CONFLICT(subject) DO UPDATE SET
                      username = excluded.username,
                      password_salt = excluded.password_salt,
                      password_hash = excluded.password_hash,
                      password_iterations = excluded.password_iterations,
                      enabled = 1,
                      updated_utc = excluded.updated_utc;",
                new
                {
                    Subject = BootstrapOwnerSubject,
                    Username = username,
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

            identity = new PanelUserIdentity(row.Subject, row.Username);
            return true;
        }

        public bool TryGetActive(string subject, out PanelUserIdentity identity)
        {
            identity = null!;
            if (string.IsNullOrEmpty(subject)) return false;

            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<UserIdentityRow>(
                @"SELECT subject AS Subject, username AS Username
                  FROM users
                  WHERE subject = @Subject
                    AND enabled = 1;",
                new { Subject = subject });
            if (row == null) return false;

            identity = new PanelUserIdentity(row.Subject, row.Username);
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
                @"SELECT subject AS Subject, username AS Username
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
                new PanelUserIdentity(row.Subject, row.Username),
                DateTimeOffset.FromUnixTimeMilliseconds(row.IssuedUtc),
                DateTimeOffset.FromUnixTimeMilliseconds(row.ExpiresUtc));
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

        private static bool TryBase64UrlDecode(string value, out byte[] decoded)
        {
            decoded = Array.Empty<byte>();
            if (string.IsNullOrEmpty(value) || value.Length % 4 == 1) return false;

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
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private class UserIdentityRow
        {
            public string Subject { get; set; } = string.Empty;
            public string Username { get; set; } = string.Empty;
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
    }
}
