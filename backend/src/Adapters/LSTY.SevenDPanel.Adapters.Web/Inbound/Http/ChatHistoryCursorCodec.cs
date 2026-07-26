using System;
using System.Text;
using LSTY.SevenDPanel.Application.Chat;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    internal sealed class ChatHistoryCursorFilters : IEquatable<ChatHistoryCursorFilters>
    {
        public static readonly ChatHistoryCursorFilters Empty =
            new ChatHistoryCursorFilters(null, null, null, null, null, null);

        public ChatHistoryCursorFilters(
            string? crossplatformId,
            string? senderName,
            ChatChannel? channel,
            ChatSourceKind? sourceKind,
            DateTimeOffset? startUtc,
            DateTimeOffset? endUtc)
        {
            CrossplatformId = Normalize(crossplatformId);
            SenderName = Normalize(senderName);
            Channel = channel;
            SourceKind = sourceKind;
            StartUtc = startUtc;
            EndUtc = endUtc;
        }

        public string? CrossplatformId { get; }
        public string? SenderName { get; }
        public ChatChannel? Channel { get; }
        public ChatSourceKind? SourceKind { get; }
        public DateTimeOffset? StartUtc { get; }
        public DateTimeOffset? EndUtc { get; }

        public bool Equals(ChatHistoryCursorFilters? other) =>
            other != null && CrossplatformId == other.CrossplatformId &&
            SenderName == other.SenderName && Channel == other.Channel &&
            SourceKind == other.SourceKind && StartUtc == other.StartUtc && EndUtc == other.EndUtc;

        public override bool Equals(object? obj) => Equals(obj as ChatHistoryCursorFilters);
        public override int GetHashCode() => 0;
        private static string? Normalize(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
    }

    internal sealed class ColoredChatProfileCursorFilters : IEquatable<ColoredChatProfileCursorFilters>
    {
        public ColoredChatProfileCursorFilters(
            string? crossplatformId, string? customName, string? nameColor, string? textColor,
            DateTimeOffset? createdAfterUtc, DateTimeOffset? createdBeforeUtc)
        {
            CrossplatformId = Normalize(crossplatformId);
            CustomName = Normalize(customName);
            NameColor = Normalize(nameColor)?.ToUpperInvariant();
            TextColor = Normalize(textColor)?.ToUpperInvariant();
            CreatedAfterUtc = createdAfterUtc;
            CreatedBeforeUtc = createdBeforeUtc;
        }

        public string? CrossplatformId { get; }
        public string? CustomName { get; }
        public string? NameColor { get; }
        public string? TextColor { get; }
        public DateTimeOffset? CreatedAfterUtc { get; }
        public DateTimeOffset? CreatedBeforeUtc { get; }
        public bool Equals(ColoredChatProfileCursorFilters? other) =>
            other != null && CrossplatformId == other.CrossplatformId && CustomName == other.CustomName &&
            NameColor == other.NameColor && TextColor == other.TextColor &&
            CreatedAfterUtc == other.CreatedAfterUtc && CreatedBeforeUtc == other.CreatedBeforeUtc;
        public override bool Equals(object? obj) => Equals(obj as ColoredChatProfileCursorFilters);
        public override int GetHashCode() => 0;
        private static string? Normalize(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
    }

    internal static class ChatHistoryCursorCodec
    {
        private const int Version = 1;

        public static string Encode(ChatHistoryKeyset keyset, ChatHistoryCursorFilters filters)
        {
            if (keyset == null) throw new ArgumentNullException(nameof(keyset));
            if (filters == null) throw new ArgumentNullException(nameof(filters));
            return EncodePayload(new JObject
            {
                ["version"] = Version,
                ["kind"] = "history",
                ["occurredAtUtcMs"] = keyset.OccurredAtUtc.ToUnixTimeMilliseconds(),
                ["rowId"] = keyset.RowId,
                ["filters"] = HistoryFiltersToJson(filters)
            });
        }

        public static bool TryDecode(
            string? encoded,
            ChatHistoryCursorFilters filters,
            out ChatHistoryKeyset? keyset)
        {
            keyset = null;
            if (!TryDecodePayload(encoded, "history", out var payload)) return false;
            try
            {
                var decodedFilters = HistoryFiltersFromJson((JObject?)payload!["filters"]);
                if (decodedFilters == null || !decodedFilters.Equals(filters)) return false;
                var occurredAt = RequireInteger(payload["occurredAtUtcMs"]);
                var rowId = RequireInteger(payload["rowId"]);
                keyset = new ChatHistoryKeyset(DateTimeOffset.FromUnixTimeMilliseconds(occurredAt), rowId);
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is FormatException ||
                exception is InvalidOperationException || exception is OverflowException)
            {
                return false;
            }
        }

        public static string EncodeProfile(
            ColoredChatProfileKeyset keyset,
            ColoredChatProfileCursorFilters filters)
        {
            if (keyset == null) throw new ArgumentNullException(nameof(keyset));
            if (filters == null) throw new ArgumentNullException(nameof(filters));
            return EncodePayload(new JObject
            {
                ["version"] = Version,
                ["kind"] = "profile",
                ["updatedAtUtcMs"] = keyset.UpdatedAtUtc.ToUnixTimeMilliseconds(),
                ["crossplatformId"] = keyset.CrossplatformId,
                ["filters"] = ProfileFiltersToJson(filters)
            });
        }

        public static bool TryDecodeProfile(
            string? encoded,
            ColoredChatProfileCursorFilters filters,
            out ColoredChatProfileKeyset? keyset)
        {
            keyset = null;
            if (!TryDecodePayload(encoded, "profile", out var payload)) return false;
            try
            {
                var decodedFilters = ProfileFiltersFromJson((JObject?)payload!["filters"]);
                if (decodedFilters == null || !decodedFilters.Equals(filters)) return false;
                var updatedAt = RequireInteger(payload["updatedAtUtcMs"]);
                var id = payload["crossplatformId"]?.Type == JTokenType.String
                    ? payload["crossplatformId"]!.Value<string>()
                    : null;
                if (id == null) return false;
                keyset = new ColoredChatProfileKeyset(
                    DateTimeOffset.FromUnixTimeMilliseconds(updatedAt), id);
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is FormatException ||
                exception is InvalidOperationException || exception is OverflowException)
            {
                return false;
            }
        }

        private static bool TryDecodePayload(string? encoded, string kind, out JObject? payload)
        {
            payload = null;
            if (string.IsNullOrWhiteSpace(encoded) || !IsUrlSafeBase64(encoded!)) return false;
            try
            {
                var normalized = encoded!.Replace('-', '+').Replace('_', '/');
                if (normalized.Length % 4 == 1) return false;
                normalized = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
                payload = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(normalized)));
                return payload["version"]?.Type == JTokenType.Integer &&
                    payload["version"]!.Value<int>() == Version &&
                    payload["kind"]?.Type == JTokenType.String &&
                    payload["kind"]!.Value<string>() == kind;
            }
            catch (Exception exception) when (exception is FormatException || exception is JsonException)
            {
                return false;
            }
        }

        private static string EncodePayload(JObject payload) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(payload.ToString(Formatting.None)))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static JObject HistoryFiltersToJson(ChatHistoryCursorFilters value) => new JObject
        {
            ["crossplatformId"] = value.CrossplatformId,
            ["senderName"] = value.SenderName,
            ["channel"] = value.Channel?.ToString(),
            ["sourceKind"] = value.SourceKind?.ToString(),
            ["startUtcMs"] = value.StartUtc?.ToUnixTimeMilliseconds(),
            ["endUtcMs"] = value.EndUtc?.ToUnixTimeMilliseconds()
        };

        private static ChatHistoryCursorFilters? HistoryFiltersFromJson(JObject? value)
        {
            if (value == null) return null;
            if (!TryOptionalEnum(value["channel"], out ChatChannel? channel) ||
                !TryOptionalEnum(value["sourceKind"], out ChatSourceKind? source)) return null;
            return new ChatHistoryCursorFilters(
                OptionalString(value["crossplatformId"]), OptionalString(value["senderName"]),
                channel, source, OptionalInstant(value["startUtcMs"]), OptionalInstant(value["endUtcMs"]));
        }

        private static JObject ProfileFiltersToJson(ColoredChatProfileCursorFilters value) => new JObject
        {
            ["crossplatformId"] = value.CrossplatformId,
            ["customName"] = value.CustomName,
            ["nameColor"] = value.NameColor,
            ["textColor"] = value.TextColor,
            ["createdAfterUtcMs"] = value.CreatedAfterUtc?.ToUnixTimeMilliseconds(),
            ["createdBeforeUtcMs"] = value.CreatedBeforeUtc?.ToUnixTimeMilliseconds()
        };

        private static ColoredChatProfileCursorFilters? ProfileFiltersFromJson(JObject? value) =>
            value == null ? null : new ColoredChatProfileCursorFilters(
                OptionalString(value["crossplatformId"]), OptionalString(value["customName"]),
                OptionalString(value["nameColor"]), OptionalString(value["textColor"]),
                OptionalInstant(value["createdAfterUtcMs"]), OptionalInstant(value["createdBeforeUtcMs"]));

        private static long RequireInteger(JToken? token)
        {
            if (token?.Type != JTokenType.Integer) throw new FormatException();
            return token.Value<long>();
        }

        private static string? OptionalString(JToken? token) =>
            token == null || token.Type == JTokenType.Null ? null :
            token.Type == JTokenType.String ? token.Value<string>() : throw new FormatException();

        private static DateTimeOffset? OptionalInstant(JToken? token) =>
            token == null || token.Type == JTokenType.Null
                ? null
                : DateTimeOffset.FromUnixTimeMilliseconds(RequireInteger(token));

        private static bool TryOptionalEnum<T>(JToken? token, out T? value) where T : struct
        {
            value = null;
            if (token == null || token.Type == JTokenType.Null) return true;
            if (token.Type != JTokenType.String ||
                !Enum.TryParse(token.Value<string>(), false, out T parsed) ||
                !Enum.IsDefined(typeof(T), parsed)) return false;
            value = parsed;
            return true;
        }

        private static bool IsUrlSafeBase64(string value)
        {
            foreach (var character in value)
            {
                if (!char.IsLetterOrDigit(character) && character != '-' && character != '_')
                    return false;
            }
            return true;
        }
    }
}
