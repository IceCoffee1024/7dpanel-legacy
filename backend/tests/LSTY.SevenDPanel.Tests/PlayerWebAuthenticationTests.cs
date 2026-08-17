using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Authentication;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Players")]
    [Trait("Boundary", "Web")]
    public sealed class PlayerWebAuthenticationTests
    {
        private const string SteamId = "76561198000000000";
        private static readonly Uri Origin = new Uri("https://players.example:8443/");
        private static readonly DateTimeOffset InitialUtc =
            new DateTimeOffset(2026, 8, 17, 4, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Login_uses_the_request_origin_for_the_realm_and_callback()
        {
            var clock = InitialUtc;
            var openId = new StubOpenIdClient(SteamOpenIdVerification.Failed("unused"));
            var service = CreateService(() => clock, openId, new StubPlayerLookup(null));

            var start = service.Start(Origin, "/player/store");

            Assert.Equal(Origin, openId.Realm);
            Assert.NotNull(openId.ReturnTo);
            Assert.Equal(Origin.Scheme, openId.ReturnTo!.Scheme);
            Assert.Equal(Origin.Authority, openId.ReturnTo.Authority);
            Assert.Equal("/api/oauth/steam/return", openId.ReturnTo.AbsolutePath);
            Assert.Contains("state=" + Uri.EscapeDataString(start.State), openId.ReturnTo.Query);
            Assert.Equal(InitialUtc.Add(PlayerWebSessionStore.ChallengeLifetime), start.ExpiresAtUtc);
        }

        [Theory]
        [InlineData("")]
        [InlineData("/")]
        [InlineData("/player/store/other")]
        [InlineData("https://attacker.example/")]
        public void Login_rejects_unapproved_redirects(string redirect)
        {
            var service = CreateService(
                () => InitialUtc,
                new StubOpenIdClient(SteamOpenIdVerification.Failed("unused")),
                new StubPlayerLookup(null));

            Assert.Throws<ArgumentException>(() => service.Start(Origin, redirect));
        }

        [Fact]
        public async Task Missing_or_mismatched_state_does_not_verify_with_Steam()
        {
            var openId = new StubOpenIdClient(SteamOpenIdVerification.SucceededWith(SteamId));
            var service = CreateService(
                () => InitialUtc,
                openId,
                new StubPlayerLookup(Player()));
            var start = service.Start(Origin, "/player/store");
            var cancellationToken = TestContext.Current.CancellationToken;

            var missing = await service.CompleteAsync(
                null,
                StateQuery(start.State),
                cancellationToken);
            var mismatched = await service.CompleteAsync(
                "different-state",
                StateQuery(start.State),
                cancellationToken);

            Assert.Equal("invalid_login_state", missing.ErrorCode);
            Assert.Equal("invalid_login_state", mismatched.ErrorCode);
            Assert.Equal(0, openId.VerifyCallCount);
        }

        [Fact]
        public async Task Expired_state_is_rejected_and_consumed()
        {
            var clock = InitialUtc;
            var openId = new StubOpenIdClient(SteamOpenIdVerification.SucceededWith(SteamId));
            var service = CreateService(() => clock, openId, new StubPlayerLookup(Player()));
            var start = service.Start(Origin, "/player/store");
            clock = InitialUtc.Add(PlayerWebSessionStore.ChallengeLifetime);

            var completion = await service.CompleteAsync(
                start.State,
                StateQuery(start.State),
                TestContext.Current.CancellationToken);

            Assert.Equal("invalid_login_state", completion.ErrorCode);
            Assert.Equal(0, openId.VerifyCallCount);
        }

        [Fact]
        public async Task State_can_only_create_one_player_session()
        {
            var openId = new StubOpenIdClient(SteamOpenIdVerification.SucceededWith(SteamId));
            var lookup = new StubPlayerLookup(Player());
            var service = CreateService(() => InitialUtc, openId, lookup);
            var start = service.Start(Origin, "/player/store");
            var cancellationToken = TestContext.Current.CancellationToken;

            var first = await service.CompleteAsync(
                start.State,
                StateQuery(start.State),
                cancellationToken);
            var replay = await service.CompleteAsync(
                start.State,
                StateQuery(start.State),
                cancellationToken);

            Assert.True(first.IsSuccessful);
            Assert.Equal("/player/store", first.Redirect);
            Assert.NotNull(first.SessionId);
            Assert.True(service.TryGetSession(first.SessionId, out var session));
            Assert.Equal("Nomad", session.Player.DisplayName);
            Assert.Equal("invalid_login_state", replay.ErrorCode);
            Assert.Equal(1, openId.VerifyCallCount);
            Assert.Equal(1, lookup.CallCount);
        }

        [Fact]
        public async Task Verified_Steam_identity_without_persistent_player_is_rejected()
        {
            var service = CreateService(
                () => InitialUtc,
                new StubOpenIdClient(SteamOpenIdVerification.SucceededWith(SteamId)),
                new StubPlayerLookup(null));
            var start = service.Start(Origin, "/player/store");

            var completion = await service.CompleteAsync(
                start.State,
                StateQuery(start.State),
                TestContext.Current.CancellationToken);

            Assert.False(completion.IsSuccessful);
            Assert.Equal("player_not_found", completion.ErrorCode);
        }

        [Fact]
        public async Task Player_session_expires_after_eight_hours()
        {
            var clock = InitialUtc;
            var service = CreateService(
                () => clock,
                new StubOpenIdClient(SteamOpenIdVerification.SucceededWith(SteamId)),
                new StubPlayerLookup(Player()));
            var start = service.Start(Origin, "/player/store");
            var completion = await service.CompleteAsync(
                start.State,
                StateQuery(start.State),
                TestContext.Current.CancellationToken);

            Assert.True(service.TryGetSession(completion.SessionId, out _));
            clock = InitialUtc.Add(PlayerWebSessionStore.SessionLifetime);
            Assert.False(service.TryGetSession(completion.SessionId, out _));
        }

        [Fact]
        public async Task Logout_invalidates_the_player_session_idempotently()
        {
            var service = CreateService(
                () => InitialUtc,
                new StubOpenIdClient(SteamOpenIdVerification.SucceededWith(SteamId)),
                new StubPlayerLookup(Player()));
            var start = service.Start(Origin, "/player/store");
            var completion = await service.CompleteAsync(
                start.State,
                StateQuery(start.State),
                TestContext.Current.CancellationToken);

            Assert.True(service.TryGetSession(completion.SessionId, out _));
            Assert.True(service.Logout(completion.SessionId));
            Assert.False(service.TryGetSession(completion.SessionId, out _));
            Assert.False(service.Logout(completion.SessionId));
            Assert.False(service.Logout(null));
        }

        [Fact]
        public async Task Steam_client_rejects_a_forged_claim_without_contacting_Steam()
        {
            var handler = new RecordingHandler("is_valid:true\n");
            using var httpClient = new HttpClient(handler);
            var client = new SteamOpenIdClient(httpClient);
            var returnTo = new Uri(Origin, "api/oauth/steam/return?state=state");
            var parameters = ValidSteamResponse(returnTo);
            parameters["openid.claimed_id"] = "https://attacker.example/openid/id/" + SteamId;
            parameters["openid.identity"] = parameters["openid.claimed_id"];

            var verification = await client.VerifyAsync(
                parameters,
                returnTo,
                TestContext.Current.CancellationToken);

            Assert.False(verification.Succeeded);
            Assert.Equal("invalid_steam_response", verification.ErrorCode);
            Assert.Equal(0, handler.CallCount);
        }

        [Fact]
        public async Task Steam_client_rejects_unsigned_identity_fields_without_contacting_Steam()
        {
            var handler = new RecordingHandler("is_valid:true\n");
            using var httpClient = new HttpClient(handler);
            var client = new SteamOpenIdClient(httpClient);
            var returnTo = new Uri(Origin, "api/oauth/steam/return?state=state");
            var parameters = ValidSteamResponse(returnTo);
            parameters["openid.signed"] = "op_endpoint,return_to,response_nonce,assoc_handle";

            var verification = await client.VerifyAsync(
                parameters,
                returnTo,
                TestContext.Current.CancellationToken);

            Assert.False(verification.Succeeded);
            Assert.Equal("invalid_steam_response", verification.ErrorCode);
            Assert.Equal(0, handler.CallCount);
        }

        [Fact]
        public async Task Steam_client_posts_the_response_for_server_side_verification()
        {
            var handler = new RecordingHandler("ns:" + SteamOpenIdClient.Namespace + "\nis_valid:true\n");
            using var httpClient = new HttpClient(handler);
            var client = new SteamOpenIdClient(httpClient);
            var returnTo = new Uri(Origin, "api/oauth/steam/return?state=state");

            var verification = await client.VerifyAsync(
                ValidSteamResponse(returnTo),
                returnTo,
                TestContext.Current.CancellationToken);

            Assert.True(verification.Succeeded);
            Assert.Equal(SteamId, verification.SteamId);
            Assert.Equal(1, handler.CallCount);
            Assert.Contains("openid.mode=check_authentication", handler.RequestBody);
        }

        [Fact]
        public async Task Steam_client_logs_a_rejected_verification_without_response_content()
        {
            var handler = new RecordingHandler("ns:" + SteamOpenIdClient.Namespace + "\nis_valid:false\n");
            using var httpClient = new HttpClient(handler);
            string? message = null;
            var client = new SteamOpenIdClient(httpClient, value => message = value);
            var returnTo = new Uri(Origin, "api/oauth/steam/return?state=state");

            var verification = await client.VerifyAsync(
                ValidSteamResponse(returnTo),
                returnTo,
                TestContext.Current.CancellationToken);

            Assert.False(verification.Succeeded);
            Assert.Equal("steam_verification_failed", verification.ErrorCode);
            Assert.Equal("Steam OpenID verification returned is_valid:false.", message);
            Assert.DoesNotContain("signature", message ?? string.Empty, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Steam_client_logs_non_success_HTTP_status()
        {
            var handler = new RecordingHandler("unavailable", HttpStatusCode.BadGateway);
            using var httpClient = new HttpClient(handler);
            string? message = null;
            var client = new SteamOpenIdClient(httpClient, value => message = value);
            var returnTo = new Uri(Origin, "api/oauth/steam/return?state=state");

            var verification = await client.VerifyAsync(
                ValidSteamResponse(returnTo),
                returnTo,
                TestContext.Current.CancellationToken);

            Assert.False(verification.Succeeded);
            Assert.Equal("Steam OpenID verification returned HTTP 502.", message);
        }

        [Fact]
        public async Task Steam_client_logs_network_failure()
        {
            var handler = new RecordingHandler(new HttpRequestException("proxy unavailable"));
            using var httpClient = new HttpClient(handler);
            string? message = null;
            var client = new SteamOpenIdClient(httpClient, value => message = value);
            var returnTo = new Uri(Origin, "api/oauth/steam/return?state=state");

            var verification = await client.VerifyAsync(
                ValidSteamResponse(returnTo),
                returnTo,
                TestContext.Current.CancellationToken);

            Assert.False(verification.Succeeded);
            Assert.Equal("steam_verification_failed", verification.ErrorCode);
            Assert.Equal(
                "Steam OpenID verification request failed: proxy unavailable",
                message);
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("7656119800000000", false)]
        [InlineData("76561198000000000x", false)]
        [InlineData("00000000000000000", false)]
        [InlineData(SteamId, true)]
        [InlineData("123456789012345678", true)]
        public void Persistent_lookup_validates_SteamID64_format(string? value, bool expected)
        {
            Assert.Equal(expected, SevenDaysPersistentPlayerIdentityLookup.IsSteamId(value));
        }

        [Fact]
        public async Task Persistent_lookup_forwards_the_Steam_identity_and_cancellation()
        {
            string? capturedSteamId = null;
            var capturedToken = CancellationToken.None;
            var expected = Player();
            var lookup = new SevenDaysPersistentPlayerIdentityLookup((steamId, cancellationToken) =>
            {
                capturedSteamId = steamId;
                capturedToken = cancellationToken;
                return Task.FromResult<PlayerWebIdentity?>(expected);
            });
            var token = TestContext.Current.CancellationToken;

            var actual = await lookup.FindBySteamIdAsync(SteamId, token);

            Assert.Same(expected, actual);
            Assert.Equal(SteamId, capturedSteamId);
            Assert.Equal(token, capturedToken);
        }

        private static PlayerAuthenticationService CreateService(
            Func<DateTimeOffset> clock,
            IPlayerOpenIdClient openId,
            IPlayerPersistentIdentityLookup lookup) =>
            new PlayerAuthenticationService(new PlayerWebSessionStore(clock), openId, lookup);

        private static PlayerWebIdentity Player() =>
            new PlayerWebIdentity(SteamId, "EOS_primary-player", "Nomad");

        private static IEnumerable<KeyValuePair<string, string>> StateQuery(string state)
        {
            yield return new KeyValuePair<string, string>("state", state);
        }

        private static Dictionary<string, string> ValidSteamResponse(Uri returnTo)
        {
            var claimedId = "https://steamcommunity.com/openid/id/" + SteamId;
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["openid.ns"] = SteamOpenIdClient.Namespace,
                ["openid.mode"] = "id_res",
                ["openid.op_endpoint"] = SteamOpenIdClient.Endpoint,
                ["openid.return_to"] = returnTo.AbsoluteUri,
                ["openid.claimed_id"] = claimedId,
                ["openid.identity"] = claimedId,
                ["openid.response_nonce"] = "2026-08-17T04:00:00Znonce",
                ["openid.assoc_handle"] = "association",
                ["openid.signed"] = "op_endpoint,claimed_id,identity,return_to,response_nonce,assoc_handle",
                ["openid.sig"] = "signature"
            };
        }

        private sealed class StubOpenIdClient : IPlayerOpenIdClient
        {
            private readonly SteamOpenIdVerification verification;

            public StubOpenIdClient(SteamOpenIdVerification verification)
            {
                this.verification = verification;
            }

            public Uri? ReturnTo { get; private set; }
            public Uri? Realm { get; private set; }
            public int VerifyCallCount { get; private set; }

            public Uri CreateLoginUri(Uri returnTo, Uri realm)
            {
                ReturnTo = returnTo;
                Realm = realm;
                return new Uri(SteamOpenIdClient.Endpoint);
            }

            public Task<SteamOpenIdVerification> VerifyAsync(
                IReadOnlyDictionary<string, string> parameters,
                Uri expectedReturnTo,
                CancellationToken cancellationToken)
            {
                VerifyCallCount++;
                return Task.FromResult(verification);
            }
        }

        private sealed class StubPlayerLookup : IPlayerPersistentIdentityLookup
        {
            private readonly PlayerWebIdentity? player;

            public StubPlayerLookup(PlayerWebIdentity? player)
            {
                this.player = player;
            }

            public int CallCount { get; private set; }

            public Task<PlayerWebIdentity?> FindBySteamIdAsync(
                string steamId,
                CancellationToken cancellationToken)
            {
                CallCount++;
                Assert.Equal(SteamId, steamId);
                return Task.FromResult(player);
            }
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly string responseBody;
            private readonly HttpRequestException? exception;

            public RecordingHandler(
                string responseBody,
                HttpStatusCode statusCode = HttpStatusCode.OK)
            {
                this.responseBody = responseBody;
                StatusCode = statusCode;
            }

            public RecordingHandler(HttpRequestException exception)
            {
                responseBody = string.Empty;
                this.exception = exception;
            }

            public int CallCount { get; private set; }
            public string RequestBody { get; private set; } = string.Empty;
            private HttpStatusCode StatusCode { get; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                CallCount++;
                Assert.Equal(new Uri(SteamOpenIdClient.Endpoint), request.RequestUri);
                RequestBody = await request.Content!.ReadAsStringAsync().ConfigureAwait(false);
                if (exception != null) throw exception;
                return new HttpResponseMessage(StatusCode)
                {
                    Content = new StringContent(responseBody)
                };
            }
        }
    }
}
