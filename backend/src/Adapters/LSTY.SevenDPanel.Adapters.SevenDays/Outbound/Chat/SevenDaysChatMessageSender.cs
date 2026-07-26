using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.Chat;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Chat
{
    public sealed class SevenDaysChatMessageSender : IChatMessageSender, IDisposable
    {
        private static readonly TimeSpan GameThreadStartTimeout = TimeSpan.FromSeconds(3);
        private readonly Func<ChatSettings> settingsAccessor;
        private readonly IOnlinePlayerQuery onlinePlayers;
        private readonly Func<SevenDaysChatDispatch, CancellationToken, Task<ChatSendStatus>> dispatch;
        private readonly Channel<PendingSend> queue;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly Task worker;
        private int disposed;

        public SevenDaysChatMessageSender(
            IChatSettingsStore settingsStore,
            IOnlinePlayerQuery onlinePlayers)
            : this(
                () => (settingsStore ?? throw new ArgumentNullException(nameof(settingsStore))).Get(),
                onlinePlayers,
                DispatchNativeAsync,
                16)
        {
        }

        internal SevenDaysChatMessageSender(
            Func<ChatSettings> settingsAccessor,
            IOnlinePlayerQuery onlinePlayers,
            Func<SevenDaysChatDispatch, CancellationToken, Task<ChatSendStatus>> dispatch,
            int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.settingsAccessor = settingsAccessor ?? throw new ArgumentNullException(nameof(settingsAccessor));
            this.onlinePlayers = onlinePlayers ?? throw new ArgumentNullException(nameof(onlinePlayers));
            this.dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
            queue = Channel.CreateBounded<PendingSend>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
            worker = Task.Run(ProcessAsync);
        }

        public Task<ChatSendResult> SendGlobalAsync(
            string message,
            CancellationToken cancellationToken) =>
            Enqueue(new PendingSend(null, message, cancellationToken));

        public Task<ChatSendResult> SendPrivateAsync(
            string targetCrossplatformId,
            string message,
            CancellationToken cancellationToken) =>
            Enqueue(new PendingSend(targetCrossplatformId, message, cancellationToken));

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            queue.Writer.TryComplete();
            lifetime.Cancel();
            try { worker.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
            lifetime.Dispose();
        }

        private Task<ChatSendResult> Enqueue(PendingSend request)
        {
            if (request.CancellationToken.IsCancellationRequested)
                return Task.FromResult(ChatSendResult.Failed(ChatSendStatus.Cancelled));
            if (Volatile.Read(ref disposed) != 0 || !queue.Writer.TryWrite(request))
                return Task.FromResult(ChatSendResult.Failed(ChatSendStatus.QueueFull));
            return request.Completion.Task;
        }

        private async Task ProcessAsync()
        {
            try
            {
                while (await queue.Reader.WaitToReadAsync(lifetime.Token).ConfigureAwait(false))
                {
                    while (queue.Reader.TryRead(out var request))
                    {
                        ChatSendStatus status;
                        if (request.CancellationToken.IsCancellationRequested)
                        {
                            status = ChatSendStatus.Cancelled;
                        }
                        else
                        {
                            status = await ProcessOneAsync(request).ConfigureAwait(false);
                        }

                        request.Completion.TrySetResult(status == ChatSendStatus.Accepted
                            ? ChatSendResult.Accepted()
                            : ChatSendResult.Failed(status));
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                while (queue.Reader.TryRead(out var pending))
                    pending.Completion.TrySetResult(ChatSendResult.Failed(ChatSendStatus.NotReady));
            }
        }

        private async Task<ChatSendStatus> ProcessOneAsync(PendingSend request)
        {
            try
            {
                var settings = ChatValidation.Normalize(settingsAccessor());
                if (!settings.IsEnabled) return ChatSendStatus.Disabled;

                int? targetEntityId = null;
                if (request.TargetCrossplatformId != null)
                {
                    var snapshot = await onlinePlayers.GetOnlineAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                    var target = snapshot.Players.FirstOrDefault(player =>
                        string.Equals(
                            player.CrossplatformIdentity?.CombinedId,
                            request.TargetCrossplatformId,
                            StringComparison.Ordinal));
                    if (target == null) return ChatSendStatus.TargetOffline;
                    targetEntityId = target.EntityId;
                }

                var nativeRequest = new SevenDaysChatDispatch(
                    request.TargetCrossplatformId == null ? ChatChannel.Global : ChatChannel.Whisper,
                    request.TargetCrossplatformId == null
                        ? settings.GlobalServerName
                        : settings.WhisperServerName,
                    request.Message,
                    request.TargetCrossplatformId,
                    targetEntityId);
                return await dispatch(nativeRequest, CancellationToken.None).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return ChatSendStatus.NotReady;
            }
            catch (OperationCanceledException)
            {
                return ChatSendStatus.NotReady;
            }
            catch (ObjectDisposedException)
            {
                return ChatSendStatus.NotReady;
            }
            catch
            {
                return ChatSendStatus.Unknown;
            }
        }

        private static async Task<ChatSendStatus> DispatchNativeAsync(
            SevenDaysChatDispatch request,
            CancellationToken cancellationToken)
        {
            return await GameThreadDispatcher.Enqueue(
                    "7dpanel-chat-send",
                    () => DispatchNative(request),
                    GameThreadStartTimeout,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        private static ChatSendStatus DispatchNative(SevenDaysChatDispatch request)
        {
            var senderName = string.IsNullOrWhiteSpace(request.SenderName)
                ? Localization.Get("xuiChatServer", false) ?? "Server"
                : request.SenderName!;
            var gameMessage = global::Utils.CreateGameMessage(senderName, request.Message);

            if (request.Channel == ChatChannel.Global)
            {
                var package = NetPackageManager.GetPackage<NetPackageChat>()
                    .Setup(EChatType.Global, -1, gameMessage, null, EMessageSender.None,
                        GeneratedTextManager.BbCodeSupportMode.Supported);
                ConnectionManager.Instance.SendPackage(package, true, -1, -1, -1, null, 192);
                return ChatSendStatus.Accepted;
            }

            if (!request.TargetEntityId.HasValue || request.TargetCrossplatformId == null)
                return ChatSendStatus.TargetOffline;
            var client = ConnectionManager.Instance.Clients.ForEntityId(request.TargetEntityId.Value);
            if (client == null || !string.Equals(
                    client.CrossplatformId?.CombinedString,
                    request.TargetCrossplatformId,
                    StringComparison.Ordinal))
            {
                return ChatSendStatus.TargetOffline;
            }

            var privatePackage = NetPackageManager.GetPackage<NetPackageChat>()
                .Setup(EChatType.Whisper, -1, gameMessage,
                    new List<int> { request.TargetEntityId.Value }, EMessageSender.None,
                    GeneratedTextManager.BbCodeSupportMode.Supported);
            client.SendPackage(privatePackage);
            return ChatSendStatus.Accepted;
        }

        private sealed class PendingSend
        {
            public PendingSend(
                string? targetCrossplatformId,
                string message,
                CancellationToken cancellationToken)
            {
                TargetCrossplatformId = targetCrossplatformId;
                Message = message ?? throw new ArgumentNullException(nameof(message));
                CancellationToken = cancellationToken;
            }

            public string? TargetCrossplatformId { get; }
            public string Message { get; }
            public CancellationToken CancellationToken { get; }
            public TaskCompletionSource<ChatSendResult> Completion { get; } =
                new TaskCompletionSource<ChatSendResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    internal sealed class SevenDaysChatDispatch
    {
        public SevenDaysChatDispatch(
            ChatChannel channel,
            string? senderName,
            string message,
            string? targetCrossplatformId,
            int? targetEntityId)
        {
            Channel = channel;
            SenderName = senderName;
            Message = message;
            TargetCrossplatformId = targetCrossplatformId;
            TargetEntityId = targetEntityId;
        }

        public ChatChannel Channel { get; }
        public string? SenderName { get; }
        public string Message { get; }
        public string? TargetCrossplatformId { get; }
        public int? TargetEntityId { get; }
    }
}
