using System;
using System.Text;
using LSTY.SevenDPanel.Application;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    internal static class PlayerEvidenceCursorCodec
    {
        private const int Version = 1;

        public static string Encode(string crossplatformId, PlayerEvidenceCursor cursor)
        {
            if (string.IsNullOrWhiteSpace(crossplatformId))
                throw new ArgumentException("A cross-platform identity is required.", nameof(crossplatformId));
            if (cursor == null) throw new ArgumentNullException(nameof(cursor));

            var payload = new JObject
            {
                ["version"] = Version,
                ["crossplatformId"] = crossplatformId,
                ["observedAtUtcMs"] = cursor.ObservedAtUtc.ToUnixTimeMilliseconds(),
                ["id"] = cursor.Id
            };
            return Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(payload.ToString(Formatting.None)))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        public static bool TryDecode(
            string? encoded,
            string crossplatformId,
            out PlayerEvidenceCursor? cursor)
        {
            cursor = null;
            if (string.IsNullOrWhiteSpace(crossplatformId) ||
                string.IsNullOrWhiteSpace(encoded) ||
                !IsUrlSafeBase64(encoded!))
            {
                return false;
            }

            try
            {
                var normalized = encoded!.Replace('-', '+').Replace('_', '/');
                if (normalized.Length % 4 == 1) return false;
                normalized = normalized.PadRight(
                    normalized.Length + (4 - normalized.Length % 4) % 4,
                    '=');
                var payload = JObject.Parse(
                    Encoding.UTF8.GetString(Convert.FromBase64String(normalized)));
                if (payload["version"]?.Type != JTokenType.Integer ||
                    payload["version"]!.Value<int>() != Version ||
                    payload["crossplatformId"]?.Type != JTokenType.String ||
                    !string.Equals(
                        payload["crossplatformId"]!.Value<string>(),
                        crossplatformId,
                        StringComparison.Ordinal) ||
                    payload["observedAtUtcMs"]?.Type != JTokenType.Integer ||
                    payload["id"]?.Type != JTokenType.Integer)
                {
                    return false;
                }

                cursor = new PlayerEvidenceCursor(
                    DateTimeOffset.FromUnixTimeMilliseconds(
                        payload["observedAtUtcMs"]!.Value<long>()),
                    payload["id"]!.Value<long>());
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is FormatException ||
                exception is JsonException ||
                exception is OverflowException)
            {
                return false;
            }
        }

        private static bool IsUrlSafeBase64(string value)
        {
            foreach (var character in value)
            {
                if ((character < 'A' || character > 'Z') &&
                    (character < 'a' || character > 'z') &&
                    (character < '0' || character > '9') &&
                    character != '-' &&
                    character != '_')
                {
                    return false;
                }
            }
            return true;
        }
    }
}
