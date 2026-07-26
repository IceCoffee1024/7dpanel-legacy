using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using LSTY.SevenDPanel.Application.Chat;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Chat
{
    public sealed class ColoredChatRenderRequest
    {
        public ColoredChatRenderRequest(
            string playerName, string? playerId, int entityId, ChatChannel channel,
            ChatSourceKind sourceKind, string message, ColoredChatSettings settings,
            ColoredChatProfile? profile)
        {
            PlayerName = playerName ?? string.Empty;
            PlayerId = playerId;
            EntityId = entityId;
            Channel = channel;
            SourceKind = sourceKind;
            Message = message ?? string.Empty;
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Profile = profile;
        }

        public string PlayerName { get; }
        public string? PlayerId { get; }
        public int EntityId { get; }
        public ChatChannel Channel { get; }
        public ChatSourceKind SourceKind { get; }
        public string Message { get; }
        public ColoredChatSettings Settings { get; }
        public ColoredChatProfile? Profile { get; }
    }

    public sealed class ColoredChatRenderer
    {
        private static readonly Regex AllowedColorTag = new Regex(
            "\\[(?:[0-9A-Fa-f]{6}|-)\\]", RegexOptions.CultureInvariant);

        public string Render(ColoredChatRenderRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var allowPlayerTags = request.Settings.PlayerColorTagPermission == PlayerColorTagPermission.All
                || (request.Settings.PlayerColorTagPermission == PlayerColorTagPermission.AdminOnly
                    && request.SourceKind == ChatSourceKind.Administrator);
            var safeName = EscapePlayerText(request.PlayerName, allowPlayerTags);
            var safeMessage = EscapePlayerText(request.Message, allowPlayerTags);
            var displayName = ExpandNameTemplate(request.Profile?.CustomName, safeName, request);
            var fallbackColor = ResolveDefaultColor(request.Settings, request.Channel, request.SourceKind);
            var nameColor = request.Profile?.NameColor ?? fallbackColor;
            var textColor = request.Profile?.TextColor ?? fallbackColor;

            return Wrap(nameColor, displayName) + ": " + Wrap(textColor, safeMessage);
        }

        internal void Send(EChatType chatType, IReadOnlyList<int>? recipientEntityIds, string renderedMessage)
        {
            var package = NetPackageManager.GetPackage<NetPackageChat>().Setup(
                chatType, -1, renderedMessage, null, EMessageSender.None,
                GeneratedTextManager.BbCodeSupportMode.Supported);
            if (recipientEntityIds != null)
            {
                foreach (var entityId in recipientEntityIds)
                    ConnectionManager.Instance.Clients.ForEntityId(entityId)?.SendPackage(package);
                return;
            }

            ConnectionManager.Instance.SendPackage(package, true, -1, -1, -1, null, 192);
        }

        private static string ExpandNameTemplate(string? template, string safePlayerName, ColoredChatRenderRequest request)
        {
            if (string.IsNullOrWhiteSpace(template)) return safePlayerName;
            var result = template!;
            result = ReplaceVariable(result, "playerName", safePlayerName);
            result = ReplaceVariable(result, "playerId", EscapePlayerText(request.PlayerId ?? string.Empty, false));
            result = ReplaceVariable(result, "entityId", request.EntityId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return ReplaceVariable(result, "chatType", request.Channel.ToString());
        }

        private static string ReplaceVariable(string template, string variable, string value) =>
            Regex.Replace(template, "\\{" + Regex.Escape(variable) + "\\}", _ => value, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static string? ResolveDefaultColor(ColoredChatSettings settings, ChatChannel channel, ChatSourceKind sourceKind)
        {
            if (sourceKind == ChatSourceKind.System) return settings.SystemDefaultColor;
            if (sourceKind == ChatSourceKind.Administrator) return settings.AdminDefaultColor;
            switch (channel)
            {
                case ChatChannel.Global: return settings.GlobalDefaultColor;
                case ChatChannel.Friends: return settings.FriendsDefaultColor;
                case ChatChannel.Party: return settings.PartyDefaultColor;
                case ChatChannel.Whisper: return settings.WhisperDefaultColor;
                default: return settings.GlobalDefaultColor;
            }
        }

        private static string Wrap(string? color, string value) =>
            string.IsNullOrEmpty(color) ? value : "[" + color + "]" + value + "[-]";

        private static string EscapePlayerText(string value, bool preserveAllowedColorTags)
        {
            if (!preserveAllowedColorTags) return value.Replace("\\", "\\\\").Replace("[", "\\[");
            var result = new StringBuilder(value.Length);
            var offset = 0;
            foreach (Match match in AllowedColorTag.Matches(value))
            {
                result.Append(value.Substring(offset, match.Index - offset).Replace("\\", "\\\\").Replace("[", "\\["));
                result.Append(match.Value);
                offset = match.Index + match.Length;
            }
            result.Append(value.Substring(offset).Replace("\\", "\\\\").Replace("[", "\\["));
            return result.ToString();
        }
    }
}
