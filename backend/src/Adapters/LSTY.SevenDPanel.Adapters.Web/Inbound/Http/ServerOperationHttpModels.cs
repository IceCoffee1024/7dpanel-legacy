using System;
using LSTY.SevenDPanel.Application;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [JsonConverter(typeof(ConfirmedServerOperationRequestConverter))]
    public sealed class ConfirmedServerOperationRequest
    {
        public bool Confirmed { get; set; }
    }

    internal sealed class ConfirmedServerOperationRequestConverter : JsonConverter
    {
        public override bool CanWrite => false;

        public override bool CanConvert(Type objectType) =>
            objectType == typeof(ConfirmedServerOperationRequest);

        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object? existingValue,
            JsonSerializer serializer)
        {
            var payload = JObject.Load(reader);
            foreach (var property in payload.Properties())
            {
                if (!string.Equals(property.Name, "confirmed", StringComparison.OrdinalIgnoreCase))
                    throw new JsonSerializationException("The request contains an unsupported member.");
            }

            var confirmed = payload.GetValue("confirmed", StringComparison.OrdinalIgnoreCase);
            return new ConfirmedServerOperationRequest
            {
                Confirmed = confirmed?.ToObject<bool>(serializer) ?? false
            };
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) =>
            throw new NotSupportedException();
    }

    public sealed class RestartServerOperationHttpResponse
    {
        internal RestartServerOperationHttpResponse(ServerOperationResult result)
        {
            OperationId = result.OperationId;
            Code = result.Status;
            RequestedAtUtc = result.RequestedAtUtc;
            ScriptStartedAtUtc = result.AcceptedAtUtc;
            AuditStatus = result.AuditStatus;
        }

        public string OperationId { get; }
        public string Code { get; }
        public DateTimeOffset RequestedAtUtc { get; }
        public DateTimeOffset ScriptStartedAtUtc { get; }
        public string AuditStatus { get; }
    }

    public sealed class ShutdownServerOperationHttpResponse
    {
        internal ShutdownServerOperationHttpResponse(ServerOperationResult result)
        {
            OperationId = result.OperationId;
            Code = result.Status;
            RequestedAtUtc = result.RequestedAtUtc;
            AcceptedAtUtc = result.AcceptedAtUtc;
            AuditStatus = result.AuditStatus;
        }

        public string OperationId { get; }
        public string Code { get; }
        public DateTimeOffset RequestedAtUtc { get; }
        public DateTimeOffset AcceptedAtUtc { get; }
        public string AuditStatus { get; }
    }
}
