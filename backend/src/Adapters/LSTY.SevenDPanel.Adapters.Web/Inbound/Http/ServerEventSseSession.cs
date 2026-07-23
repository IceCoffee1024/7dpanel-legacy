using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Hosting;
using LSTY.SevenDPanel.Hosting.Authentication;
using LSTY.SevenDPanel.Hosting.ServerEvents;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class ServerEventSseSession : IDisposable
    {
        private const int MailboxCapacity = 256;
        private const int ReplayLimit = 5000;
        private static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(15);
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        private readonly IServerEventStream serverEvents;
        private readonly IPanelRuntimeStatus runtimeStatus;
        private readonly IPanelCredentialStore credentialStore;
        private readonly IPanelAccessTokenStore accessTokenStore;
        private readonly IPanelApiKeyStore apiKeyStore;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly TimeSpan authorizationValidationInterval;
        private readonly TimeSpan heartbeatInterval;
        private IServerEventSubscription? subscription;
        private WelcomeEventData? welcome;
        private string? authorizationSubject;
        private string? bearerToken;
        private PanelCredentialType credentialType;
        private IReadOnlyCollection<string>? allowedRoles;
        private DateTimeOffset nextAuthorizationValidationUtc;
        private int authorizationAttempted;
        private int reservationAttempted;
        private int writeStarted;
        private int disposed;

        public ServerEventSseSession(
            IServerEventStream serverEvents,
            IPanelRuntimeStatus runtimeStatus,
            IPanelCredentialStore credentialStore,
            IPanelAccessTokenStore accessTokenStore,
            IPanelApiKeyStore apiKeyStore)
            : this(
                serverEvents,
                runtimeStatus,
                credentialStore,
                accessTokenStore,
                apiKeyStore,
                () => DateTimeOffset.UtcNow,
                DefaultHeartbeatInterval,
                DefaultHeartbeatInterval)
        {
        }

        internal ServerEventSseSession(
            IServerEventStream serverEvents,
            IPanelRuntimeStatus runtimeStatus,
            IPanelCredentialStore credentialStore,
            IPanelAccessTokenStore accessTokenStore,
            IPanelApiKeyStore apiKeyStore,
            Func<DateTimeOffset> utcNow,
            TimeSpan authorizationValidationInterval)
            : this(
                serverEvents,
                runtimeStatus,
                credentialStore,
                accessTokenStore,
                apiKeyStore,
                utcNow,
                authorizationValidationInterval,
                DefaultHeartbeatInterval)
        {
        }

        internal ServerEventSseSession(
            IServerEventStream serverEvents,
            IPanelRuntimeStatus runtimeStatus,
            IPanelCredentialStore credentialStore,
            IPanelAccessTokenStore accessTokenStore,
            IPanelApiKeyStore apiKeyStore,
            Func<DateTimeOffset> utcNow,
            TimeSpan authorizationValidationInterval,
            TimeSpan heartbeatInterval)
        {
            this.serverEvents = serverEvents ?? throw new ArgumentNullException(nameof(serverEvents));
            this.runtimeStatus = runtimeStatus ?? throw new ArgumentNullException(nameof(runtimeStatus));
            this.credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
            this.accessTokenStore = accessTokenStore ?? throw new ArgumentNullException(nameof(accessTokenStore));
            this.apiKeyStore = apiKeyStore ?? throw new ArgumentNullException(nameof(apiKeyStore));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            if (authorizationValidationInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(authorizationValidationInterval),
                    "The authorization validation interval must be positive.");
            }

            this.authorizationValidationInterval = authorizationValidationInterval;
            if (heartbeatInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(heartbeatInterval),
                    "The heartbeat interval must be positive.");
            }

            this.heartbeatInterval = heartbeatInterval;
        }

        public bool TryAuthorize(
            string subject,
            string? token,
            PanelCredentialType suppliedCredentialType,
            IReadOnlyCollection<string> suppliedAllowedRoles)
        {
            if (Volatile.Read(ref disposed) != 0)
                throw new ObjectDisposedException(nameof(ServerEventSseSession));
            if (Interlocked.Exchange(ref authorizationAttempted, 1) != 0)
                throw new InvalidOperationException("SSE authorization can only be attempted once.");
            if (string.IsNullOrWhiteSpace(subject) ||
                string.IsNullOrWhiteSpace(token) ||
                suppliedAllowedRoles == null ||
                suppliedAllowedRoles.Count == 0)
            {
                return false;
            }

            authorizationSubject = subject;
            bearerToken = token;
            credentialType = suppliedCredentialType;
            allowedRoles = suppliedAllowedRoles;
            if (RefreshAuthorization()) return true;

            authorizationSubject = null;
            bearerToken = null;
            allowedRoles = null;
            return false;
        }

        public bool TryReserve()
        {
            if (Volatile.Read(ref disposed) != 0)
                throw new ObjectDisposedException(nameof(ServerEventSseSession));
            if (Interlocked.Exchange(ref reservationAttempted, 1) != 0)
                throw new InvalidOperationException("An SSE reservation can only be attempted once.");

            if (!serverEvents.TrySubscribe(MailboxCapacity, out var candidate) || candidate == null)
                return false;

            subscription = candidate;
            welcome = new WelcomeEventData(
                ProductInfo.Name,
                ProductInfo.Version,
                runtimeStatus.State.ToString().ToLowerInvariant(),
                runtimeStatus.GameReadiness.ToString().ToLowerInvariant(),
                DateTime.UtcNow);

            if (Volatile.Read(ref disposed) == 0) return true;
            Interlocked.Exchange(ref subscription, null)?.Dispose();
            throw new ObjectDisposedException(nameof(ServerEventSseSession));
        }

        public async Task WriteAsync(
            Stream output,
            long? afterSequence,
            CancellationToken cancellationToken)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (Interlocked.Exchange(ref writeStarted, 1) != 0)
                throw new InvalidOperationException("An SSE session can only write one response.");

            var activeSubscription = subscription;
            var welcomeSnapshot = welcome;
            if (activeSubscription == null || welcomeSnapshot == null)
                throw new InvalidOperationException("The SSE subscription must be reserved before writing.");
            if (Volatile.Read(ref authorizationAttempted) == 0 || authorizationSubject == null)
                throw new InvalidOperationException("The SSE authorization must be validated before writing.");

            try
            {
                await WriteNamedEventAsync(
                    output,
                    "welcome",
                    welcomeSnapshot,
                    cancellationToken).ConfigureAwait(false);

                var replay = serverEvents.ReadAfter(
                    afterSequence,
                    ReplayLimit,
                    out var hasGap);
                var requestedSequence = afterSequence ?? 0L;
                var lastSentSequence = hasGap
                    ? replay.Count > 0
                        ? replay[0].Sequence - 1L
                        : 0L
                    : requestedSequence;
                if (hasGap)
                {
                    await WriteGapAsync(
                        output,
                        requestedSequence,
                        cancellationToken).ConfigureAwait(false);
                }

                foreach (var serverEvent in replay)
                {
                    if (serverEvent.Sequence <= lastSentSequence) continue;
                    if (AuthorizationValidationIsDue() && !RefreshAuthorization()) return;
                    await WriteServerEventAsync(
                        output,
                        serverEvent,
                        cancellationToken).ConfigureAwait(false);
                    lastSentSequence = serverEvent.Sequence;
                }

                var heartbeatDeadlineUtc = utcNow().Add(heartbeatInterval);
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (AuthorizationValidationIsDue() && !RefreshAuthorization()) return;
                    ServerEvent? serverEvent;
                    using (var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken))
                    {
                        heartbeat.CancelAfter(GetSubscriptionReadTimeout(heartbeatDeadlineUtc));
                        try
                        {
                            serverEvent = await activeSubscription
                                .ReadAsync(heartbeat.Token)
                                .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                            when (!cancellationToken.IsCancellationRequested)
                        {
                            var now = utcNow();
                            if (now >= nextAuthorizationValidationUtc && !RefreshAuthorization()) return;
                            if (now >= heartbeatDeadlineUtc)
                            {
                                await WriteTextAsync(
                                    output,
                                    ": keep-alive\n\n",
                                    cancellationToken).ConfigureAwait(false);
                                heartbeatDeadlineUtc = utcNow().Add(heartbeatInterval);
                            }
                            continue;
                        }
                    }

                    if (serverEvent == null)
                    {
                        if (activeSubscription.IsOverflowed)
                        {
                            await WriteGapAsync(
                                output,
                                lastSentSequence,
                                cancellationToken).ConfigureAwait(false);
                        }
                        return;
                    }

                    if (serverEvent.Sequence <= lastSentSequence) continue;
                    if (AuthorizationValidationIsDue() && !RefreshAuthorization()) return;
                    await WriteServerEventAsync(
                        output,
                        serverEvent,
                        cancellationToken).ConfigureAwait(false);
                    lastSentSequence = serverEvent.Sequence;
                    heartbeatDeadlineUtc = utcNow().Add(heartbeatInterval);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (IOException)
            {
            }
            catch (HttpListenerException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                Dispose();
                try { output.Close(); } catch { }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            bearerToken = null;
            authorizationSubject = null;
            allowedRoles = null;
            Interlocked.Exchange(ref subscription, null)?.Dispose();
        }

        private bool AuthorizationValidationIsDue() =>
            utcNow() >= nextAuthorizationValidationUtc;

        private TimeSpan GetSubscriptionReadTimeout(DateTimeOffset heartbeatDeadlineUtc)
        {
            var now = utcNow();
            var deadlineUtc = heartbeatDeadlineUtc < nextAuthorizationValidationUtc
                ? heartbeatDeadlineUtc
                : nextAuthorizationValidationUtc;
            var timeout = deadlineUtc - now;
            if (timeout <= TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            return timeout;
        }

        private bool RefreshAuthorization()
        {
            var subject = authorizationSubject;
            var roles = allowedRoles;
            if (string.IsNullOrEmpty(subject) || roles == null) return false;

            var now = utcNow();
            DateTimeOffset? expiresUtc = null;
            if (bearerToken != null)
            {
                if (credentialType == PanelCredentialType.AccessToken)
                {
                    if (!accessTokenStore.TryValidate(bearerToken, now, out var storedToken) ||
                        !string.Equals(
                            storedToken.Identity.Subject,
                            subject,
                            StringComparison.Ordinal))
                    {
                        return false;
                    }

                    expiresUtc = storedToken.ExpiresUtc;
                }
                else if (credentialType == PanelCredentialType.ApiKey)
                {
                    if (!apiKeyStore.TryValidate(bearerToken, now, out var storedApiKey) ||
                        !string.Equals(
                            storedApiKey.Identity.Subject,
                            subject,
                            StringComparison.Ordinal))
                    {
                        return false;
                    }

                    expiresUtc = storedApiKey.ExpiresUtc;
                }
                else
                {
                    return false;
                }
            }

            if (!credentialStore.TryGetActive(subject!, out var currentIdentity) ||
                !string.Equals(currentIdentity.Subject, subject, StringComparison.Ordinal))
            {
                return false;
            }
            if (!ContainsRole(roles, currentIdentity.Role)) return false;

            var scheduled = now.Add(authorizationValidationInterval);
            nextAuthorizationValidationUtc = expiresUtc.HasValue && expiresUtc.Value < scheduled
                ? expiresUtc.Value
                : scheduled;
            return true;
        }

        private static bool ContainsRole(IReadOnlyCollection<string> roles, string role)
        {
            foreach (var candidate in roles)
            {
                if (string.Equals(candidate, role, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        private static Task WriteServerEventAsync(
            Stream output,
            ServerEvent serverEvent,
            CancellationToken cancellationToken) =>
            WriteTextAsync(
                output,
                "id: " + serverEvent.Sequence.ToString(CultureInfo.InvariantCulture) + "\n" +
                "event: " + serverEvent.EventName + "\n" +
                "data: " + JsonConvert.SerializeObject(serverEvent.Data, JsonSettings) + "\n\n",
                cancellationToken);

        private static Task WriteGapAsync(
            Stream output,
            long afterSequence,
            CancellationToken cancellationToken) =>
            WriteNamedEventAsync(
                output,
                "gap",
                new { afterSequence },
                cancellationToken);

        private static Task WriteNamedEventAsync(
            Stream output,
            string eventName,
            object payload,
            CancellationToken cancellationToken) =>
            WriteTextAsync(
                output,
                "event: " + eventName + "\n" +
                "data: " + JsonConvert.SerializeObject(payload, JsonSettings) + "\n\n",
                cancellationToken);

        private static async Task WriteTextAsync(
            Stream output,
            string value,
            CancellationToken cancellationToken)
        {
            var bytes = Utf8.GetBytes(value);
            await output.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private sealed class WelcomeEventData
        {
            public WelcomeEventData(
                string product,
                string version,
                string hostState,
                string gameReadiness,
                DateTime connectedAtUtc)
            {
                Product = product;
                Version = version;
                HostState = hostState;
                GameReadiness = gameReadiness;
                ConnectedAtUtc = connectedAtUtc;
            }

            public string Product { get; }
            public string Version { get; }
            public string HostState { get; }
            public string GameReadiness { get; }
            public DateTime ConnectedAtUtc { get; }
        }
    }
}
