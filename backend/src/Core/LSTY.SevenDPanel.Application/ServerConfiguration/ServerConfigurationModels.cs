using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LSTY.SevenDPanel.Application.ServerConfiguration
{
    public enum ServerConfigurationValueType
    {
        Text,
        Integer,
        Decimal,
        Boolean,
        Enum
    }

    public sealed class ServerConfigurationField
    {
        public ServerConfigurationField(
            string key,
            string value,
            string group,
            ServerConfigurationValueType valueType,
            bool editable,
            bool sensitive,
            bool isSet,
            bool restartRequired,
            IReadOnlyList<string> allowedValues,
            decimal? minimum,
            decimal? maximum)
        {
            Key = key;
            Value = value;
            Group = group;
            ValueType = valueType;
            Editable = editable;
            Sensitive = sensitive;
            IsSet = isSet;
            RestartRequired = restartRequired;
            AllowedValues = allowedValues;
            Minimum = minimum;
            Maximum = maximum;
        }

        public string Key { get; }
        public string Value { get; }
        public string Group { get; }
        public ServerConfigurationValueType ValueType { get; }
        public bool Editable { get; }
        public bool Sensitive { get; }
        public bool IsSet { get; }
        public bool RestartRequired { get; }
        public IReadOnlyList<string> AllowedValues { get; }
        public decimal? Minimum { get; }
        public decimal? Maximum { get; }
    }

    public sealed class ServerConfigurationSnapshot
    {
        public ServerConfigurationSnapshot(string version, DateTimeOffset readAtUtc, IReadOnlyList<ServerConfigurationField> fields)
        {
            Version = version;
            ReadAtUtc = readAtUtc;
            Fields = fields;
        }

        public string Version { get; }
        public DateTimeOffset ReadAtUtc { get; }
        public IReadOnlyList<ServerConfigurationField> Fields { get; }
    }

    public sealed class UpdateServerConfigurationRequest
    {
        public UpdateServerConfigurationRequest(string key, string value, string version)
        {
            Key = key;
            Value = value;
            Version = version;
        }

        public string Key { get; }
        public string Value { get; }
        public string Version { get; }
    }

    public enum ServerConfigurationUpdateStatus
    {
        Updated,
        UnknownField,
        ReadOnly,
        InvalidValue,
        Conflict,
        WriteFailed
    }

    public sealed class ServerConfigurationUpdateResult
    {
        public ServerConfigurationUpdateResult(
            ServerConfigurationUpdateStatus status,
            string version,
            DateTimeOffset? savedAtUtc,
            bool restartRequired)
        {
            Status = status;
            Version = version;
            SavedAtUtc = savedAtUtc;
            RestartRequired = restartRequired;
        }

        public ServerConfigurationUpdateStatus Status { get; }
        public string Version { get; }
        public DateTimeOffset? SavedAtUtc { get; }
        public bool RestartRequired { get; }
    }

    public sealed class ServerConfigurationFieldDefinition
    {
        internal ServerConfigurationFieldDefinition(
            string key,
            string group,
            ServerConfigurationValueType valueType,
            bool editable,
            bool sensitive,
            bool restartRequired,
            IReadOnlyList<string> allowedValues,
            decimal? minimum,
            decimal? maximum)
        {
            Key = key;
            Group = group;
            ValueType = valueType;
            Editable = editable;
            Sensitive = sensitive;
            RestartRequired = restartRequired;
            AllowedValues = allowedValues;
            Minimum = minimum;
            Maximum = maximum;
        }

        public string Key { get; }
        public string Group { get; }
        public ServerConfigurationValueType ValueType { get; }
        public bool Editable { get; }
        public bool Sensitive { get; }
        public bool RestartRequired { get; }
        public IReadOnlyList<string> AllowedValues { get; }
        public decimal? Minimum { get; }
        public decimal? Maximum { get; }
    }

    public sealed class ServerConfigurationFieldCatalog
    {
        private static readonly IReadOnlyList<string> NoValues = Array.Empty<string>();
        private readonly IReadOnlyDictionary<string, ServerConfigurationFieldDefinition> definitions;

        private ServerConfigurationFieldCatalog(IDictionary<string, ServerConfigurationFieldDefinition> definitions)
        {
            this.definitions = new ReadOnlyDictionary<string, ServerConfigurationFieldDefinition>(definitions);
        }

        public static ServerConfigurationFieldCatalog Create()
        {
            var fields = new Dictionary<string, ServerConfigurationFieldDefinition>(StringComparer.Ordinal)
            {
                ["ServerName"] = Text("ServerName", "Identity", true),
                ["ServerDescription"] = Text("ServerDescription", "Identity", true),
                ["ServerMaxPlayerCount"] = Integer("ServerMaxPlayerCount", "Network", 1, 64),
                ["ServerPort"] = Integer("ServerPort", "Network", 1, 65535),
                ["GameWorld"] = Text("GameWorld", "World", true),
                ["WorldGenSeed"] = Text("WorldGenSeed", "World", true),
                ["GameName"] = Text("GameName", "World", true),
                ["GameDifficulty"] = Integer("GameDifficulty", "Gameplay", 0, 5),
                ["DayNightLength"] = Integer("DayNightLength", "Gameplay", 1, 240),
                ["PlayerKillingMode"] = Integer("PlayerKillingMode", "Gameplay", 0, 3),
                ["ServerDisabledNetworkProtocols"] = Text("ServerDisabledNetworkProtocols", "Network", false),
                ["ServerPassword"] = Sensitive("ServerPassword"),
                ["ControlPanelPassword"] = Sensitive("ControlPanelPassword"),
                ["TelnetPassword"] = Sensitive("TelnetPassword")
            };
            return new ServerConfigurationFieldCatalog(fields);
        }

        public bool TryGet(string key, out ServerConfigurationFieldDefinition definition)
        {
            return definitions.TryGetValue(key, out definition!);
        }

        public ServerConfigurationFieldDefinition DescribeUnknown(string key)
        {
            var sensitive = IsSensitiveKey(key);
            return new ServerConfigurationFieldDefinition(
                key, "Other", ServerConfigurationValueType.Text, false, sensitive, false, NoValues, null, null);
        }

        private static ServerConfigurationFieldDefinition Text(string key, string group, bool editable)
        {
            return new ServerConfigurationFieldDefinition(key, group, ServerConfigurationValueType.Text, editable, false, true, NoValues, null, null);
        }

        private static ServerConfigurationFieldDefinition Integer(string key, string group, decimal minimum, decimal maximum)
        {
            return new ServerConfigurationFieldDefinition(key, group, ServerConfigurationValueType.Integer, true, false, true, NoValues, minimum, maximum);
        }

        private static ServerConfigurationFieldDefinition Sensitive(string key)
        {
            return new ServerConfigurationFieldDefinition(key, "Security", ServerConfigurationValueType.Text, false, true, true, NoValues, null, null);
        }

        private static bool IsSensitiveKey(string key)
        {
            return key.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
