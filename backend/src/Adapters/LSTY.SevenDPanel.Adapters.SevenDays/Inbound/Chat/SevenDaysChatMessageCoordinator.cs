using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.Chat;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleLogs;
using LSTY.SevenDPanel.Application.Automations;
using LSTY.SevenDPanel.Application.Chat;
using LSTY.SevenDPanel.Application.Discord;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Chat
{
    public sealed class SevenDaysChatMessageCoordinator
    {
        [ThreadStatic] private static bool sendingReplacement;
        private readonly ChatRuntimeState runtimeState;
        private readonly ColoredChatRenderer renderer;
        private readonly ServerEventLiveWindow liveWindow;
        private readonly ServerEventHub eventHub;
        private readonly ChatHistoryWriteService historyWriter;
        private readonly GameChatCommandCatalog? commands;
        private readonly SevenDaysGameChatCommandReplySender? replySender;
        private readonly IAutomationTriggerIngress? automationIngress;
        private readonly BridgeGameChatToDiscordUseCase? discordBridge;
        private readonly Action<string> log;

        public SevenDaysChatMessageCoordinator(
            ChatRuntimeState runtimeState,
            ColoredChatRenderer renderer,
            ConsoleLogService consoleLogService,
            ChatHistoryWriteService historyWriter,
            Action<string>? log = null,
            GameChatCommandCatalog? commands = null,
            SevenDaysGameChatCommandReplySender? replySender = null,
            IAutomationTriggerIngress? automationIngress = null,
            BridgeGameChatToDiscordUseCase? discordBridge = null)
            : this(
                runtimeState,
                renderer,
                (consoleLogService ?? throw new ArgumentNullException(nameof(consoleLogService))).LiveWindow,
                consoleLogService.Stream as ServerEventHub
                    ?? throw new ArgumentException("The console log service must expose its unified server event hub.", nameof(consoleLogService)),
                historyWriter,
                log,
                commands,
                replySender,
                automationIngress,
                discordBridge)
        {
        }

        internal SevenDaysChatMessageCoordinator(
            ChatRuntimeState runtimeState,
            ColoredChatRenderer renderer,
            ServerEventLiveWindow liveWindow,
            ServerEventHub eventHub,
            ChatHistoryWriteService historyWriter,
            Action<string>? log = null,
            GameChatCommandCatalog? commands = null,
            SevenDaysGameChatCommandReplySender? replySender = null,
            IAutomationTriggerIngress? automationIngress = null,
            BridgeGameChatToDiscordUseCase? discordBridge = null)
        {
            this.runtimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));
            this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            this.liveWindow = liveWindow ?? throw new ArgumentNullException(nameof(liveWindow));
            this.eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
            this.historyWriter = historyWriter ?? throw new ArgumentNullException(nameof(historyWriter));
            this.log = log ?? (_ => { });
            this.commands = commands;
            this.replySender = replySender;
            this.automationIngress = automationIngress;
            this.discordBridge = discordBridge;
        }

        public ModEvents.EModEventResult Handle(ref ModEvents.SChatMessageData data)
        {
            if (sendingReplacement) return ModEvents.EModEventResult.Continue;
            var published = false;
            try
            {
                var snapshot = runtimeState.Current;
                var messageText = data.Message ?? string.Empty;
                var entityId = data.SenderEntityId;
                var crossplatformId = data.ClientInfo?.CrossplatformId?.CombinedString;
                var sourceKind = ResolveSource(data.ClientInfo, entityId);
                var senderName = sourceKind == ChatSourceKind.System
                    ? ResolveSystemName(data.MainName, ref messageText)
                    : (string.IsNullOrWhiteSpace(data.MainName) ? "Unknown" : data.MainName);
                var channel = MapChannel(data.ChatType);
                var recipients = data.RecipientEntityIds == null ? null : data.RecipientEntityIds.ToArray();
                var isCommand = IsCommand(channel, messageText, snapshot.ChatSettings.CommandPrefixes);
                var occurredAtUtc = DateTimeOffset.UtcNow;

                var retained = liveWindow.AppendChatMessage(
                    occurredAtUtc, entityId, crossplatformId, senderName,
                    channel.ToString(), sourceKind.ToString(), messageText);
                eventHub.Publish(retained);
                published = true;
                var canonical = new ChatMessage
                {
                    Sequence = retained.Sequence,
                    OccurredAtUtc = occurredAtUtc,
                    EntityId = entityId,
                    CrossplatformId = crossplatformId,
                    SenderName = senderName,
                    Channel = channel,
                    SourceKind = sourceKind,
                    Message = messageText
                };
                if (!(isCommand && snapshot.ChatSettings.ExcludeCommandsFromHistory)) historyWriter.TryRecord(canonical);
                TryWriteAutomationTrigger(
                    retained.Sequence,
                    occurredAtUtc,
                    entityId,
                    crossplatformId,
                    messageText);

                if (isCommand && TryHandleCommand(data.ClientInfo, crossplatformId, senderName, messageText))
                    return ModEvents.EModEventResult.StopHandlersAndVanilla;

                if (sourceKind == ChatSourceKind.Player && channel == ChatChannel.Global &&
                    !string.IsNullOrWhiteSpace(crossplatformId) &&
                    snapshot.Mutes.TryGetValue(crossplatformId!, out var mute) &&
                    mute.IsActiveAt(DateTimeOffset.UtcNow))
                    return ModEvents.EModEventResult.StopHandlersAndVanilla;

                TryBridgeToDiscord(canonical, isCommand);

                if (!snapshot.ColoredSettings.IsEnabled || isCommand || string.IsNullOrWhiteSpace(messageText))
                    return ModEvents.EModEventResult.Continue;
                snapshot.Profiles.TryGetValue(crossplatformId ?? string.Empty, out var profile);
                var rendered = renderer.Render(new ColoredChatRenderRequest(
                    senderName, crossplatformId, entityId, channel, sourceKind,
                    messageText, snapshot.ColoredSettings, profile));
                sendingReplacement = true;
                try { renderer.Send(data.ChatType, recipients, rendered); }
                finally { sendingReplacement = false; }
                return ModEvents.EModEventResult.StopHandlersAndVanilla;
            }
            catch (Exception exception)
            {
                try { log("Chat message processing failed open: " + exception.GetType().Name + "."); } catch { }
                if (!published)
                {
                    try
                    {
                        var retained = liveWindow.AppendChatMessage(
                            DateTimeOffset.UtcNow, data.SenderEntityId, null,
                            string.IsNullOrWhiteSpace(data.MainName) ? "Unknown" : data.MainName,
                            MapChannel(data.ChatType).ToString(), ChatSourceKind.System.ToString(), data.Message ?? string.Empty);
                        eventHub.Publish(retained);
                    }
                    catch { }
                }
                return ModEvents.EModEventResult.Continue;
            }
        }

        private static ChatSourceKind ResolveSource(ClientInfo? clientInfo, int entityId)
        {
            if (entityId == -1 || clientInfo == null) return ChatSourceKind.System;
            try
            {
                return GameManager.Instance.adminTools.Users.GetUserPermissionLevel(clientInfo) == 0
                    ? ChatSourceKind.Administrator
                    : ChatSourceKind.Player;
            }
            catch { return ChatSourceKind.Player; }
        }

        private static string ResolveSystemName(string? mainName, ref string message)
        {
            if (!string.IsNullOrWhiteSpace(mainName)) return mainName!;
            var separator = message.IndexOf(": ", StringComparison.Ordinal);
            if (separator > 0)
            {
                var name = message.Substring(0, separator);
                message = message.Substring(separator + 2);
                return name;
            }
            return "Server";
        }

        private static bool IsCommand(ChatChannel channel, string message, IReadOnlyList<string> prefixes)
        {
            if (channel != ChatChannel.Global || string.IsNullOrEmpty(message)) return false;
            foreach (var prefix in prefixes)
                if (message.StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }

        private bool TryHandleCommand(
            ClientInfo? clientInfo,
            string? crossplatformId,
            string displayName,
            string message)
        {
            if (commands == null || replySender == null || clientInfo == null || string.IsNullOrWhiteSpace(crossplatformId))
                return false;
            var tokens = message.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return false;
            var commandName = tokens[0].Substring(1);
            if (commandName.Length == 0) return false;
            var result = commands.Handle(commandName,
                new GameChatCommandContext(crossplatformId!, displayName, tokens.Skip(1)));
            return DeliverHandledCommand(
                result,
                messages => replySender.Send(clientInfo, messages),
                log);
        }

        private void TryWriteAutomationTrigger(
            long sequence,
            DateTimeOffset occurredAtUtc,
            int entityId,
            string? crossplatformId,
            string messageText)
        {
            if (automationIngress == null) return;
            var trigger = new AutomationTriggerSnapshot(
                "chat:" + sequence.ToString(CultureInfo.InvariantCulture),
                AutomationTriggerType.ChatMessage.ToString(),
                occurredAtUtc,
                crossplatformId,
                entityId >= 0 ? entityId : (long?)null,
                null,
                null,
                messageText,
                null,
                null,
                Array.Empty<string>());
            try { automationIngress.TryWrite(trigger); }
            catch { }
        }

        private void TryBridgeToDiscord(ChatMessage message, bool isCommand)
        {
            if (isCommand || discordBridge == null) return;
            try { discordBridge.Execute(message); }
            catch (Exception exception)
            {
                try { log("Discord chat bridge failed: " + exception.GetType().Name + "."); } catch { }
            }
        }

        internal static bool DeliverHandledCommand(
            GameChatCommandResult result,
            Action<IEnumerable<string>> deliver,
            Action<string> log)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (deliver == null) throw new ArgumentNullException(nameof(deliver));
            if (log == null) throw new ArgumentNullException(nameof(log));
            if (!result.IsHandled) return false;

            var messages = result.Messages.Count == 0
                ? new[] { result.Code ?? "chat.command.failed" }
                : result.Messages;
            try
            {
                deliver(messages);
            }
            catch (Exception exception)
            {
                try { log("Chat command reply failed: " + exception.GetType().Name + "."); } catch { }
            }
            return true;
        }

        private static ChatChannel MapChannel(EChatType chatType)
        {
            switch (chatType)
            {
                case EChatType.Global: return ChatChannel.Global;
                case EChatType.Friends: return ChatChannel.Friends;
                case EChatType.Party: return ChatChannel.Party;
                case EChatType.Whisper: return ChatChannel.Whisper;
                default: return ChatChannel.Unknown;
            }
        }
    }
}
