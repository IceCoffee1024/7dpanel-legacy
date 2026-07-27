using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application.Discord;
using Newtonsoft.Json.Linq;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [OwnerAuthorize]
    [RoutePrefix("api/v1/integrations/discord")]
    public sealed class DiscordIntegrationController : ApiController
    {
        private const string DiscordPath = "/api/v1/integrations/discord";
        private const string TestMessage = "7DPanel Discord integration test.";
        private static readonly TimeSpan BindingCodeLifetime = TimeSpan.FromMinutes(10);
        private readonly IDiscordIntegrationStore store;
        private readonly IDiscordInteractionSignatureVerifier? interactionSignatureVerifier;
        private readonly IDiscordInteractionPersistenceStore? interactionStore;
        private readonly IDiscordDeferredInteractionSink? deferredInteractionSink;

        public DiscordIntegrationController(IDiscordIntegrationStore store) =>
            this.store = store ?? throw new ArgumentNullException(nameof(store));

        public DiscordIntegrationController(
            IDiscordIntegrationStore store,
            IDiscordInteractionSignatureVerifier interactionSignatureVerifier)
            : this(store)
        {
            this.interactionSignatureVerifier = interactionSignatureVerifier ??
                throw new ArgumentNullException(nameof(interactionSignatureVerifier));
            interactionStore = store as IDiscordInteractionPersistenceStore;
        }

        public DiscordIntegrationController(
            IDiscordIntegrationStore store,
            IDiscordInteractionSignatureVerifier interactionSignatureVerifier,
            IDiscordDeferredInteractionSink deferredInteractionSink)
            : this(store, interactionSignatureVerifier)
        {
            this.deferredInteractionSink = deferredInteractionSink ??
                throw new ArgumentNullException(nameof(deferredInteractionSink));
        }

        [HttpGet]
        [Route("")]
        [ResponseType(typeof(DiscordConfigurationHttpResponse))]
        public HttpResponseMessage GetConfiguration()
        {
            try
            {
                var summary = new GetDiscordConfigurationUseCase(store).Execute();
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new DiscordConfigurationHttpResponse(summary));
            }
            catch
            {
                return Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "discord_configuration_unavailable",
                    "Discord integration configuration is temporarily unavailable.");
            }
        }

        [HttpPut]
        [Route("")]
        [ResponseType(typeof(DiscordConfigurationHttpResponse))]
        public HttpResponseMessage PutConfiguration(
            DiscordConfigurationUpdateHttpRequest? request)
        {
            if (!ModelState.IsValid || !TryBuildUpdate(request, out var update))
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);

            try
            {
                var summary = new SaveDiscordConfigurationUseCase(
                        store,
                        () => DateTimeOffset.UtcNow)
                    .Execute(update!);
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new DiscordConfigurationHttpResponse(summary));
            }
            catch (DiscordIntegrationVersionConflictException)
            {
                return Problem(
                    HttpStatusCode.Conflict,
                    "discord_settings_version_conflict",
                    "Discord integration settings changed before the update completed.");
            }
            catch (ArgumentException)
            {
                return Problem(
                    HttpStatusCode.BadRequest,
                    "invalid_discord_configuration",
                    "The Discord integration configuration is invalid.");
            }
            catch
            {
                return Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "discord_configuration_update_unavailable",
                    "Discord integration configuration could not be updated.");
            }
        }

        [HttpPost]
        [Route("test")]
        [ResponseType(typeof(DiscordDeliveryHttpResponse))]
        public HttpResponseMessage Test(DiscordTestHttpRequest? request)
        {
            if (!ModelState.IsValid || request == null ||
                string.IsNullOrWhiteSpace(request.TargetKey))
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);

            var targetKey = request.TargetKey!.Trim();
            try
            {
                var target = store.FindTarget(targetKey);
                if (target == null)
                {
                    return Problem(
                        HttpStatusCode.NotFound,
                        "discord_target_not_found",
                        "The Discord delivery target was not found.");
                }
                if (!target.IsEnabled)
                {
                    return Problem(
                        HttpStatusCode.Conflict,
                        "discord_target_disabled",
                        "The Discord delivery target is disabled.");
                }

                var operationId = Guid.NewGuid().ToString("N");
                var delivery = new EnqueueDiscordDeliveryUseCase(
                        store,
                        () => DateTimeOffset.UtcNow,
                        () => operationId)
                    .Execute("discord-test:" + operationId, targetKey, TestMessage);
                return Request.CreateResponse(
                    HttpStatusCode.Accepted,
                    new DiscordDeliveryHttpResponse(delivery));
            }
            catch (DiscordIntegrationDisabledException)
            {
                return Problem(
                    HttpStatusCode.Conflict,
                    "discord_integration_disabled",
                    "Discord integration is disabled.");
            }
            catch (ArgumentException)
            {
                return Problem(
                    HttpStatusCode.BadRequest,
                    "invalid_discord_test_request",
                    "The Discord integration test request is invalid.");
            }
            catch
            {
                return Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "discord_test_unavailable",
                    "The Discord integration test could not be queued.");
            }
        }

        [HttpGet]
        [Route("deliveries")]
        [ResponseType(typeof(DiscordDeliveryHttpResponse[]))]
        public HttpResponseMessage GetDeliveries()
        {
            if (!(store is IDiscordIntegrationAdministrationStore administration))
                return Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "discord_deliveries_query_unavailable",
                    "Discord delivery history is temporarily unavailable.");
            try
            {
                var deliveries = administration.ListDeliveries(100)
                    .Select(DiscordDeliverySummary.FromDelivery)
                    .Select(delivery => new DiscordDeliveryHttpResponse(delivery))
                    .ToArray();
                return Request.CreateResponse(HttpStatusCode.OK, deliveries);
            }
            catch
            {
                return Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "discord_deliveries_query_unavailable",
                    "Discord delivery history is temporarily unavailable.");
            }
        }

        [HttpPost]
        [Route("deliveries/{deliveryId}/retry")]
        [ResponseType(typeof(DiscordDeliveryHttpResponse))]
        public HttpResponseMessage RetryDelivery(string deliveryId)
        {
            if (string.IsNullOrWhiteSpace(deliveryId))
            {
                return Problem(
                    HttpStatusCode.BadRequest,
                    "invalid_discord_delivery_id",
                    "A Discord delivery identifier is required.");
            }

            try
            {
                var delivery = store.FindDelivery(deliveryId.Trim());
                if (delivery == null)
                {
                    return Problem(
                        HttpStatusCode.NotFound,
                        "discord_delivery_not_found",
                        "The Discord delivery was not found.");
                }
                if (string.IsNullOrEmpty(delivery.ContentText))
                {
                    return Problem(
                        HttpStatusCode.Conflict,
                        "discord_delivery_not_retryable",
                        "The Discord delivery no longer has retryable content.");
                }

                var retried = new RetryDiscordDeliveryUseCase(
                        store,
                        () => DateTimeOffset.UtcNow)
                    .Execute(delivery.DeliveryId, delivery.ContentText!);
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new DiscordDeliveryHttpResponse(retried));
            }
            catch (DiscordIntegrationDisabledException)
            {
                return Problem(
                    HttpStatusCode.Conflict,
                    "discord_integration_disabled",
                    "Discord integration is disabled.");
            }
            catch (InvalidOperationException exception) when (string.Equals(
                exception.Message,
                "discord_delivery_not_retryable",
                StringComparison.Ordinal))
            {
                return Problem(
                    HttpStatusCode.Conflict,
                    "discord_delivery_not_retryable",
                    "The Discord delivery cannot be retried from its current state.");
            }
            catch (ArgumentException)
            {
                return Problem(
                    HttpStatusCode.BadRequest,
                    "invalid_discord_delivery_retry",
                    "The Discord delivery retry request is invalid.");
            }
            catch
            {
                return Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "discord_delivery_retry_unavailable",
                    "The Discord delivery could not be scheduled for retry.");
            }
        }

        [HttpGet]
        [Route("bindings")]
        [ResponseType(typeof(DiscordBindingHttpResponse[]))]
        public HttpResponseMessage GetBindings()
        {
            if (!(store is IDiscordIntegrationAdministrationStore administration))
                return Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "discord_bindings_query_unavailable",
                    "Discord bindings are temporarily unavailable.");
            try
            {
                var bindings = administration.ListBindings()
                    .Select(binding => new DiscordBindingHttpResponse(binding))
                    .ToArray();
                return Request.CreateResponse(HttpStatusCode.OK, bindings);
            }
            catch
            {
                return Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "discord_bindings_query_unavailable",
                    "Discord bindings are temporarily unavailable.");
            }
        }

        [HttpPost]
        [Route("binding-codes")]
        [ResponseType(typeof(DiscordBindingCodeHttpResponse))]
        public HttpResponseMessage CreateBindingCode(
            DiscordBindingCodeCreateHttpRequest? request)
        {
            if (!ModelState.IsValid || request == null ||
                string.IsNullOrWhiteSpace(request.CrossplatformId) ||
                request.CrossplatformId!.Trim().Length > 128)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);

            try
            {
                var code = CreateOneTimeCode();
                var prefix = code.Substring(0, 4);
                var expiresAtUtc = DateTimeOffset.UtcNow.Add(BindingCodeLifetime);
                store.SaveBindingCode(new DiscordBindingCode(
                    Guid.NewGuid().ToString("N"),
                    request.CrossplatformId.Trim(),
                    prefix,
                    DiscordBindingCodeHash.Compute(code),
                    expiresAtUtc));
                return Request.CreateResponse(
                    HttpStatusCode.Created,
                    new DiscordBindingCodeHttpResponse(code, prefix, expiresAtUtc));
            }
            catch (ArgumentException)
            {
                return Problem(
                    HttpStatusCode.BadRequest,
                    "invalid_discord_binding_code_request",
                    "The Discord binding-code request is invalid.");
            }
            catch
            {
                return Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "discord_binding_code_unavailable",
                    "A Discord binding code could not be created.");
            }
        }

        [HttpDelete]
        [Route("bindings/{discordSubject}")]
        [ResponseType(typeof(void))]
        public HttpResponseMessage DeleteBinding(string discordSubject)
        {
            if (string.IsNullOrWhiteSpace(discordSubject))
                return Problem(
                    HttpStatusCode.BadRequest,
                    "invalid_discord_subject",
                    "A Discord subject is required.");
            if (!(store is IDiscordIntegrationAdministrationStore administration))
                return Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "discord_binding_delete_unavailable",
                    "Discord bindings cannot be removed at this time.");
            try
            {
                return administration.DisableBinding(
                        discordSubject.Trim(),
                        DateTimeOffset.UtcNow)
                    ? Request.CreateResponse(HttpStatusCode.NoContent)
                    : Problem(
                        HttpStatusCode.NotFound,
                        "discord_binding_not_found",
                        "The active Discord binding was not found.");
            }
            catch
            {
                return Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "discord_binding_delete_unavailable",
                    "Discord bindings cannot be removed at this time.");
            }
        }

        [HttpGet]
        [Route("commands")]
        [ResponseType(typeof(DiscordCommandHttpResponse[]))]
        public HttpResponseMessage GetCommands()
        {
            try
            {
                var commands = store.ListCommandSettings()
                    .Where(command => DiscordSlashCommandNames.IsAllowed(command.CommandKey))
                    .OrderBy(command => command.CommandKey, StringComparer.Ordinal)
                    .Select(command => new DiscordCommandHttpResponse(command))
                    .ToArray();
                return Request.CreateResponse(HttpStatusCode.OK, commands);
            }
            catch
            {
                return Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "discord_commands_unavailable",
                    "Discord command settings are temporarily unavailable.");
            }
        }

        [AllowAnonymous]
        [HttpPost]
        [Route("interactions")]
        [ResponseType(typeof(void))]
        public async Task<HttpResponseMessage> PostInteraction(
            CancellationToken cancellationToken)
        {
            if (interactionSignatureVerifier == null)
            {
                return Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "discord_interaction_verification_unavailable",
                    "Discord interaction signature verification is not available.");
            }

            var signature = SingleHeader("X-Signature-Ed25519");
            var timestamp = SingleHeader("X-Signature-Timestamp");
            if (signature == null || timestamp == null)
                return InvalidInteractionSignature();

            byte[] rawBody;
            try
            {
                if (Request.Content == null ||
                    Request.Content.Headers.ContentLength > 64 * 1024)
                    return InvalidInteractionBody();
                rawBody = await Request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                if (rawBody.Length > 64 * 1024) return InvalidInteractionBody();
            }
            catch
            {
                return InvalidInteractionBody();
            }

            bool verified;
            try
            {
                verified = interactionSignatureVerifier.Verify(signature, timestamp, rawBody);
            }
            catch
            {
                verified = false;
            }
            if (!verified) return InvalidInteractionSignature();

            JObject payload;
            try
            {
                payload = JObject.Parse(System.Text.Encoding.UTF8.GetString(rawBody));
            }
            catch
            {
                return InvalidInteractionBody();
            }

            var interactionType = (int?)payload["type"];
            if (interactionType == 1)
                return Request.CreateResponse(HttpStatusCode.OK, new { type = 1 });
            if (!TryMapInteraction(payload, out var interaction, out var interactionToken))
                return InvalidInteractionBody();
            if (deferredInteractionSink != null)
            {
                try
                {
                    var accepted = deferredInteractionSink.AcceptInteraction(
                        interaction!,
                        interactionToken!);
                    if (accepted.Disposition == DiscordInboundDisposition.NotRunning)
                    {
                        return Problem(
                            HttpStatusCode.ServiceUnavailable,
                            "discord_interaction_processing_unavailable",
                            "Discord interaction processing is temporarily unavailable.");
                    }
                    return Request.CreateResponse(HttpStatusCode.OK, new { type = 5 });
                }
                catch
                {
                    return Problem(
                        HttpStatusCode.ServiceUnavailable,
                        "discord_interaction_processing_unavailable",
                        "Discord interaction processing is temporarily unavailable.");
                }
            }
            if (interactionStore == null)
            {
                return Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "discord_interaction_acceptance_unavailable",
                    "Discord interaction acceptance is temporarily unavailable.");
            }

            try
            {
                new AcceptDiscordInteractionUseCase(
                        store,
                        interactionStore,
                        () => DateTimeOffset.UtcNow)
                    .Execute(interaction!, interactionToken!);
                return Request.CreateResponse(HttpStatusCode.OK, new { type = 5 });
            }
            catch
            {
                return Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "discord_interaction_processing_unavailable",
                    "Discord interaction processing is temporarily unavailable.");
            }
        }

        private string? SingleHeader(string name)
        {
            if (!Request.Headers.TryGetValues(name, out var values)) return null;
            var headers = values.Take(2).ToArray();
            return headers.Length == 1 && !string.IsNullOrEmpty(headers[0])
                ? headers[0]
                : null;
        }

        private HttpResponseMessage InvalidInteractionSignature() => Problem(
            HttpStatusCode.Unauthorized,
            "discord_interaction_signature_invalid",
            "The Discord interaction signature is invalid.");

        private HttpResponseMessage InvalidInteractionBody() => Problem(
            HttpStatusCode.BadRequest,
            "discord_interaction_body_invalid",
            "The Discord interaction body is invalid.");

        private static bool TryMapInteraction(
            JObject payload,
            out DiscordInteractionEnvelope? interaction,
            out string? interactionToken)
        {
            interaction = null;
            interactionToken = null;
            try
            {
                var member = payload["member"] as JObject;
                var user = member?["user"] as JObject ?? payload["user"] as JObject;
                var data = payload["data"] as JObject;
                interactionToken = (string?)payload["token"];
                if (string.IsNullOrWhiteSpace(interactionToken)) return false;
                var options = data?["options"] as JArray;
                var bindingCode = options?
                    .OfType<JObject>()
                    .FirstOrDefault(option => string.Equals(
                        (string?)option["name"],
                        "code",
                        StringComparison.OrdinalIgnoreCase))?["value"]?
                    .Value<string>();
                interaction = new DiscordInteractionEnvelope(
                    (string)payload["id"]!,
                    (int)payload["type"]!,
                    (string)payload["guild_id"]!,
                    (string)payload["channel_id"]!,
                    (string)user!["id"]!,
                    (bool?)user["bot"] ?? false,
                    (string)data!["name"]!,
                    bindingCode);
                return true;
            }
            catch
            {
                interaction = null;
                interactionToken = null;
                return false;
            }
        }

        private static bool TryBuildUpdate(
            DiscordConfigurationUpdateHttpRequest? request,
            out DiscordConfigurationUpdate? update)
        {
            update = null;
            if (request == null ||
                !request.ExpectedVersion.HasValue ||
                request.ExpectedVersion.Value < 0 ||
                request.ExpectedVersion.Value == long.MaxValue ||
                !request.IsEnabled.HasValue ||
                !request.BridgeGameToDiscord.HasValue ||
                !request.BridgeDiscordToGame.HasValue ||
                !request.ProxyEnabled.HasValue ||
                request.Targets == null ||
                !Enum.TryParse(request.Mode, false, out DiscordIntegrationMode mode) ||
                !Enum.IsDefined(typeof(DiscordIntegrationMode), mode) ||
                !TryProxyEndpoint(
                    request.ProxyEnabled.Value,
                    request.ProxyEndpoint,
                    out var proxyEndpoint))
                return false;

            var targets = new DiscordTarget[request.Targets.Length];
            for (var index = 0; index < request.Targets.Length; index++)
            {
                var target = request.Targets[index];
                if (target == null ||
                    string.IsNullOrWhiteSpace(target.TargetKey) ||
                    string.IsNullOrWhiteSpace(target.DeliveryMode) ||
                    !target.IsEnabled.HasValue)
                    return false;
                targets[index] = new DiscordTarget(
                    target.TargetKey!.Trim(),
                    target.DeliveryMode!.Trim(),
                    NormalizeOptional(target.ChannelId),
                    target.IsEnabled.Value);
            }

            update = new DiscordConfigurationUpdate(
                request.ExpectedVersion.Value,
                request.IsEnabled.Value,
                mode,
                NormalizeOptional(request.ApplicationId),
                NormalizeOptional(request.GuildId),
                NormalizeOptional(request.PublicChannelId),
                request.BridgeGameToDiscord.Value,
                request.BridgeDiscordToGame.Value,
                request.ProxyEnabled.Value,
                proxyEndpoint,
                targets);
            return true;
        }

        private static bool TryProxyEndpoint(
            bool enabled,
            string? value,
            out string? endpoint)
        {
            endpoint = null;
            if (!enabled) return string.IsNullOrWhiteSpace(value);
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment))
                return false;
            endpoint = uri.AbsoluteUri;
            return true;
        }

        private static string? NormalizeOptional(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

        private static string CreateOneTimeCode()
        {
            var bytes = new byte[9];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return BitConverter.ToString(bytes).Replace("-", string.Empty);
        }

        private HttpResponseMessage Problem(
            HttpStatusCode status,
            string code,
            string detail) =>
            ApiProblemDetailsFactory.CreateResponse(
                Request,
                status,
                code,
                detail,
                DiscordPath);
    }
}
