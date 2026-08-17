using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Authentication
{
    public sealed class PlayerAuthenticationService
    {
        private readonly PlayerWebSessionStore sessions;
        private readonly IPlayerOpenIdClient openId;
        private readonly IPlayerPersistentIdentityLookup players;

        internal PlayerAuthenticationService(
            PlayerWebSessionStore sessions,
            IPlayerOpenIdClient openId,
            IPlayerPersistentIdentityLookup players)
        {
            this.sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            this.openId = openId ?? throw new ArgumentNullException(nameof(openId));
            this.players = players ?? throw new ArgumentNullException(nameof(players));
        }

        internal PlayerLoginStart Start(Uri origin, string redirect)
        {
            if (origin == null) throw new ArgumentNullException(nameof(origin));
            if (!origin.IsAbsoluteUri ||
                (origin.Scheme != Uri.UriSchemeHttp && origin.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("An absolute HTTP origin is required.", nameof(origin));
            }
            if (!string.Equals(redirect, "/player/store", StringComparison.Ordinal))
                throw new ArgumentException("The player redirect is not allowed.", nameof(redirect));

            var challenge = sessions.CreateChallenge(origin, redirect);
            return new PlayerLoginStart(
                challenge.State,
                openId.CreateLoginUri(challenge.ReturnTo, challenge.Realm),
                challenge.ExpiresAtUtc);
        }

        internal async Task<PlayerAuthenticationCompletion> CompleteAsync(
            string? stateCookie,
            IEnumerable<KeyValuePair<string, string>> query,
            CancellationToken cancellationToken)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            var parameters = ToUniqueDictionary(query);
            if (!parameters.TryGetValue("state", out var state) ||
                string.IsNullOrWhiteSpace(stateCookie) ||
                !string.Equals(state, stateCookie, StringComparison.Ordinal) ||
                !sessions.TryConsumeChallenge(state, out var challenge))
            {
                return PlayerAuthenticationCompletion.Failed("invalid_login_state");
            }

            SteamOpenIdVerification verification;
            try
            {
                verification = await openId.VerifyAsync(
                        parameters,
                        challenge.ReturnTo,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return PlayerAuthenticationCompletion.Failed("steam_verification_failed");
            }

            if (!verification.Succeeded)
                return PlayerAuthenticationCompletion.Failed(verification.ErrorCode);

            PlayerWebIdentity? player;
            try
            {
                player = await players.FindBySteamIdAsync(
                        verification.SteamId!,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return PlayerAuthenticationCompletion.Failed("game_unavailable");
            }

            if (player == null)
                return PlayerAuthenticationCompletion.Failed("player_not_found");

            var session = sessions.CreateSession(player);
            return PlayerAuthenticationCompletion.Succeeded(
                session.SessionId,
                session.ExpiresAtUtc,
                challenge.Redirect);
        }

        internal bool TryGetSession(string? sessionId, out PlayerWebSession session) =>
            sessions.TryGetSession(sessionId, out session);

        internal bool Logout(string? sessionId) => sessions.RemoveSession(sessionId);

        private static IReadOnlyDictionary<string, string> ToUniqueDictionary(
            IEnumerable<KeyValuePair<string, string>> query)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var parameter in query)
            {
                if (result.ContainsKey(parameter.Key))
                    return new Dictionary<string, string>(StringComparer.Ordinal);
                result.Add(parameter.Key, parameter.Value);
            }

            return result;
        }
    }

    internal interface IPlayerOpenIdClient
    {
        Uri CreateLoginUri(Uri returnTo, Uri realm);

        Task<SteamOpenIdVerification> VerifyAsync(
            IReadOnlyDictionary<string, string> parameters,
            Uri expectedReturnTo,
            CancellationToken cancellationToken);
    }

    internal sealed class SteamOpenIdClient : IPlayerOpenIdClient, IDisposable
    {
        internal const string Endpoint = "https://steamcommunity.com/openid/login";
        internal const string Namespace = "http://specs.openid.net/auth/2.0";
        private const string IdentifierSelect = Namespace + "/identifier_select";
        private static readonly Regex ClaimedIdPattern = new Regex(
            "^https://steamcommunity\\.com/openid/id/([0-9]{17,18})$",
            RegexOptions.CultureInvariant);

        private readonly HttpClient httpClient;
        private readonly Action<string>? log;

        internal SteamOpenIdClient(HttpClient httpClient, Action<string>? log = null)
        {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            this.log = log;
        }

        public Uri CreateLoginUri(Uri returnTo, Uri realm)
        {
            if (returnTo == null) throw new ArgumentNullException(nameof(returnTo));
            if (realm == null) throw new ArgumentNullException(nameof(realm));
            var parameters = new[]
            {
                Pair("openid.ns", Namespace),
                Pair("openid.mode", "checkid_setup"),
                Pair("openid.return_to", returnTo.AbsoluteUri),
                Pair("openid.realm", realm.AbsoluteUri),
                Pair("openid.identity", IdentifierSelect),
                Pair("openid.claimed_id", IdentifierSelect)
            };
            return new Uri(Endpoint + "?" + EncodeForm(parameters), UriKind.Absolute);
        }

        public async Task<SteamOpenIdVerification> VerifyAsync(
            IReadOnlyDictionary<string, string> parameters,
            Uri expectedReturnTo,
            CancellationToken cancellationToken)
        {
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));
            if (expectedReturnTo == null) throw new ArgumentNullException(nameof(expectedReturnTo));
            if (Value(parameters, "openid.mode") == "cancel")
                return SteamOpenIdVerification.Failed("steam_login_cancelled");

            var claimedId = Value(parameters, "openid.claimed_id");
            var match = ClaimedIdPattern.Match(claimedId ?? string.Empty);
            if (Value(parameters, "openid.ns") != Namespace ||
                Value(parameters, "openid.mode") != "id_res" ||
                Value(parameters, "openid.op_endpoint") != Endpoint ||
                Value(parameters, "openid.return_to") != expectedReturnTo.AbsoluteUri ||
                Value(parameters, "openid.identity") != claimedId ||
                !match.Success ||
                string.IsNullOrWhiteSpace(Value(parameters, "openid.response_nonce")) ||
                string.IsNullOrWhiteSpace(Value(parameters, "openid.assoc_handle")) ||
                !HasRequiredSignedFields(Value(parameters, "openid.signed")) ||
                string.IsNullOrWhiteSpace(Value(parameters, "openid.sig")))
            {
                return SteamOpenIdVerification.Failed("invalid_steam_response");
            }

            var verificationParameters = parameters
                .Where(parameter => parameter.Key.StartsWith("openid.", StringComparison.Ordinal))
                .ToDictionary(parameter => parameter.Key, parameter => parameter.Value, StringComparer.Ordinal);
            verificationParameters["openid.mode"] = "check_authentication";
            using var content = new FormUrlEncodedContent(verificationParameters);
            HttpResponseMessage response;
            try
            {
                response = await httpClient.PostAsync(Endpoint, content, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                log?.Invoke("Steam OpenID verification request timed out.");
                return SteamOpenIdVerification.Failed("steam_verification_failed");
            }
            catch (HttpRequestException exception)
            {
                log?.Invoke("Steam OpenID verification request failed: " + exception.Message);
                return SteamOpenIdVerification.Failed("steam_verification_failed");
            }
            using (response)
            {
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    log?.Invoke(
                        "Steam OpenID verification returned HTTP " +
                        ((int)response.StatusCode).ToString() + ".");
                    return SteamOpenIdVerification.Failed("steam_verification_failed");
                }
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var valid = body.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Any(line => string.Equals(line.Trim(), "is_valid:true", StringComparison.Ordinal));
                if (!valid)
                    log?.Invoke("Steam OpenID verification returned is_valid:false.");
                return valid
                    ? SteamOpenIdVerification.SucceededWith(match.Groups[1].Value)
                    : SteamOpenIdVerification.Failed("steam_verification_failed");
            }
        }

        public void Dispose() => httpClient.Dispose();

        private static KeyValuePair<string, string> Pair(string key, string value) =>
            new KeyValuePair<string, string>(key, value);

        private static string EncodeForm(IEnumerable<KeyValuePair<string, string>> parameters)
        {
            using var content = new FormUrlEncodedContent(parameters);
            return content.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        private static string? Value(IReadOnlyDictionary<string, string> values, string key) =>
            values.TryGetValue(key, out var value) ? value : null;

        private static bool HasRequiredSignedFields(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var signed = new HashSet<string>(value!.Split(','), StringComparer.Ordinal);
            return signed.Contains("op_endpoint") &&
                   signed.Contains("claimed_id") &&
                   signed.Contains("identity") &&
                   signed.Contains("return_to") &&
                   signed.Contains("response_nonce") &&
                   signed.Contains("assoc_handle");
        }
    }

    internal sealed class PlayerWebSessionStore
    {
        internal static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);
        internal static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);
        private readonly ConcurrentDictionary<string, PlayerAuthChallenge> challenges =
            new ConcurrentDictionary<string, PlayerAuthChallenge>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, PlayerWebSession> sessions =
            new ConcurrentDictionary<string, PlayerWebSession>(StringComparer.Ordinal);
        private readonly Func<DateTimeOffset> utcClock;

        internal PlayerWebSessionStore(Func<DateTimeOffset> utcClock)
        {
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
        }

        internal PlayerAuthChallenge CreateChallenge(Uri origin, string redirect)
        {
            RemoveExpired();
            var now = UtcNow();
            var state = RandomToken();
            var returnTo = new Uri(
                origin,
                "api/oauth/steam/return?state=" + Uri.EscapeDataString(state));
            var challenge = new PlayerAuthChallenge(
                state,
                returnTo,
                origin,
                redirect,
                now.Add(ChallengeLifetime));
            if (!challenges.TryAdd(state, challenge))
                throw new InvalidOperationException("player_login_state_collision");
            return challenge;
        }

        internal bool TryConsumeChallenge(string state, out PlayerAuthChallenge challenge)
        {
            if (!challenges.TryRemove(state, out challenge!)) return false;
            return challenge.ExpiresAtUtc > UtcNow();
        }

        internal PlayerWebSession CreateSession(PlayerWebIdentity player)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            RemoveExpired();
            var session = new PlayerWebSession(
                RandomToken(),
                player,
                UtcNow().Add(SessionLifetime));
            if (!sessions.TryAdd(session.SessionId, session))
                throw new InvalidOperationException("player_session_id_collision");
            return session;
        }

        internal bool TryGetSession(string? sessionId, out PlayerWebSession session)
        {
            session = null!;
            if (string.IsNullOrWhiteSpace(sessionId))
                return false;
            var normalizedSessionId = sessionId!;
            if (!sessions.TryGetValue(normalizedSessionId, out var found))
                return false;
            if (found.ExpiresAtUtc <= UtcNow())
            {
                sessions.TryRemove(normalizedSessionId, out _);
                return false;
            }

            session = found;
            return true;
        }

        internal bool RemoveSession(string? sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return false;
            return sessions.TryRemove(sessionId!, out _);
        }

        private DateTimeOffset UtcNow()
        {
            var value = utcClock();
            if (value.Offset != TimeSpan.Zero)
                throw new InvalidOperationException("player_session_clock_not_utc");
            return value;
        }

        private void RemoveExpired()
        {
            var now = UtcNow();
            foreach (var challenge in challenges)
            {
                if (challenge.Value.ExpiresAtUtc <= now)
                    challenges.TryRemove(challenge.Key, out _);
            }
            foreach (var session in sessions)
            {
                if (session.Value.ExpiresAtUtc <= now)
                    sessions.TryRemove(session.Key, out _);
            }
        }

        private static string RandomToken()
        {
            var bytes = new byte[32];
            using (var generator = RandomNumberGenerator.Create())
                generator.GetBytes(bytes);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }

    internal sealed class PlayerAuthChallenge
    {
        internal PlayerAuthChallenge(
            string state,
            Uri returnTo,
            Uri realm,
            string redirect,
            DateTimeOffset expiresAtUtc)
        {
            State = state;
            ReturnTo = returnTo;
            Realm = realm;
            Redirect = redirect;
            ExpiresAtUtc = expiresAtUtc;
        }

        internal string State { get; }
        internal Uri ReturnTo { get; }
        internal Uri Realm { get; }
        internal string Redirect { get; }
        internal DateTimeOffset ExpiresAtUtc { get; }
    }

    internal sealed class PlayerWebSession
    {
        internal PlayerWebSession(string sessionId, PlayerWebIdentity player, DateTimeOffset expiresAtUtc)
        {
            SessionId = sessionId;
            Player = player;
            ExpiresAtUtc = expiresAtUtc;
        }

        internal string SessionId { get; }
        internal PlayerWebIdentity Player { get; }
        internal DateTimeOffset ExpiresAtUtc { get; }
    }

    internal sealed class PlayerLoginStart
    {
        internal PlayerLoginStart(string state, Uri loginUri, DateTimeOffset expiresAtUtc)
        {
            State = state;
            LoginUri = loginUri;
            ExpiresAtUtc = expiresAtUtc;
        }

        internal string State { get; }
        internal Uri LoginUri { get; }
        internal DateTimeOffset ExpiresAtUtc { get; }
    }

    internal sealed class PlayerAuthenticationCompletion
    {
        private PlayerAuthenticationCompletion(
            bool isSuccessful,
            string? errorCode,
            string? sessionId,
            DateTimeOffset? expiresAtUtc,
            string? redirect)
        {
            IsSuccessful = isSuccessful;
            ErrorCode = errorCode;
            SessionId = sessionId;
            ExpiresAtUtc = expiresAtUtc;
            Redirect = redirect;
        }

        internal bool IsSuccessful { get; }
        internal string? ErrorCode { get; }
        internal string? SessionId { get; }
        internal DateTimeOffset? ExpiresAtUtc { get; }
        internal string? Redirect { get; }

        internal static PlayerAuthenticationCompletion Failed(string code) =>
            new PlayerAuthenticationCompletion(false, code, null, null, null);

        internal static PlayerAuthenticationCompletion Succeeded(
            string sessionId,
            DateTimeOffset expiresAtUtc,
            string redirect) =>
            new PlayerAuthenticationCompletion(true, null, sessionId, expiresAtUtc, redirect);
    }

    internal sealed class SteamOpenIdVerification
    {
        private SteamOpenIdVerification(bool succeeded, string? steamId, string errorCode)
        {
            Succeeded = succeeded;
            SteamId = steamId;
            ErrorCode = errorCode;
        }

        internal bool Succeeded { get; }
        internal string? SteamId { get; }
        internal string ErrorCode { get; }

        internal static SteamOpenIdVerification SucceededWith(string steamId) =>
            new SteamOpenIdVerification(true, steamId, string.Empty);

        internal static SteamOpenIdVerification Failed(string code) =>
            new SteamOpenIdVerification(false, null, code);
    }
}
