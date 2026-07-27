using System;
using System.Threading;
using LSTY.SevenDPanel.Application.GeoIp;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Inbound.AccessPolicies
{
    internal sealed record SevenDaysGeoIpJoinSnapshot(
        object ClientHandle,
        string IpAddress,
        string? CrossplatformId,
        bool IsConfirmedNativeAdministrator);

    public sealed class SevenDaysGeoIpJoinPolicyRuntime : IModRuntime, IDisposable
    {
        private readonly EvaluateGeoIpJoinUseCase evaluateJoin;
        private readonly Func<Action<SevenDaysGeoIpJoinSnapshot>, IDisposable> subscribe;
        private readonly Action<object, string> reject;
        private IDisposable? subscription;
        private int started;
        private int stopped;

        public SevenDaysGeoIpJoinPolicyRuntime(EvaluateGeoIpJoinUseCase evaluateJoin)
            : this(evaluateJoin, SubscribeNative, RejectNative)
        {
        }

        internal SevenDaysGeoIpJoinPolicyRuntime(
            EvaluateGeoIpJoinUseCase evaluateJoin,
            Func<Action<SevenDaysGeoIpJoinSnapshot>, IDisposable> subscribe,
            Action<object, string> reject)
        {
            this.evaluateJoin = evaluateJoin ?? throw new ArgumentNullException(nameof(evaluateJoin));
            this.subscribe = subscribe ?? throw new ArgumentNullException(nameof(subscribe));
            this.reject = reject ?? throw new ArgumentNullException(nameof(reject));
        }

        public void Start()
        {
            if (Interlocked.CompareExchange(ref started, 1, 0) != 0) return;
            subscription = subscribe(OnPlayerJoined);
        }

        public void MarkGameReady()
        {
        }

        public void Stop()
        {
            if (Interlocked.Exchange(ref stopped, 1) != 0) return;
            Interlocked.Exchange(ref subscription, null)?.Dispose();
        }

        public void Dispose() => Stop();

        private void OnPlayerJoined(SevenDaysGeoIpJoinSnapshot snapshot)
        {
            var decision = evaluateJoin.Execute(new GeoIpJoinAttempt(
                snapshot.IpAddress,
                snapshot.CrossplatformId,
                snapshot.IsConfirmedNativeAdministrator));
            if (!decision.IsAllowed)
                reject(
                    snapshot.ClientHandle,
                    decision.RejectionMessage ?? GeoIpPolicyDecision.DefaultRejectionMessage);
        }

        private static IDisposable SubscribeNative(Action<SevenDaysGeoIpJoinSnapshot> handler)
        {
            ModEvents.ModEventHandlerDelegate<ModEvents.SPlayerJoinedGameData> callback =
                delegate(ref ModEvents.SPlayerJoinedGameData data)
                {
                    var client = data.ClientInfo;
                    if (client == null) return;
                    string ipAddress;
                    try { ipAddress = client.ip ?? string.Empty; }
                    catch { ipAddress = string.Empty; }
                    var isAdministrator = false;
                    try
                    {
                        var users = GameManager.Instance?.adminTools?.Users;
                        isAdministrator = users != null && users.GetUserPermissionLevel(client) < 1000;
                    }
                    catch
                    {
                    }
                    handler(new SevenDaysGeoIpJoinSnapshot(
                        client,
                        ipAddress,
                        client.CrossplatformId?.CombinedString,
                        isAdministrator));
                };
            ModEvents.PlayerJoinedGame.RegisterHandler(callback);
            return new CallbackSubscription(
                () => ModEvents.PlayerJoinedGame.UnregisterHandler(callback));
        }

        private static void RejectNative(object handle, string message)
        {
            GameUtils.KickPlayerForClientInfo(
                (ClientInfo)handle,
                new GameUtils.KickPlayerData(
                    GameUtils.EKickReason.ManualKick,
                    0,
                    default(DateTime),
                    message));
        }

        private sealed class CallbackSubscription : IDisposable
        {
            private Action? unsubscribe;

            public CallbackSubscription(Action unsubscribe) => this.unsubscribe = unsubscribe;

            public void Dispose() => Interlocked.Exchange(ref unsubscribe, null)?.Invoke();
        }
    }
}
