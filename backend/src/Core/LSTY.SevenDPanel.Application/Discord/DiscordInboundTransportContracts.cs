using System;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application.Discord
{
    public interface IDiscordInteractionSignatureVerifier
    {
        bool Verify(string? signatureHex, string? timestamp, byte[] rawBody);
    }

    public interface IDiscordInboundTransportSink
    {
        Task<DiscordInboundResult> HandleMessageAsync(
            DiscordMessageCreateEnvelope message,
            CancellationToken cancellationToken);

        Task<DiscordInboundResult> HandleInteractionAsync(
            DiscordInteractionEnvelope interaction,
            CancellationToken cancellationToken);
    }

    public interface IDiscordDeferredInteractionSink
    {
        DiscordInboundResult AcceptInteraction(
            DiscordInteractionEnvelope interaction,
            string interactionToken);
    }

    public sealed class DiscordInteractionResponse
    {
        public DiscordInteractionResponse(
            string applicationId,
            string interactionToken,
            string content,
            DiscordProxyConfiguration? proxy)
        {
            ApplicationId = applicationId ?? throw new ArgumentNullException(nameof(applicationId));
            InteractionToken = interactionToken ?? throw new ArgumentNullException(nameof(interactionToken));
            Content = content ?? throw new ArgumentNullException(nameof(content));
            Proxy = proxy;
        }

        public string ApplicationId { get; }
        public string InteractionToken { get; }
        public string Content { get; }
        public DiscordProxyConfiguration? Proxy { get; }

        public override string ToString() =>
            $"DiscordInteractionResponse {{ ApplicationId = {ApplicationId}, InteractionToken = [REDACTED], ContentLength = {Content.Length}, Proxy = {Proxy} }}";
    }

    public enum DiscordInteractionResponseDisposition
    {
        Succeeded,
        Retryable,
        Rejected,
        ResultUnknown
    }

    public interface IDiscordInteractionResponseSender
    {
        Task<DiscordInteractionResponseDisposition> SendEphemeralAsync(
            DiscordInteractionResponse response,
            CancellationToken cancellationToken);
    }
}
