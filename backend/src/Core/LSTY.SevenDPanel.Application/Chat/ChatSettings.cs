using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace LSTY.SevenDPanel.Application.Chat
{
    public enum PlayerColorTagPermission
    {
        None,
        AdminOnly,
        All
    }

    public sealed class ChatSettings
    {
        public required bool IsEnabled { get; init; }
        public string? GlobalServerName { get; init; }
        public string? WhisperServerName { get; init; }
        public required IReadOnlyList<string> CommandPrefixes { get; init; }
        public bool AllowNoPrefix { get; init; }
        public string CommandParameterSeparator { get; init; } = " ";
        public bool HideRegisteredCommandGlobalMessages { get; init; } = true;
        public required bool ExcludeCommandsFromHistory { get; init; }
        public required int HistoryRetentionDays { get; init; }
    }

    public sealed class ColoredChatSettings
    {
        public required bool IsEnabled { get; init; }
        public string? GlobalDefaultColor { get; init; }
        public string? WhisperDefaultColor { get; init; }
        public string? FriendsDefaultColor { get; init; }
        public string? PartyDefaultColor { get; init; }
        public string? AdminDefaultColor { get; init; }
        public string? SystemDefaultColor { get; init; }
        public required PlayerColorTagPermission PlayerColorTagPermission { get; init; }
    }

    public sealed class ColoredChatProfile
    {
        public required string CrossplatformId { get; init; }
        public string? CustomName { get; init; }
        public string? NameColor { get; init; }
        public string? TextColor { get; init; }
        public string? Description { get; init; }
        public required DateTimeOffset CreatedAtUtc { get; init; }
        public required DateTimeOffset UpdatedAtUtc { get; init; }
    }

    public static class ChatValidation
    {
        public const int MaximumMessageLength = 500;
        public const int MaximumHistoryRetentionDays = 3650;

        private static readonly Regex RgbPattern = new Regex(
            "^[0-9A-Fa-f]{6}$",
            RegexOptions.CultureInvariant);

        public static string NormalizeMessage(string message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            var normalized = message.Trim();
            if (normalized.Length == 0 || normalized.Length > MaximumMessageLength)
                throw new ArgumentException("A chat message must contain between 1 and 500 characters.", nameof(message));

            return normalized;
        }

        public static ChatSettings Normalize(ChatSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (settings.HistoryRetentionDays < 0
                || settings.HistoryRetentionDays > MaximumHistoryRetentionDays)
            {
                throw new ArgumentOutOfRangeException(nameof(settings.HistoryRetentionDays));
            }

            if (settings.CommandPrefixes == null)
                throw new ArgumentException("Command prefixes are required.", nameof(settings));

            var prefixes = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var prefix in settings.CommandPrefixes)
            {
                if (string.IsNullOrWhiteSpace(prefix) || prefix.Length != 1)
                    throw new ArgumentException("Each command prefix must be exactly one character.", nameof(settings));

                if (seen.Add(prefix))
                    prefixes.Add(prefix);
            }

            var parameterSeparator = settings.CommandParameterSeparator;
            if (parameterSeparator == null || parameterSeparator.Length != 1)
                throw new ArgumentException("The command parameter separator must be exactly one character.", nameof(settings));
            var separator = parameterSeparator[0];
            var separatorCategory = char.GetUnicodeCategory(separator);
            if (separatorCategory == UnicodeCategory.Control
                || separatorCategory == UnicodeCategory.Format
                || separatorCategory == UnicodeCategory.Surrogate
                || separatorCategory == UnicodeCategory.PrivateUse
                || separatorCategory == UnicodeCategory.OtherNotAssigned
                || (char.IsWhiteSpace(separator) && separator != ' ')
                || char.IsLetterOrDigit(separator))
            {
                throw new ArgumentException(
                    "The command parameter separator must be a space or one printable non-alphanumeric character.",
                    nameof(settings));
            }
            if (prefixes.Contains(parameterSeparator, StringComparer.Ordinal))
                throw new ArgumentException("The command parameter separator cannot also be a command prefix.", nameof(settings));

            return new ChatSettings
            {
                IsEnabled = settings.IsEnabled,
                GlobalServerName = OptionalText(settings.GlobalServerName),
                WhisperServerName = OptionalText(settings.WhisperServerName),
                CommandPrefixes = prefixes.ToArray(),
                AllowNoPrefix = settings.AllowNoPrefix,
                CommandParameterSeparator = parameterSeparator,
                HideRegisteredCommandGlobalMessages = settings.HideRegisteredCommandGlobalMessages,
                ExcludeCommandsFromHistory = settings.ExcludeCommandsFromHistory,
                HistoryRetentionDays = settings.HistoryRetentionDays
            };
        }

        public static ColoredChatSettings Normalize(ColoredChatSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (!Enum.IsDefined(typeof(PlayerColorTagPermission), settings.PlayerColorTagPermission))
                throw new ArgumentOutOfRangeException(nameof(settings.PlayerColorTagPermission));

            return new ColoredChatSettings
            {
                IsEnabled = settings.IsEnabled,
                GlobalDefaultColor = NormalizeColor(settings.GlobalDefaultColor),
                WhisperDefaultColor = NormalizeColor(settings.WhisperDefaultColor),
                FriendsDefaultColor = NormalizeColor(settings.FriendsDefaultColor),
                PartyDefaultColor = NormalizeColor(settings.PartyDefaultColor),
                AdminDefaultColor = NormalizeColor(settings.AdminDefaultColor),
                SystemDefaultColor = NormalizeColor(settings.SystemDefaultColor),
                PlayerColorTagPermission = settings.PlayerColorTagPermission
            };
        }

        public static ColoredChatProfile Normalize(ColoredChatProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            var crossplatformId = RequireBusinessKey(profile.CrossplatformId, nameof(profile.CrossplatformId));

            return new ColoredChatProfile
            {
                CrossplatformId = crossplatformId,
                CustomName = OptionalText(profile.CustomName),
                NameColor = NormalizeColor(profile.NameColor),
                TextColor = NormalizeColor(profile.TextColor),
                Description = OptionalText(profile.Description),
                CreatedAtUtc = RequireUtc(profile.CreatedAtUtc, nameof(profile.CreatedAtUtc)),
                UpdatedAtUtc = RequireUtc(profile.UpdatedAtUtc, nameof(profile.UpdatedAtUtc))
            };
        }

        public static string? NormalizeColor(string? color)
        {
            var normalized = OptionalText(color);
            if (normalized == null)
                return null;
            if (!RgbPattern.IsMatch(normalized))
                throw new ArgumentException("A color must be a six-digit RGB hexadecimal value.", nameof(color));

            return normalized.ToUpperInvariant();
        }

        public static string RequireBusinessKey(string value, string parameterName)
        {
            if (string.IsNullOrEmpty(value) || value.Any(char.IsWhiteSpace))
                throw new ArgumentException("A non-empty business key without whitespace is required.", parameterName);
            return value;
        }

        internal static string RequireActor(string actorSubject)
        {
            if (string.IsNullOrWhiteSpace(actorSubject))
                throw new ArgumentException("An actor subject is required.", nameof(actorSubject));
            return actorSubject.Trim();
        }

        internal static DateTimeOffset RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", parameterName);
            return value;
        }

        internal static string? OptionalText(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
    }
}
