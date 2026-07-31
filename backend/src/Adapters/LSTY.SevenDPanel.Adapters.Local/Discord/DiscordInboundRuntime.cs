using System;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application.Chat;
using LSTY.SevenDPanel.Application.Discord;

namespace LSTY.SevenDPanel.Adapters.Local.Discord
{
    public sealed class DiscordInboundRuntime :
        IDiscordInboundTransportSink,
        IDiscordDeferredInteractionSink,
        IDiscordIntegrationHealthSource,
        IDiscordGatewayHealthSink,
        IDisposable
    {
        private readonly BridgeDiscordMessageToGameUseCase discordToGame;
        private readonly HandleDiscordInteractionUseCase interactions;
        private readonly BridgeGameChatToDiscordUseCase gameToDiscord;
        private readonly AcceptDiscordInteractionUseCase? deferredInteractions;
        private readonly ProcessDiscordInteractionUseCase? interactionProcessor;
        private readonly object sync = new object();
        private TaskCompletionSource<bool> drained = CompletedSignal();
        private CancellationTokenSource? processorLifetime;
        private Task? processorTask;
        private bool running;
        private bool disposed;
        private int activeHandlers;
        private DiscordHealthSection gatewayHealth = new DiscordHealthSection(
            DiscordHealthState.Unavailable,
            "discord_gateway_not_started",
            null);
        private DiscordHealthSection inboundHealth = new DiscordHealthSection(
            DiscordHealthState.Unavailable,
            "discord_inbound_runtime_not_running",
            null);
        private string? loadedGatewayBotTokenFingerprint;

        public DiscordInboundRuntime(
            BridgeDiscordMessageToGameUseCase discordToGame,
            HandleDiscordInteractionUseCase interactions,
            BridgeGameChatToDiscordUseCase gameToDiscord)
            : this(discordToGame, interactions, gameToDiscord, null, null)
        {
        }

        public DiscordInboundRuntime(
            BridgeDiscordMessageToGameUseCase discordToGame,
            HandleDiscordInteractionUseCase interactions,
            BridgeGameChatToDiscordUseCase gameToDiscord,
            AcceptDiscordInteractionUseCase? deferredInteractions,
            ProcessDiscordInteractionUseCase? interactionProcessor)
        {
            this.discordToGame = discordToGame ?? throw new ArgumentNullException(nameof(discordToGame));
            this.interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
            this.gameToDiscord = gameToDiscord ?? throw new ArgumentNullException(nameof(gameToDiscord));
            this.deferredInteractions = deferredInteractions;
            this.interactionProcessor = interactionProcessor;
        }

        public bool IsRunning
        {
            get
            {
                lock (sync) return running;
            }
        }

        public bool Start()
        {
            lock (sync)
            {
                ThrowIfDisposed();
                if (running || activeHandlers != 0) return false;
                interactionProcessor?.RecoverRunningInteractions();
                processorLifetime?.Dispose();
                processorLifetime = interactionProcessor == null
                    ? null
                    : new CancellationTokenSource();
                processorTask = processorLifetime == null
                    ? null
                    : RunProcessorAsync(processorLifetime.Token);
                drained = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                running = true;
                inboundHealth = new DiscordHealthSection(
                    DiscordHealthState.Healthy,
                    null,
                    DateTimeOffset.UtcNow);
                return true;
            }
        }

        public async Task<bool> StopAsync(
            TimeSpan drainTimeout,
            CancellationToken cancellationToken)
        {
            if (drainTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(drainTimeout));
            Task? pending;
            lock (sync)
            {
                running = false;
                inboundHealth = new DiscordHealthSection(
                    DiscordHealthState.Unavailable,
                    "discord_inbound_runtime_not_running",
                    DateTimeOffset.UtcNow);
                if (activeHandlers == 0)
                {
                    drained.TrySetResult(true);
                    pending = null;
                }
                else
                {
                    pending = drained.Task;
                }
            }

            if (pending != null)
            {
                var timeout = Task.Delay(drainTimeout, cancellationToken);
                var completed = await Task.WhenAny(pending, timeout).ConfigureAwait(false);
                if (completed == pending)
                {
                    await pending.ConfigureAwait(false);
                }
                else
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return false;
                }
            }

            return await StopProcessorAsync(drainTimeout, cancellationToken).ConfigureAwait(false);
        }

