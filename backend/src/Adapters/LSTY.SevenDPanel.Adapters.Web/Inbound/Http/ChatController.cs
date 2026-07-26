using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application.Chat;
using LSTY.SevenDPanel.Hosting;
using LSTY.SevenDPanel.Hosting.ServerEvents;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [Authorize(Roles = "Owner")]
    [RoutePrefix("api/v1/chat")]
    public sealed class ChatController : ApiController
    {
        private readonly IRecentChatMessageQuery recentMessages;
        private readonly IPanelRuntimeStatus runtimeStatus;
        private readonly GetChatHistoryUseCase getHistory;
        private readonly GetChatSettingsUseCase getSettings;
        private readonly SaveChatSettingsUseCase saveSettings;
        private readonly ResetChatSettingsUseCase resetSettings;
        private readonly GetColoredChatSettingsUseCase getColoredSettings;
        private readonly SaveColoredChatSettingsUseCase saveColoredSettings;
        private readonly ResetColoredChatSettingsUseCase resetColoredSettings;
        private readonly GetColoredChatProfilesUseCase getProfiles;
        private readonly CreateColoredChatProfileUseCase createProfile;
        private readonly UpdateColoredChatProfileUseCase updateProfile;
        private readonly DeleteColoredChatProfileUseCase deleteProfile;
        private readonly SendGlobalChatMessageUseCase sendGlobal;
        private readonly SendPrivateChatMessageUseCase sendPrivate;

        public ChatController(
            IRecentChatMessageQuery recentMessages,
            IPanelRuntimeStatus runtimeStatus,
            GetChatHistoryUseCase getHistory,
            GetChatSettingsUseCase getSettings,
            SaveChatSettingsUseCase saveSettings,
            ResetChatSettingsUseCase resetSettings,
            GetColoredChatSettingsUseCase getColoredSettings,
            SaveColoredChatSettingsUseCase saveColoredSettings,
            ResetColoredChatSettingsUseCase resetColoredSettings,
            GetColoredChatProfilesUseCase getProfiles,
            CreateColoredChatProfileUseCase createProfile,
            UpdateColoredChatProfileUseCase updateProfile,
            DeleteColoredChatProfileUseCase deleteProfile,
            SendGlobalChatMessageUseCase sendGlobal,
            SendPrivateChatMessageUseCase sendPrivate)
        {
            this.recentMessages = recentMessages ?? throw new ArgumentNullException(nameof(recentMessages));
            this.runtimeStatus = runtimeStatus ?? throw new ArgumentNullException(nameof(runtimeStatus));
            this.getHistory = getHistory ?? throw new ArgumentNullException(nameof(getHistory));
            this.getSettings = getSettings ?? throw new ArgumentNullException(nameof(getSettings));
            this.saveSettings = saveSettings ?? throw new ArgumentNullException(nameof(saveSettings));
            this.resetSettings = resetSettings ?? throw new ArgumentNullException(nameof(resetSettings));
            this.getColoredSettings = getColoredSettings ?? throw new ArgumentNullException(nameof(getColoredSettings));
            this.saveColoredSettings = saveColoredSettings ?? throw new ArgumentNullException(nameof(saveColoredSettings));
            this.resetColoredSettings = resetColoredSettings ?? throw new ArgumentNullException(nameof(resetColoredSettings));
            this.getProfiles = getProfiles ?? throw new ArgumentNullException(nameof(getProfiles));
            this.createProfile = createProfile ?? throw new ArgumentNullException(nameof(createProfile));
            this.updateProfile = updateProfile ?? throw new ArgumentNullException(nameof(updateProfile));
            this.deleteProfile = deleteProfile ?? throw new ArgumentNullException(nameof(deleteProfile));
            this.sendGlobal = sendGlobal ?? throw new ArgumentNullException(nameof(sendGlobal));
            this.sendPrivate = sendPrivate ?? throw new ArgumentNullException(nameof(sendPrivate));
        }

        [HttpGet, Route("messages/recent"), ResponseType(typeof(RecentChatMessagesResponse))]
        public HttpResponseMessage GetRecentMessages(int? limit = null)
        {
            if (!ModelState.IsValid || limit is < 1 or > 500)
                return Problem(HttpStatusCode.BadRequest, "invalid_chat_message_limit", "The chat message limit must be from 1 through 500.");
            try
            {
                return Request.CreateResponse(HttpStatusCode.OK,
                    new RecentChatMessagesResponse(recentMessages.ReadRecentChatMessages(limit ?? 200)));
            }
            catch (Exception exception) when (exception is ObjectDisposedException || exception is InvalidOperationException)
            {
                return Problem(HttpStatusCode.ServiceUnavailable, "recent_chat_unavailable", "Recent chat messages are unavailable.");
            }
        }

        [HttpGet, Route("messages"), ResponseType(typeof(ChatHistoryHttpResponse))]
        public HttpResponseMessage GetMessages(
            string? cursor = null, int? limit = null, string? crossplatformId = null,
            string? senderName = null, string? chatType = null, string? sourceKind = null,
            string? startUtc = null, string? endUtc = null)
        {
            if (!TryEnum(chatType, out ChatChannel? channel) ||
                !TryEnum(sourceKind, out ChatSourceKind? source) ||
                !TryUtc(startUtc, out var start) || !TryUtc(endUtc, out var end))
                return InvalidQuery("invalid_chat_history_query", "The chat history query is invalid.");
            var filters = new ChatHistoryCursorFilters(crossplatformId, senderName, channel, source, start, end);
            ChatHistoryKeyset? keyset = null;
            if (cursor != null && !ChatHistoryCursorCodec.TryDecode(cursor, filters, out keyset))
                return InvalidQuery("invalid_chat_history_cursor", "The chat history cursor is invalid for these filters.");
            try
            {
                var query = new ChatHistoryQuery(limit ?? ChatHistoryQuery.DefaultPageSize,
                    crossplatformId, senderName, channel, source, start, end, keyset);
                var page = getHistory.Execute(query);
                var next = page.NextKeyset == null ? null : ChatHistoryCursorCodec.Encode(page.NextKeyset, filters);
                return Request.CreateResponse(HttpStatusCode.OK, new ChatHistoryHttpResponse(page, next));
            }
            catch (ArgumentException)
            {
                return InvalidQuery("invalid_chat_history_query", "The chat history query is invalid.");
            }
            catch
            {
                return Problem(HttpStatusCode.ServiceUnavailable, "chat_history_unavailable", "Chat history is unavailable.");
            }
        }

        [HttpPost, Route("messages/global"), ResponseType(typeof(ChatSendResponse))]
        public Task<HttpResponseMessage> SendGlobalMessage(
            SendChatMessageRequest? body, CancellationToken cancellationToken) =>
            SendMessage(body, null, cancellationToken);

        [HttpPost, Route("messages/private"), ResponseType(typeof(ChatSendResponse))]
        public Task<HttpResponseMessage> SendPrivateMessage(
            SendPrivateChatMessageRequest? body, CancellationToken cancellationToken) =>
            SendMessage(body, body?.TargetCrossplatformId, cancellationToken);

        [HttpGet, Route("settings"), ResponseType(typeof(ChatSettingsHttpModel))]
        public HttpResponseMessage GetChatSettings() => ExecuteRead(
            () => new ChatSettingsHttpModel(getSettings.Execute()), "chat_settings_unavailable", "Chat settings are unavailable.");

        [HttpPut, Route("settings"), ResponseType(typeof(ChatSettingsHttpModel))]
        public HttpResponseMessage PutChatSettings(ChatSettingsHttpModel? body)
        {
            if (!ModelState.IsValid || body == null) return InvalidBody();
            return ExecuteMutation(() => new ChatSettingsHttpModel(saveSettings.Execute(RequireActor(), body.ToApplication())),
                "invalid_chat_settings", "The chat settings are invalid.", "chat_settings_unavailable", "Chat settings could not be saved.");
        }

        [HttpDelete, Route("settings"), ResponseType(typeof(ChatSettingsHttpModel))]
        public HttpResponseMessage DeleteChatSettings() => ExecuteMutation(
            () => new ChatSettingsHttpModel(resetSettings.Execute(RequireActor())),
            "invalid_chat_settings", "The chat settings are invalid.", "chat_settings_unavailable", "Chat settings could not be reset.");

        [HttpGet, Route("colored/settings"), ResponseType(typeof(ColoredChatSettingsHttpModel))]
        public HttpResponseMessage GetColoredSettings() => ExecuteRead(
            () => new ColoredChatSettingsHttpModel(getColoredSettings.Execute()), "colored_chat_settings_unavailable", "Colored chat settings are unavailable.");

        [HttpPut, Route("colored/settings"), ResponseType(typeof(ColoredChatSettingsHttpModel))]
        public HttpResponseMessage PutColoredSettings(ColoredChatSettingsHttpModel? body)
        {
            if (!ModelState.IsValid || body == null) return InvalidBody();
            return ExecuteMutation(() => new ColoredChatSettingsHttpModel(saveColoredSettings.Execute(RequireActor(), body.ToApplication())),
                "invalid_colored_chat_settings", "The colored chat settings are invalid.",
                "colored_chat_settings_unavailable", "Colored chat settings could not be saved.");
        }

        [HttpDelete, Route("colored/settings"), ResponseType(typeof(ColoredChatSettingsHttpModel))]
        public HttpResponseMessage DeleteColoredSettings() => ExecuteMutation(
            () => new ColoredChatSettingsHttpModel(resetColoredSettings.Execute(RequireActor())),
            "invalid_colored_chat_settings", "The colored chat settings are invalid.",
            "colored_chat_settings_unavailable", "Colored chat settings could not be reset.");

        [HttpGet, Route("colored/profiles"), ResponseType(typeof(ColoredChatProfilesHttpResponse))]
        public HttpResponseMessage GetColoredProfiles(
            string? cursor = null, int? limit = null, string? crossplatformId = null,
            string? customName = null, string? nameColor = null, string? textColor = null,
            string? createdAfterUtc = null, string? createdBeforeUtc = null)
        {
            if (!TryUtc(createdAfterUtc, out var after) || !TryUtc(createdBeforeUtc, out var before))
                return InvalidQuery("invalid_colored_chat_profile_query", "The colored chat profile query is invalid.");
            var filters = new ColoredChatProfileCursorFilters(
                crossplatformId, customName, nameColor, textColor, after, before);
            ColoredChatProfileKeyset? keyset = null;
            if (cursor != null && !ChatHistoryCursorCodec.TryDecodeProfile(cursor, filters, out keyset))
                return InvalidQuery("invalid_colored_chat_profile_cursor", "The colored chat profile cursor is invalid for these filters.");
            try
            {
                var query = new ColoredChatProfileQuery(limit ?? ColoredChatProfileQuery.DefaultPageSize,
                    crossplatformId, customName, nameColor, textColor, after, before, keyset);
                var page = getProfiles.Execute(query);
                var next = page.NextKeyset == null ? null : ChatHistoryCursorCodec.EncodeProfile(page.NextKeyset, filters);
                return Request.CreateResponse(HttpStatusCode.OK, new ColoredChatProfilesHttpResponse(page, next));
            }
            catch (ArgumentException)
            {
                return InvalidQuery("invalid_colored_chat_profile_query", "The colored chat profile query is invalid.");
            }
            catch
            {
                return Problem(HttpStatusCode.ServiceUnavailable, "colored_chat_profiles_unavailable", "Colored chat profiles are unavailable.");
            }
        }

        [HttpPost, Route("colored/profiles"), ResponseType(typeof(ColoredChatProfileHttpResponse))]
        public HttpResponseMessage PostColoredProfile(CreateColoredChatProfileRequest? body)
        {
            if (!ModelState.IsValid || body == null) return InvalidBody();
            try
            {
                var now = DateTimeOffset.UtcNow;
                var saved = createProfile.Execute(RequireActor(), ToProfile(body.CrossplatformId, body, now, now));
                return Request.CreateResponse(HttpStatusCode.Created, new ColoredChatProfileHttpResponse(saved));
            }
            catch (ColoredChatProfileConflictException)
            {
                return Problem(HttpStatusCode.Conflict, "colored_chat_profile_conflict", "A colored chat profile already exists for this identity.");
            }
            catch (ArgumentException)
            {
                return InvalidQuery("invalid_colored_chat_profile", "The colored chat profile is invalid.");
            }
            catch (UnauthorizedAccessException)
            {
                return AuthenticationRequired();
            }
            catch
            {
                return Problem(HttpStatusCode.ServiceUnavailable, "colored_chat_profiles_unavailable", "The colored chat profile could not be created.");
            }
        }

        [HttpPut, Route("colored/profiles/{crossplatformId}"), ResponseType(typeof(ColoredChatProfileHttpResponse))]
        public HttpResponseMessage PutColoredProfile(string crossplatformId, ColoredChatProfileWriteRequest? body)
        {
            if (!ModelState.IsValid || body == null) return InvalidBody();
            try
            {
                var existing = FindProfile(crossplatformId);
                if (existing == null)
                    return Problem(HttpStatusCode.NotFound, "colored_chat_profile_not_found", "The colored chat profile was not found.");
                var saved = updateProfile.Execute(RequireActor(),
                    ToProfile(crossplatformId, body, existing.CreatedAtUtc, DateTimeOffset.UtcNow));
                return Request.CreateResponse(HttpStatusCode.OK, new ColoredChatProfileHttpResponse(saved));
            }
            catch (ColoredChatProfileNotFoundException)
            {
                return Problem(HttpStatusCode.NotFound, "colored_chat_profile_not_found", "The colored chat profile was not found.");
            }
            catch (ArgumentException)
            {
                return InvalidQuery("invalid_colored_chat_profile", "The colored chat profile is invalid.");
            }
            catch (UnauthorizedAccessException)
            {
                return AuthenticationRequired();
            }
            catch
            {
                return Problem(HttpStatusCode.ServiceUnavailable, "colored_chat_profiles_unavailable", "The colored chat profile could not be updated.");
            }
        }

        [HttpDelete, Route("colored/profiles/{crossplatformId}")]
        public HttpResponseMessage DeleteColoredProfile(string crossplatformId)
        {
            try
            {
                deleteProfile.Execute(RequireActor(), crossplatformId);
                return Request.CreateResponse(HttpStatusCode.NoContent);
            }
            catch (ColoredChatProfileNotFoundException)
            {
                return Problem(HttpStatusCode.NotFound, "colored_chat_profile_not_found", "The colored chat profile was not found.");
            }
            catch (ArgumentException)
            {
                return InvalidQuery("invalid_colored_chat_profile_identity", "The colored chat profile identity is invalid.");
            }
            catch (UnauthorizedAccessException)
            {
                return AuthenticationRequired();
            }
            catch
            {
                return Problem(HttpStatusCode.ServiceUnavailable, "colored_chat_profiles_unavailable", "The colored chat profile could not be deleted.");
            }
        }

        private async Task<HttpResponseMessage> SendMessage(
            SendChatMessageRequest? body, string? target, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid || body?.Message == null ||
                (body is SendPrivateChatMessageRequest && string.IsNullOrWhiteSpace(target))) return InvalidBody();
            if (runtimeStatus.GameReadiness != GameReadinessState.Ready)
                return Problem(HttpStatusCode.ServiceUnavailable, "game_not_ready", "The game is not ready to send chat messages.");
            try
            {
                var actor = RequireActor();
                var result = target == null
                    ? await sendGlobal.ExecuteAsync(actor, body.Message, cancellationToken).ConfigureAwait(false)
                    : await sendPrivate.ExecuteAsync(actor, target, body.Message, cancellationToken).ConfigureAwait(false);
                return ToSendResponse(result);
            }
            catch (ArgumentException)
            {
                return InvalidQuery("invalid_chat_message", "The chat message or target is invalid.");
            }
            catch (OperationCanceledException)
            {
                return Problem(HttpStatusCode.ServiceUnavailable, "chat_send_cancelled", "The chat send request was cancelled before execution.");
            }
            catch (UnauthorizedAccessException)
            {
                return AuthenticationRequired();
            }
            catch
            {
                return Problem(HttpStatusCode.ServiceUnavailable, "chat_send_failed", "The chat message could not be sent.");
            }
        }

        private HttpResponseMessage ToSendResponse(ChatSendResult result)
        {
            switch (result.Status)
            {
                case ChatSendStatus.Accepted:
                    return Request.CreateResponse(HttpStatusCode.Accepted, new ChatSendResponse(result.Status));
                case ChatSendStatus.Disabled:
                    return Problem(HttpStatusCode.ServiceUnavailable, "chat_disabled", "Chat sending is disabled.");
                case ChatSendStatus.NotReady:
                    return Problem(HttpStatusCode.ServiceUnavailable, "game_not_ready", "The game is not ready to send chat messages.");
                case ChatSendStatus.QueueFull:
                    return Problem(HttpStatusCode.ServiceUnavailable, "chat_send_queue_full", "The chat send queue is full.");
                case ChatSendStatus.TargetOffline:
                    return Problem(HttpStatusCode.Conflict, "chat_target_offline", "The private chat target is no longer online.");
                case ChatSendStatus.Cancelled:
                    return Problem(HttpStatusCode.ServiceUnavailable, "chat_send_cancelled", "The chat send request was cancelled before execution.");
                default:
                    return Problem(HttpStatusCode.ServiceUnavailable, "chat_send_result_unknown", "The chat send result could not be confirmed.");
            }
        }

        private ColoredChatProfile? FindProfile(string crossplatformId)
        {
            ColoredChatProfileKeyset? keyset = null;
            do
            {
                var page = getProfiles.Execute(new ColoredChatProfileQuery(
                    ColoredChatProfileQuery.MaximumPageSize, crossplatformId, null, null, null, null, null, keyset));
                var match = page.Profiles.FirstOrDefault(profile =>
                    string.Equals(profile.CrossplatformId, crossplatformId, StringComparison.Ordinal));
                if (match != null) return match;
                keyset = page.NextKeyset;
            } while (keyset != null);
            return null;
        }

        private static ColoredChatProfile ToProfile(
            string? crossplatformId, ColoredChatProfileWriteRequest body,
            DateTimeOffset createdAtUtc, DateTimeOffset updatedAtUtc) => new ColoredChatProfile
        {
            CrossplatformId = crossplatformId ?? string.Empty,
            CustomName = body.CustomName,
            NameColor = body.NameColor,
            TextColor = body.TextColor,
            Description = body.Description,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = updatedAtUtc
        };

        private HttpResponseMessage ExecuteRead<T>(Func<T> action, string code, string detail)
        {
            try { return Request.CreateResponse(HttpStatusCode.OK, action()); }
            catch { return Problem(HttpStatusCode.ServiceUnavailable, code, detail); }
        }

        private HttpResponseMessage ExecuteMutation<T>(
            Func<T> action, string invalidCode, string invalidDetail, string failureCode, string failureDetail)
        {
            try { return Request.CreateResponse(HttpStatusCode.OK, action()); }
            catch (ArgumentException) { return InvalidQuery(invalidCode, invalidDetail); }
            catch (UnauthorizedAccessException) { return AuthenticationRequired(); }
            catch { return Problem(HttpStatusCode.ServiceUnavailable, failureCode, failureDetail); }
        }

        private string RequireActor()
        {
            var identity = User?.Identity as ClaimsIdentity;
            var actor = identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(actor)) throw new UnauthorizedAccessException();
            return actor!;
        }

        private HttpResponseMessage InvalidBody() =>
            ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
        private HttpResponseMessage AuthenticationRequired() =>
            Problem(HttpStatusCode.Unauthorized, "authentication_required", "Authentication is required to manage game chat.");
        private HttpResponseMessage InvalidQuery(string code, string detail) =>
            Problem(HttpStatusCode.BadRequest, code, detail);
        private HttpResponseMessage Problem(HttpStatusCode status, string code, string detail) =>
            ApiProblemDetailsFactory.CreateResponse(Request, status, code, detail);

        private static bool TryEnum<T>(string? text, out T? value) where T : struct
        {
            value = null;
            if (string.IsNullOrWhiteSpace(text)) return true;
            if (!Enum.TryParse(text, false, out T parsed) || !Enum.IsDefined(typeof(T), parsed)) return false;
            value = parsed;
            return true;
        }

        private static bool TryUtc(string? text, out DateTimeOffset? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(text)) return true;
            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var parsed) || parsed.Offset != TimeSpan.Zero) return false;
            value = parsed;
            return true;
        }
    }
}
