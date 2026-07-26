using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Application.ServerConfiguration;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class UpdateServerConfigurationHttpRequest
    {
        public string? Value { get; set; }
        public string? Version { get; set; }
    }

    public sealed class ServerConfigurationSnapshotResponse
    {
        public ServerConfigurationSnapshotResponse(ServerConfigurationSnapshot snapshot)
        {
            Version = snapshot.Version;
            ReadAtUtc = snapshot.ReadAtUtc;
            Fields = snapshot.Fields.Select(field => new ServerConfigurationFieldResponse(field)).ToArray();
        }

        public string Version { get; }
        public DateTimeOffset ReadAtUtc { get; }
        public IReadOnlyList<ServerConfigurationFieldResponse> Fields { get; }
    }

    public sealed class ServerConfigurationFieldResponse
    {
        public ServerConfigurationFieldResponse(ServerConfigurationField field)
        {
            Key = field.Key;
            Value = field.Value;
            Group = field.Group;
            ValueType = field.ValueType.ToString().ToLowerInvariant();
            Editable = field.Editable;
            Advanced = field.Advanced;
            Sensitive = field.Sensitive;
            IsSet = field.IsSet;
            RestartRequired = field.RestartRequired;
            AllowedValues = field.AllowedValues;
            Minimum = field.Minimum;
            Maximum = field.Maximum;
        }

        public string Key { get; }
        public string Value { get; }
        public string Group { get; }
        public string ValueType { get; }
        public bool Editable { get; }
        public bool Advanced { get; }
        public bool Sensitive { get; }
        public bool IsSet { get; }
        public bool RestartRequired { get; }
        public IReadOnlyList<string> AllowedValues { get; }
        public decimal? Minimum { get; }
        public decimal? Maximum { get; }
    }

    public sealed class ServerConfigurationUpdateResponse
    {
        public ServerConfigurationUpdateResponse(ServerConfigurationUpdateResult result)
        {
            Version = result.Version;
            SavedAtUtc = result.SavedAtUtc;
            RestartRequired = result.RestartRequired;
        }

        public string Version { get; }
        public DateTimeOffset? SavedAtUtc { get; }
        public bool RestartRequired { get; }
    }
}
