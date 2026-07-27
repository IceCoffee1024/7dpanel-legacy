using System;
using System.Globalization;
using System.Text;
using LSTY.SevenDPanel.Application.Jobs;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    internal static class JobCursorCodec
    {
        public static string Encode(JobCursor cursor)
        {
            if (cursor == null) throw new ArgumentNullException(nameof(cursor));
            var payload = cursor.CreatedAtUtc.ToUnixTimeMilliseconds()
                .ToString(CultureInfo.InvariantCulture) + ":" + cursor.Id.ToString("N");
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        public static JobCursor Decode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new FormatException("invalid_job_cursor");
            try
            {
                var padded = value.Replace('-', '+').Replace('_', '/');
                padded += new string('=', (4 - padded.Length % 4) % 4);
                var payload = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
                var separator = payload.IndexOf(':');
                if (separator <= 0 || separator == payload.Length - 1)
                    throw new FormatException();
                if (!long.TryParse(payload.Substring(0, separator), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var milliseconds) ||
                    !Guid.TryParseExact(payload.Substring(separator + 1), "N", out var id) ||
                    id == Guid.Empty)
                {
                    throw new FormatException();
                }
                return new JobCursor(DateTimeOffset.FromUnixTimeMilliseconds(milliseconds), id);
            }
            catch (Exception exception) when (
                exception is FormatException || exception is ArgumentException ||
                exception is OverflowException)
            {
                throw new FormatException("invalid_job_cursor", exception);
            }
        }
    }
}
