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
            bool advanced,
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
            Advanced = advanced;
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
        public bool Advanced { get; }
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
            bool advanced,
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
            Advanced = advanced;
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
        public bool Advanced { get; }
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
                ["ServerWebsiteURL"] = Text("ServerWebsiteURL", "Identity", true),
                ["ServerPassword"] = Sensitive("ServerPassword"),
                ["ServerLoginConfirmationText"] = Text("ServerLoginConfirmationText", "Identity", true),
                ["Region"] = Enum("Region", "Identity", "NorthAmericaEast", "NorthAmericaWest", "CentralAmerica", "SouthAmerica", "Europe", "Russia", "Asia", "MiddleEast", "Africa", "Oceania"),
                ["Language"] = Text("Language", "Identity", true),

                ["ServerPort"] = Integer("ServerPort", "Network", 1, 65535),
                ["ServerVisibility"] = Enum("ServerVisibility", "Network", "0", "1", "2"),
                ["ServerDisabledNetworkProtocols"] = Text("ServerDisabledNetworkProtocols", "Network", true),
                ["ServerMaxWorldTransferSpeedKiBs"] = Integer("ServerMaxWorldTransferSpeedKiBs", "Network", 0, 1300),
                ["ServerMaxPlayerCount"] = Integer("ServerMaxPlayerCount", "Network", 1, 64),
                ["ServerReservedSlots"] = Integer("ServerReservedSlots", "Slots", 0, null),
                ["ServerReservedSlotsPermission"] = Integer("ServerReservedSlotsPermission", "Slots", 0, 1000),
                ["ServerAdminSlots"] = Integer("ServerAdminSlots", "Slots", 0, null),
                ["ServerAdminSlotsPermission"] = Integer("ServerAdminSlotsPermission", "Slots", 0, 1000),

                ["WebDashboardEnabled"] = Boolean("WebDashboardEnabled", "Administration"),
                ["WebDashboardPort"] = Integer("WebDashboardPort", "Administration", 1, 65535),
                ["WebDashboardUrl"] = Text("WebDashboardUrl", "Administration", true),
                ["EnableMapRendering"] = Boolean("EnableMapRendering", "Administration"),
                ["TelnetEnabled"] = Boolean("TelnetEnabled", "Administration"),
                ["TelnetPort"] = Integer("TelnetPort", "Administration", 1, 65535),
                ["TelnetPassword"] = Sensitive("TelnetPassword"),
                ["TelnetFailedLoginLimit"] = Integer("TelnetFailedLoginLimit", "Administration", 0, null),
                ["TelnetFailedLoginsBlocktime"] = Integer("TelnetFailedLoginsBlocktime", "Administration", 0, null),
                ["TerminalWindowEnabled"] = Boolean("TerminalWindowEnabled", "Administration"),
                ["AdminFileName"] = Text("AdminFileName", "Administration", true),

                ["ServerAllowCrossplay"] = Boolean("ServerAllowCrossplay", "Security"),
                ["EACEnabled"] = Boolean("EACEnabled", "Security"),
                ["IgnoreEOSSanctions"] = Boolean("IgnoreEOSSanctions", "Security"),
                ["HideCommandExecutionLog"] = Enum("HideCommandExecutionLog", "Security", "0", "1", "2", "3"),
                ["MaxUncoveredMapChunksPerPlayer"] = Integer("MaxUncoveredMapChunksPerPlayer", "Persistence", 0, null),
                ["PersistentPlayerProfiles"] = Boolean("PersistentPlayerProfiles", "Persistence"),
                ["MaxChunkAge"] = Integer("MaxChunkAge", "Persistence", null, null),
                ["SaveDataLimit"] = Integer("SaveDataLimit", "Persistence", null, null),

                ["GameWorld"] = Text("GameWorld", "World", true),
                ["WorldGenSeed"] = Text("WorldGenSeed", "World", true),
                ["WorldGenSize"] = Enum("WorldGenSize", "World", "6144", "8192", "10240"),
                ["GameName"] = Text("GameName", "World", true),
                ["GameMode"] = Enum("GameMode", "World", "GameModeSurvival"),
                ["PlayerSafeZoneLevel"] = Integer("PlayerSafeZoneLevel", "Gameplay", 0, null),
                ["PlayerSafeZoneHours"] = Integer("PlayerSafeZoneHours", "Gameplay", 0, null),
                ["BuildCreate"] = Boolean("BuildCreate", "Gameplay"),
                ["BedrollDeadZoneSize"] = Integer("BedrollDeadZoneSize", "Gameplay", 0, null),
                ["BedrollExpiryTime"] = Integer("BedrollExpiryTime", "Gameplay", 0, null),
                ["AllowSpawnNearFriend"] = Enum("AllowSpawnNearFriend", "Gameplay", "0", "1", "2"),
                ["CameraRestrictionMode"] = Enum("CameraRestrictionMode", "Gameplay", "0", "1", "2"),
                ["MaxSpawnedZombies"] = Integer("MaxSpawnedZombies", "Performance", 0, null),
                ["MaxSpawnedAnimals"] = Integer("MaxSpawnedAnimals", "Performance", 0, null),
                ["ServerMaxAllowedViewDistance"] = Integer("ServerMaxAllowedViewDistance", "Performance", 6, 12),
                ["MaxQueuedMeshLayers"] = Integer("MaxQueuedMeshLayers", "Performance", 0, null),
                ["PartySharedKillRange"] = Integer("PartySharedKillRange", "Gameplay", 0, null),
                ["PlayerKillingMode"] = Enum("PlayerKillingMode", "Gameplay", "0", "1", "2", "3"),
                ["LandClaimCount"] = Integer("LandClaimCount", "LandClaim", 0, null),
                ["LandClaimSize"] = Integer("LandClaimSize", "LandClaim", 0, null),
                ["LandClaimDeadZone"] = Integer("LandClaimDeadZone", "LandClaim", 0, null),
                ["LandClaimExpiryTime"] = Integer("LandClaimExpiryTime", "LandClaim", 0, null),
                ["LandClaimDecayMode"] = Enum("LandClaimDecayMode", "LandClaim", "0", "1", "2"),
                ["LandClaimOnlineDurabilityModifier"] = Integer("LandClaimOnlineDurabilityModifier", "LandClaim", 0, null),
                ["LandClaimOfflineDurabilityModifier"] = Integer("LandClaimOfflineDurabilityModifier", "LandClaim", 0, null),
                ["LandClaimOfflineDelay"] = Integer("LandClaimOfflineDelay", "LandClaim", 0, null),
                ["DynamicMeshEnabled"] = Boolean("DynamicMeshEnabled", "Performance"),
                ["DynamicMeshLandClaimOnly"] = Boolean("DynamicMeshLandClaimOnly", "Performance"),
                ["DynamicMeshLandClaimBuffer"] = Integer("DynamicMeshLandClaimBuffer", "Performance", 0, null),
                ["DynamicMeshMaxItemCache"] = Integer("DynamicMeshMaxItemCache", "Performance", 0, null),
                ["TwitchServerPermission"] = Integer("TwitchServerPermission", "Integrations", 0, 1000),
                ["TwitchBloodMoonAllowed"] = Boolean("TwitchBloodMoonAllowed", "Integrations"),
                ["SandboxCode"] = Text("SandboxCode", "Gameplay", true),

                // Retained as typed compatibility metadata for older configurations.
                ["GameDifficulty"] = Integer("GameDifficulty", "Gameplay", 0, 5),
                ["DayNightLength"] = Integer("DayNightLength", "Gameplay", 1, 240),
                ["ControlPanelPassword"] = Sensitive("ControlPanelPassword")
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
            var restrictedPath = IsPathKey(key);
            return new ServerConfigurationFieldDefinition(
                key, "Advanced", ServerConfigurationValueType.Text, !sensitive && !restrictedPath, true, sensitive, true, NoValues, null, null);
        }

        private static ServerConfigurationFieldDefinition Text(string key, string group, bool editable)
        {
            return new ServerConfigurationFieldDefinition(key, group, ServerConfigurationValueType.Text, editable, false, false, true, NoValues, null, null);
        }

        private static ServerConfigurationFieldDefinition Integer(string key, string group, decimal? minimum, decimal? maximum)
        {
            return new ServerConfigurationFieldDefinition(key, group, ServerConfigurationValueType.Integer, true, false, false, true, NoValues, minimum, maximum);
        }

        private static ServerConfigurationFieldDefinition Boolean(string key, string group)
        {
            return new ServerConfigurationFieldDefinition(key, group, ServerConfigurationValueType.Boolean, true, false, false, true, NoValues, null, null);
        }

        private static ServerConfigurationFieldDefinition Enum(string key, string group, params string[] allowedValues)
        {
            return new ServerConfigurationFieldDefinition(key, group, ServerConfigurationValueType.Enum, true, false, false, true, Array.AsReadOnly(allowedValues), null, null);
        }

        private static ServerConfigurationFieldDefinition Sensitive(string key)
        {
            return new ServerConfigurationFieldDefinition(key, "Security", ServerConfigurationValueType.Text, false, false, true, true, NoValues, null, null);
        }

        private static bool IsSensitiveKey(string key)
        {
            return key.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsPathKey(string key)
        {
            return key.IndexOf("path", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("folder", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("directory", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
