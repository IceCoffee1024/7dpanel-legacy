using System;
using System.Text;
using LSTY.SevenDPanel.Application;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    internal static class PlayerHistoryCursorCodec
    {
        private const int Version = 1;

        public static string Encode(HistoricalPlayersCursor cursor)
        {
            if (cursor == null) throw new ArgumentNullException(nameof(cursor));

            var payload = new JObject
            {
                ["version"] = Version,
                ["firstObservedUtcMs"] = cursor.FirstObservedAtUtc.ToUnixTimeMilliseconds(),
                ["crossplatformId"] = cursor.CrossplatformId
            };
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload.ToString(Formatting.None)))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        public static bool TryDecode(string? encoded, out HistoricalPlayersCursor? cursor)
        {
            cursor = null;
            if (string.IsNullOrWhiteSpace(encoded)) return false;
            if (!IsUrlSafeBase64(encoded!)) return false;

            try
            {
                var normalized = encoded!.Replace('-', '+').Replace('_', '/');
                if (normalized.Length % 4 == 1) return false;
                normalized = normalized.PadRight(
                    normalized.Length + (4 - normalized.Length % 4) % 4,
                    '=');
                var payload = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(normalized)));
                var version = payload["version"];
                var firstObservedUtcMs = payload["firstObservedUtcMs"];
                var crossplatformId = payload["crossplatformId"];
                if (version?.Type != JTokenType.Integer ||
                    version.Value<int>() != Version ||
                    firstObservedUtcMs?.Type != JTokenType.Integer ||
                    crossplatformId?.Type != JTokenType.String)
                {
                    return false;
                }

                cursor = new HistoricalPlayersCursor(
                    DateTimeOffset.FromUnixTimeMilliseconds(firstObservedUtcMs.Value<long>()),
                    crossplatformId.Value<string>()!);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (JsonException)
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
