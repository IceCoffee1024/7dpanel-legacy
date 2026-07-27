using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public static class AuditCursorCodec
    {
        private static readonly HashSet<string> SourceKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            "playerAction", "consoleCommand", "serverOperation", "chatOperation", "chatMuteOperation"
        };

        public static string Encode(UnifiedAuditCursor cursor)
        {
            if (cursor == null) throw new ArgumentNullException(nameof(cursor));
            var payload = string.Join("\n", new[]
            {
                cursor.OccurredAtUtc.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                Convert.ToBase64String(Encoding.UTF8.GetBytes(cursor.SourceKind)),
                Convert.ToBase64String(Encoding.UTF8.GetBytes(cursor.SourceId))
            });
            return ToBase64Url(Encoding.UTF8.GetBytes(payload));
        }

        public static bool TryDecode(string? value, out UnifiedAuditCursor? cursor)
        {
            cursor = null;
            if (string.IsNullOrWhiteSpace(value)) return false;

            try
            {
                var parts = Encoding.UTF8.GetString(FromBase64Url(value!)).Split(new[] { '\n' });
                if (parts.Length != 3 ||
                    !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var occurredUtc))
                {
                    return false;
                }

                var sourceKind = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));
                if (!IsSupportedSourceKind(sourceKind)) return false;

                cursor = new UnifiedAuditCursor(
                    DateTimeOffset.FromUnixTimeMilliseconds(occurredUtc),
                    sourceKind,
                    Encoding.UTF8.GetString(Convert.FromBase64String(parts[2])));
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        internal static bool IsSupportedSourceKind(string? value) =>
            value != null && SourceKinds.Contains(value);

        private static string ToBase64Url(byte[] value) => Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        private static byte[] FromBase64Url(string value)
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            switch (normalized.Length % 4)
            {
                case 0: break;
                case 2: normalized += "=="; break;
                case 3: normalized += "="; break;
                default: throw new FormatException("The cursor is not base64url.");
            }
            return Convert.FromBase64String(normalized);
        }
    }
}
