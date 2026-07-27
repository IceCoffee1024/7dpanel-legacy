using System;
using System.Text;
using LSTY.SevenDPanel.Application.GameEvents;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    internal sealed class GameEventCursorFilters : IEquatable<GameEventCursorFilters>
    {
        public GameEventCursorFilters(DateTimeOffset? fromUtc, DateTimeOffset? toUtc, GameEventType? eventType, string? crossplatformId)
        {
            FromUtc = fromUtc; ToUtc = toUtc; EventType = eventType;
            CrossplatformId = string.IsNullOrWhiteSpace(crossplatformId) ? null : crossplatformId!.Trim();
        }
        public DateTimeOffset? FromUtc { get; } public DateTimeOffset? ToUtc { get; } public GameEventType? EventType { get; } public string? CrossplatformId { get; }
        public bool Equals(GameEventCursorFilters? other) => other != null && FromUtc == other.FromUtc && ToUtc == other.ToUtc && EventType == other.EventType && CrossplatformId == other.CrossplatformId;
        public override bool Equals(object? obj) => Equals(obj as GameEventCursorFilters);
        public override int GetHashCode() => 0;
    }

    internal static class GameEventCursorCodec
    {
        public static string Encode(GameEventCursor cursor, GameEventCursorFilters filters)
        {
            if (cursor == null) throw new ArgumentNullException(nameof(cursor));
            if (filters == null) throw new ArgumentNullException(nameof(filters));
            return ToBase64(new JObject { ["version"] = 1, ["kind"] = "game-events", ["occurredUtcMs"] = cursor.OccurredAtUtc.ToUnixTimeMilliseconds(), ["eventId"] = cursor.EventId, ["filters"] = ToJson(filters) });
        }
        public static bool TryDecode(string? value, GameEventCursorFilters filters, out GameEventCursor? cursor)
        {
            cursor = null;
            if (filters == null || string.IsNullOrWhiteSpace(value) || !IsUrlBase64(value!)) return false;
            try
            {
                var json = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(Pad(value!))));
                if (json["version"]?.Value<int>() != 1 || json["kind"]?.Value<string>() != "game-events") return false;
                var decoded = FromJson(json["filters"] as JObject);
                if (decoded == null || !decoded.Equals(filters)) return false;
                var milliseconds = json["occurredUtcMs"]?.Type == JTokenType.Integer ? json["occurredUtcMs"]!.Value<long>() : throw new FormatException();
                var eventId = json["eventId"]?.Type == JTokenType.String ? json["eventId"]!.Value<string>() : throw new FormatException();
                cursor = new GameEventCursor(DateTimeOffset.FromUnixTimeMilliseconds(milliseconds), eventId!);
                return true;
            }
            catch (Exception exception) when (exception is FormatException || exception is JsonException || exception is ArgumentException || exception is OverflowException) { return false; }
        }
        private static JObject ToJson(GameEventCursorFilters filters) => new JObject { ["fromUtcMs"] = filters.FromUtc?.ToUnixTimeMilliseconds(), ["toUtcMs"] = filters.ToUtc?.ToUnixTimeMilliseconds(), ["eventType"] = filters.EventType?.ToString(), ["crossplatformId"] = filters.CrossplatformId };
        private static GameEventCursorFilters? FromJson(JObject? json)
        {
            if (json == null) return null;
            var type = json["eventType"]?.Type == JTokenType.Null || json["eventType"] == null ? (GameEventType?)null : json["eventType"]!.Type == JTokenType.String && Enum.TryParse(json["eventType"]!.Value<string>(), false, out GameEventType parsed) && Enum.IsDefined(typeof(GameEventType), parsed) ? parsed : (GameEventType?)null;
            if (json["eventType"] != null && json["eventType"]!.Type != JTokenType.Null && !type.HasValue) return null;
            return new GameEventCursorFilters(Instant(json["fromUtcMs"]), Instant(json["toUtcMs"]), type, String(json["crossplatformId"]));
        }
        private static DateTimeOffset? Instant(JToken? token) => token == null || token.Type == JTokenType.Null ? null : token.Type == JTokenType.Integer ? DateTimeOffset.FromUnixTimeMilliseconds(token.Value<long>()) : throw new FormatException();
        private static string? String(JToken? token) => token == null || token.Type == JTokenType.Null ? null : token.Type == JTokenType.String ? token.Value<string>() : throw new FormatException();
        private static string ToBase64(JObject value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value.ToString(Formatting.None))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        private static string Pad(string value) { var normalized = value.Replace('-', '+').Replace('_', '/'); if (normalized.Length % 4 == 1) throw new FormatException(); return normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '='); }
        private static bool IsUrlBase64(string value) { foreach (var character in value) if (!char.IsLetterOrDigit(character) && character != '-' && character != '_') return false; return true; }
    }
}
