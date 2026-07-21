using System;
using System.Security.Claims;
using System.Text;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Authentication;
using LSTY.SevenDPanel.Hosting;
using Microsoft.Owin.Security;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class AuthenticationTests
    {
        [Fact]
        public void Configured_credentials_are_exact_and_password_may_contain_colons()
        {
            var options = PanelAuthenticationOptions.FromBinding(
                true,
                "Owner",
                "pass:word",
                allowInsecureHttp: true);
            var verifier = new PanelCredentialVerifier(options);

            Assert.True(verifier.Verify("Owner", "pass:word"));
            Assert.False(verifier.Verify("owner", "pass:word"));
            Assert.False(verifier.Verify("Owner", "pass"));
            Assert.False(verifier.Verify("Owner", "pass:word "));
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
        public void Access_token_store_revokes_oldest_and_expires_tokens()
        {
            var now = new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero);
            var sequence = 0;
            using var provider = new InMemoryAccessTokenProvider(
                2,
                () => now,
                () => "token-" + (++sequence));
            var ticket = CreateTicket(now.AddMinutes(5));

            var first = provider.Issue(ticket);
            var second = provider.Issue(ticket);
            var third = provider.Issue(ticket);

            Assert.False(provider.TryReceive(first, out _));
            Assert.True(provider.TryReceive(second, out _));
            Assert.True(provider.TryReceive(third, out _));

            now = now.AddMinutes(6);
            Assert.False(provider.TryReceive(second, out _));
            Assert.False(provider.TryReceive(third, out _));
            Assert.Equal(0, provider.Count);
        }

        [Fact]
        public void Disposing_access_token_store_invalidates_all_tokens()
        {
            var now = new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero);
            var provider = new InMemoryAccessTokenProvider(
                2,
                () => now,
                () => "token");
            var token = provider.Issue(CreateTicket(now.AddMinutes(5)));

            provider.Dispose();

            Assert.False(provider.TryReceive(token, out _));
            Assert.Throws<ObjectDisposedException>(() => provider.Issue(CreateTicket(now.AddMinutes(5))));
        }

        [Fact]
        public void Fallback_ticket_format_rejects_all_self_contained_tokens()
        {
            var format = RejectingAuthenticationTicketFormat.Instance;

            Assert.Null(format.Unprotect("self-contained-token"));
            Assert.Throws<InvalidOperationException>(() =>
            {
                format.Protect(CreateTicket(DateTimeOffset.UtcNow.AddMinutes(5)));
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

        private static AuthenticationTicket CreateTicket(DateTimeOffset expiresUtc)
        {
            var identity = new ClaimsIdentity("Bearer");
            identity.AddClaim(new Claim(ClaimTypes.Name, "Owner"));
            return new AuthenticationTicket(
                identity,
                new AuthenticationProperties { ExpiresUtc = expiresUtc });
        }
    }
}
