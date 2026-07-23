using System;
using System.Reflection;
using System.Security.Claims;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Authentication;
using LSTY.SevenDPanel.Hosting.Authentication;
using Microsoft.Owin.Security;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class AuthenticationTests
    {
        [Fact]
        public void Panel_user_identity_exposes_a_role()
        {
            var constructor = typeof(PanelUserIdentity).GetConstructor(
                new[] { typeof(string), typeof(string), typeof(string) });

            Assert.NotNull(constructor);
            var identity = (PanelUserIdentity)constructor!.Invoke(
                new object[] { "owner-subject", "Owner", PanelUserIdentity.AdminRole });

            Assert.Equal(PanelUserIdentity.AdminRole, identity.Role);
        }

        [Theory]
        [InlineData("")]
        [InlineData("Operator")]
        public void Panel_user_identity_rejects_unknown_roles(string role)
        {
            var constructor = typeof(PanelUserIdentity).GetConstructor(
                new[] { typeof(string), typeof(string), typeof(string) });

            Assert.NotNull(constructor);
            var exception = Assert.Throws<TargetInvocationException>(() => constructor!.Invoke(
                new object[] { "owner-subject", "Owner", role }));
            Assert.IsType<ArgumentException>(exception.InnerException);
        }

        [Fact]
        public void Persistent_credentials_are_exact_and_return_stable_identity()
        {
            var expected = new PanelUserIdentity(
                "owner-subject",
                "Owner",
                PanelUserIdentity.OwnerRole);
            var verifier = new PanelCredentialVerifier(
                new TestCredentialStore(expected, "pass:word"));

            Assert.True(verifier.TryVerify("Owner", "pass:word", out var identity));
            Assert.Equal(expected.Subject, identity.Subject);
            Assert.Equal(expected.Username, identity.Username);
            Assert.False(verifier.TryVerify("owner", "pass:word", out _));
            Assert.False(verifier.TryVerify("Owner", "pass", out _));
            Assert.False(verifier.TryVerify("Owner", "pass:word ", out _));
        }

        [Fact]
        public void Persistent_access_token_bridge_issues_opaque_token_and_rebuilds_current_identity()
        {
            var currentUtc = DateTimeOffset.UtcNow;
            var issuedUtc = new DateTimeOffset(
                currentUtc.Year,
                currentUtc.Month,
                currentUtc.Day,
                currentUtc.Hour,
                currentUtc.Minute,
                currentUtc.Second,
                TimeSpan.Zero);
            var expiresUtc = issuedUtc.AddMinutes(5);
            var original = new PanelUserIdentity(
                "owner-subject",
                "Owner",
                PanelUserIdentity.AdminRole);
            var credentials = new TestCredentialStore(original, "password");
            var tokens = new TestAccessTokenStore("7dp_t_opaque-token");
            var provider = new PersistentBearerCredentialProvider(
                tokens,
                new TestApiKeyStore(),
                credentials);

            var token = provider.Issue(CreateTicket(original, issuedUtc, expiresUtc));

            Assert.Equal("7dp_t_opaque-token", token);
            Assert.Equal(original.Subject, tokens.IssuedIdentity?.Subject);
            Assert.Equal(original.Username, tokens.IssuedIdentity?.Username);
            Assert.Equal(issuedUtc, tokens.IssuedUtc);
            Assert.Equal(expiresUtc, tokens.ExpiresUtc);

            credentials.Identity = new PanelUserIdentity(
                original.Subject,
                "Renamed Owner",
                PanelUserIdentity.AdminRole);
            Assert.True(provider.TryReceive(token, out var received));

            Assert.Equal(
                original.Subject,
                received.Identity.FindFirst(ClaimTypes.NameIdentifier)?.Value);
            Assert.Equal(
                "Renamed Owner",
                received.Identity.FindFirst(ClaimTypes.Name)?.Value);
            Assert.Equal(
                PanelUserIdentity.AdminRole,
                received.Identity.FindFirst(ClaimTypes.Role)?.Value);
            Assert.Equal("sqlite", received.Identity.FindFirst("identity_source")?.Value);
            Assert.Equal(issuedUtc, received.Properties.IssuedUtc);
            Assert.Equal(expiresUtc, received.Properties.ExpiresUtc);

            credentials.Active = false;
            Assert.False(provider.TryReceive(token, out _));
        }

        [Fact]
        public void Persistent_bearer_provider_routes_api_keys_without_access_token_fallback()
        {
            var now = new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero);
            var original = new PanelUserIdentity(
                "owner-subject",
                "Owner",
                PanelUserIdentity.OwnerRole);
            var current = new PanelUserIdentity(
                original.Subject,
                "Viewer Owner",
                PanelUserIdentity.ViewerRole);
            var credentials = new TestCredentialStore(current, "password");
            var accessTokens = new TestAccessTokenStore("7dp_t_access-token");
            var apiKey = "7dp_k_0123456789012345678901_0123456789012345678901234567890123456789012";
            var apiKeys = new TestApiKeyStore(
                apiKey,
                new StoredApiKey(
                    "0123456789012345678901",
                    original,
                    "automation",
                    now,
                    null,
                    null,
                    null,
                    now));
            var provider = new PersistentBearerCredentialProvider(accessTokens, apiKeys, credentials);

            Assert.True(provider.TryReceive(apiKey, out var ticket));
            Assert.Equal(0, accessTokens.ValidationCount);
            Assert.Equal(1, apiKeys.ValidationCount);
            Assert.Equal(
                PanelUserIdentity.ViewerRole,
                ticket.Identity.FindFirst(ClaimTypes.Role)?.Value);
            Assert.Equal(
                "api_key",
                ticket.Identity.FindFirst(PanelClaimTypes.CredentialType)?.Value);
        }

        [Fact]
        public void Persistent_bearer_provider_does_not_fall_back_from_malformed_api_key_prefix()
        {
            var identity = new PanelUserIdentity(
                "owner-subject",
                "Owner",
                PanelUserIdentity.OwnerRole);
            var credentials = new TestCredentialStore(identity, "password");
            var accessTokens = new TestAccessTokenStore("7dp_k_not-an-api-key");
            var apiKeys = new TestApiKeyStore();
            var provider = new PersistentBearerCredentialProvider(accessTokens, apiKeys, credentials);

            Assert.False(provider.TryReceive("7dp_k_not-an-api-key", out _));
            Assert.Equal(0, accessTokens.ValidationCount);
            Assert.Equal(1, apiKeys.ValidationCount);
        }

        [Fact]
        public void Fallback_ticket_format_rejects_all_self_contained_tokens()
        {
            var format = RejectingAuthenticationTicketFormat.Instance;

            Assert.Null(format.Unprotect("self-contained-token"));
            Assert.Throws<InvalidOperationException>(() =>
            {
                var now = DateTimeOffset.UtcNow;
                format.Protect(CreateTicket(
                    new PanelUserIdentity(
                        "owner-subject",
                        "Owner",
                        PanelUserIdentity.OwnerRole),
                    now,
                    now.AddMinutes(5)));
            });
        }

        [Fact]
        public void Authentication_limiter_enforces_window_and_bucket_capacity()
        {
            var now = new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero);
            var limiter = new AuthenticationAttemptLimiter(
                2,
                2,
                TimeSpan.FromMinutes(1),
                () => now);

            Assert.True(limiter.TryAcquire("127.0.0.1", out _));
            Assert.True(limiter.TryAcquire("127.0.0.1", out _));
            Assert.False(limiter.TryAcquire("127.0.0.1", out var retryAfter));
            Assert.Equal(TimeSpan.FromMinutes(1), retryAfter);
            Assert.True(limiter.TryAcquire("127.0.0.2", out _));
            Assert.False(limiter.TryAcquire("127.0.0.3", out _));

            now = now.AddMinutes(1);
            Assert.True(limiter.TryAcquire("127.0.0.1", out _));
            Assert.True(limiter.TryAcquire("127.0.0.3", out _));
        }

        private static AuthenticationTicket CreateTicket(
            PanelUserIdentity panelIdentity,
            DateTimeOffset issuedUtc,
            DateTimeOffset expiresUtc)
        {
            var identity = new ClaimsIdentity("Bearer");
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, panelIdentity.Subject));
            identity.AddClaim(new Claim(ClaimTypes.Name, panelIdentity.Username));
            identity.AddClaim(new Claim(ClaimTypes.Role, panelIdentity.Role));
            return new AuthenticationTicket(
                identity,
                new AuthenticationProperties
                {
                    IssuedUtc = issuedUtc,
                    ExpiresUtc = expiresUtc
                });
        }

        private sealed class TestCredentialStore : IPanelCredentialStore
        {
            private readonly string password;

            public TestCredentialStore(PanelUserIdentity identity, string password)
            {
                Identity = identity;
                this.password = password;
            }

            public PanelUserIdentity Identity { get; set; }
            public bool Active { get; set; } = true;

            public bool TryVerify(
                string username,
                string suppliedPassword,
                out PanelUserIdentity identity)
            {
                identity = null!;
                if (!Active ||
                    !string.Equals(username, Identity.Username, StringComparison.Ordinal) ||
                    !string.Equals(suppliedPassword, password, StringComparison.Ordinal))
                {
                    return false;
                }

                identity = Identity;
                return true;
            }

            public bool TryGetActive(string subject, out PanelUserIdentity identity)
            {
                identity = null!;
                if (!Active ||
                    !string.Equals(subject, Identity.Subject, StringComparison.Ordinal))
                {
                    return false;
                }

                identity = Identity;
                return true;
            }
        }

        private sealed class TestAccessTokenStore : IPanelAccessTokenStore
        {
            private readonly string token;
            private StoredAccessToken? storedToken;

            public TestAccessTokenStore(string token)
            {
                this.token = token;
            }

            public PanelUserIdentity? IssuedIdentity { get; private set; }
            public DateTimeOffset IssuedUtc { get; private set; }
            public DateTimeOffset ExpiresUtc { get; private set; }
            public int ValidationCount { get; private set; }

            public string Issue(
                PanelUserIdentity identity,
                DateTimeOffset issuedUtc,
                DateTimeOffset expiresUtc)
            {
                IssuedIdentity = identity;
                IssuedUtc = issuedUtc;
                ExpiresUtc = expiresUtc;
                storedToken = new StoredAccessToken(identity, issuedUtc, expiresUtc);
                return token;
            }

            public bool TryValidate(
                string suppliedToken,
                DateTimeOffset utcNow,
                out StoredAccessToken accessToken)
            {
                ValidationCount++;
                accessToken = null!;
                if (storedToken == null ||
                    !string.Equals(suppliedToken, token, StringComparison.Ordinal) ||
                    storedToken.ExpiresUtc <= utcNow)
                {
                    return false;
                }

                accessToken = storedToken;
                return true;
            }
        }

        private sealed class TestApiKeyStore : IPanelApiKeyStore
        {
            private readonly string? apiKey;
            private readonly StoredApiKey? storedApiKey;

            public TestApiKeyStore()
            {
            }

            public TestApiKeyStore(string apiKey, StoredApiKey storedApiKey)
            {
                this.apiKey = apiKey;
                this.storedApiKey = storedApiKey;
            }

            public int ValidationCount { get; private set; }

            public ApiKeyCreateResult Create(
                string subject,
                string name,
                DateTimeOffset createdUtc,
                DateTimeOffset? expiresUtc) =>
                throw new NotSupportedException();

            public IReadOnlyList<StoredApiKey> List(string subject, DateTimeOffset utcNow) =>
                throw new NotSupportedException();

            public bool Revoke(string subject, string keyId, DateTimeOffset revokedUtc) =>
                throw new NotSupportedException();

            public bool TryValidate(
                string suppliedApiKey,
                DateTimeOffset utcNow,
                out StoredApiKey apiKey)
            {
                ValidationCount++;
                apiKey = null!;
                if (!string.Equals(suppliedApiKey, this.apiKey, StringComparison.Ordinal) ||
                    storedApiKey == null)
                {
                    return false;
                }

                apiKey = storedApiKey;
                return true;
            }
        }
    }
}