        public async Task<DiscordInboundResult> HandleMessageAsync(
            DiscordMessageCreateEnvelope message,
            CancellationToken cancellationToken)
        {
            if (!TryEnter()) return NotRunning();
            try
            {
                return await discordToGame.ExecuteAsync(message, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                Exit();
            }
        }

        public async Task<DiscordInboundResult> HandleInteractionAsync(
            DiscordInteractionEnvelope interaction,
            CancellationToken cancellationToken)
        {
            if (!TryEnter()) return NotRunning();
            try
            {
                return await interactions.ExecuteAsync(interaction, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                Exit();
            }
        }

        public DiscordInboundResult HandleGameChat(ChatMessage message)
        {
            if (!TryEnter()) return NotRunning();
            try
            {
                return gameToDiscord.Execute(message);
            }
            finally
            {
                Exit();
            }
        }

        public DiscordInboundResult AcceptInteraction(
            DiscordInteractionEnvelope interaction,
            string interactionToken)
        {
            if (!TryEnter()) return NotRunning();
            try
            {
                if (deferredInteractions == null)
                    return DiscordInboundResult.From(
                        DiscordInboundDisposition.NotRunning,
                        "discord_interaction_processing_unavailable");
                return deferredInteractions.Execute(interaction, interactionToken);
            }
            finally
            {
                Exit();
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                running = false;
                inboundHealth = new DiscordHealthSection(
                    DiscordHealthState.Unavailable,
                    "discord_inbound_runtime_not_running",
                    DateTimeOffset.UtcNow);
                if (activeHandlers == 0) drained.TrySetResult(true);
            }
        }

        public DiscordHealthSnapshot GetHealth()
        {
            lock (sync)
            {
                return new DiscordHealthSnapshot(
                    gatewayHealth,
                    inboundHealth,
                    loadedGatewayBotTokenFingerprint);
            }
        }

        public void ObserveLoadedGatewayBotTokenFingerprint(string? fingerprint)
        {
            lock (sync)
            {
                if (disposed) return;
                loadedGatewayBotTokenFingerprint = fingerprint;
            }
        }

        public void ObserveGatewayHealth(
            DiscordHealthState state,
            string? errorCode,
            DateTimeOffset observedAtUtc)
        {
            if (observedAtUtc.Offset != TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(observedAtUtc));
            lock (sync)
            {
                if (disposed) return;
                gatewayHealth = new DiscordHealthSection(state, errorCode, observedAtUtc);
            }
        }

        private bool TryEnter()
        {
            lock (sync)
            {
                ThrowIfDisposed();
                if (!running) return false;
                activeHandlers++;
                return true;
            }
        }

        private void Exit()
        {
            lock (sync)
            {
                activeHandlers--;
                if (!running && activeHandlers == 0) drained.TrySetResult(true);
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(DiscordInboundRuntime));
        }

        private static DiscordInboundResult NotRunning() => DiscordInboundResult.From(
            DiscordInboundDisposition.NotRunning,
            "discord_inbound_runtime_not_running");

        private async Task RunProcessorAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var result = await interactionProcessor!
                        .ExecuteNextAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (result == null)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private async Task<bool> StopProcessorAsync(
            TimeSpan drainTimeout,
            CancellationToken cancellationToken)
        {
            Task? pending;
            lock (sync)
            {
                processorLifetime?.Cancel();
                pending = processorTask;
                if (pending == null) return true;
            }

            var timeout = Task.Delay(drainTimeout, cancellationToken);
            var completed = await Task.WhenAny(pending, timeout).ConfigureAwait(false);
            if (completed != pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return false;
            }

            await pending.ConfigureAwait(false);
            lock (sync)
            {
                processorTask = null;
                processorLifetime?.Dispose();
                processorLifetime = null;
            }
            return true;
        }

        private static TaskCompletionSource<bool> CompletedSignal()
        {
            var signal = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            signal.SetResult(true);
            return signal;
        }
    }
}
