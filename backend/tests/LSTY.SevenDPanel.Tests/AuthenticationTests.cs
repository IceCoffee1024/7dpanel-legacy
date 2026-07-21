using System;
using System.Security.Claims;
using System.Text;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Authentication;
using LSTY.SevenDPanel.Hosting.Authentication;
using Microsoft.Owin.Security;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class AuthenticationTests
    {
        [Fact]
        public void Persistent_credentials_are_exact_and_return_stable_identity()
        {
            var expected = new PanelUserIdentity("owner-subject", "Owner");
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
        public void Basic_credentials_split_on_the_first_colon()
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("Owner:pass:word"));

            var parsed = BasicAuthenticationHandler.TryDecodeCredentials(encoded, out var username, out var password);

            Assert.True(parsed);
            Assert.Equal("Owner", username);
            Assert.Equal("pass:word", password);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not-base64")]
        [InlineData("T3duZXI=")]
        [InlineData("OnBhc3N3b3Jk")]
        public void Invalid_basic_credentials_are_rejected(string encoded)
        {
            Assert.False(BasicAuthenticationHandler.TryDecodeCredentials(
                encoded,
                out _,
                out _));
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
            var original = new PanelUserIdentity("owner-subject", "Owner");
            var credentials = new TestCredentialStore(original, "password");
            var tokens = new TestAccessTokenStore("opaque-token");
            var provider = new PersistentAccessTokenProvider(tokens, credentials);

            var token = provider.Issue(CreateTicket(original, issuedUtc, expiresUtc));

            Assert.Equal("opaque-token", token);
            Assert.Equal(original.Subject, tokens.IssuedIdentity?.Subject);
            Assert.Equal(original.Username, tokens.IssuedIdentity?.Username);
            Assert.Equal(issuedUtc, tokens.IssuedUtc);
            Assert.Equal(expiresUtc, tokens.ExpiresUtc);

            credentials.Identity = new PanelUserIdentity(original.Subject, "Renamed Owner");
            Assert.True(provider.TryReceive(token, out var received));

            Assert.Equal(
                original.Subject,
                received.Identity.FindFirst(ClaimTypes.NameIdentifier)?.Value);
            Assert.Equal(
                "Renamed Owner",
                received.Identity.FindFirst(ClaimTypes.Name)?.Value);
            Assert.Equal("Owner", received.Identity.FindFirst(ClaimTypes.Role)?.Value);
            Assert.Equal("sqlite", received.Identity.FindFirst("identity_source")?.Value);
            Assert.Equal(issuedUtc, received.Properties.IssuedUtc);
            Assert.Equal(expiresUtc, received.Properties.ExpiresUtc);

            credentials.Active = false;
            Assert.False(provider.TryReceive(token, out _));
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
                    new PanelUserIdentity("owner-subject", "Owner"),
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
    }
}
