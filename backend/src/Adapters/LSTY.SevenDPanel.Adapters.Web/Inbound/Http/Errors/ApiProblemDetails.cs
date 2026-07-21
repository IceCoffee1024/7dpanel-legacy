using Newtonsoft.Json;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors
{
    internal sealed class ApiProblemDetails
    {
        [JsonProperty("type", Order = 1)]
        public string Type { get; set; } = "about:blank";

        [JsonProperty("title", Order = 2)]
        public string Title { get; set; } = string.Empty;

        [JsonProperty("status", Order = 3)]
        public int Status { get; set; }

        [JsonProperty("detail", Order = 4)]
        public string Detail { get; set; } = string.Empty;

        [JsonProperty("instance", Order = 5)]
        public string Instance { get; set; } = string.Empty;

        [JsonProperty("code", Order = 6)]
        public string Code { get; set; } = string.Empty;

        [JsonProperty("traceId", Order = 7)]
        public string TraceId { get; set; } = string.Empty;
    }
}
