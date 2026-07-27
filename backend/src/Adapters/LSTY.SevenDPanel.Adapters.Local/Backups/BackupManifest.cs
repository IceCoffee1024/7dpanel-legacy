using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LSTY.SevenDPanel.Adapters.Local.Backups
{
    public sealed record BackupManifest(
        int Version,
        string Kind,
        string WorldId,
        string GameVersion,
        DateTimeOffset CreatedAtUtc,
        Guid SourceJobId,
        IReadOnlyList<BackupManifestEntry> Files)
    {
        public const int CurrentVersion = 1;
        public const string EntryName = "backup-manifest.json";

        public string ToJson()
        {
            var files = string.Join(",", Files.Select(file =>
                "{\"path\":\"" + Escape(file.RelativePath) +
                "\",\"sizeBytes\":" + file.SizeBytes.ToString(CultureInfo.InvariantCulture) +
                ",\"sha256\":\"" + Escape(file.Sha256) + "\"}"));
            return "{\"version\":" + Version.ToString(CultureInfo.InvariantCulture) +
                ",\"kind\":\"" + Escape(Kind) +
                "\",\"worldId\":\"" + Escape(WorldId) +
                "\",\"gameVersion\":\"" + Escape(GameVersion) +
                "\",\"createdAtUtc\":\"" + CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture) +
                "\",\"sourceJobId\":\"" + SourceJobId.ToString("D") +
                "\",\"files\":[" + files + "]}";
        }

        private static string Escape(string value)
        {
            var result = new StringBuilder(value.Length + 8);
            foreach (var character in value)
            {
                switch (character)
                {
                    case '\\': result.Append("\\\\"); break;
                    case '"': result.Append("\\\""); break;
                    case '\b': result.Append("\\b"); break;
                    case '\f': result.Append("\\f"); break;
                    case '\n': result.Append("\\n"); break;
                    case '\r': result.Append("\\r"); break;
                    case '\t': result.Append("\\t"); break;
                    default:
                        if (character < 0x20)
                            result.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            result.Append(character);
                        break;
                }
            }
            return result.ToString();
        }
    }

    public sealed record BackupManifestEntry(string RelativePath, long SizeBytes, string Sha256);
}
