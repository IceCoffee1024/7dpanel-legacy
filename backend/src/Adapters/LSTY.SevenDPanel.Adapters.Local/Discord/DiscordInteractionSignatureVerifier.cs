using System;
using System.Globalization;
using System.Text;
using LSTY.SevenDPanel.Application.Discord;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace LSTY.SevenDPanel.Adapters.Local.Discord
{
    public sealed class Ed25519SignatureVerifier
    {
        private const int PublicKeySize = 32;
        private const int SignatureSize = 64;
        private readonly Ed25519PublicKeyParameters publicKey;

        public Ed25519SignatureVerifier(string publicKeyHex)
        {
            if (!TryDecodeHex(publicKeyHex, PublicKeySize, out var publicKeyBytes))
                throw new ArgumentException(
                    "discord_interaction_public_key_invalid",
                    nameof(publicKeyHex));
            publicKey = new Ed25519PublicKeyParameters(publicKeyBytes);
        }

        public bool Verify(string? signatureHex, byte[] message)
        {
            if (message == null ||
                !TryDecodeHex(signatureHex, SignatureSize, out var signature))
                return false;

            try
            {
                var verifier = new Ed25519Signer();
                verifier.Init(false, publicKey);
                verifier.BlockUpdate(message, 0, message.Length);
                return verifier.VerifySignature(signature);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryDecodeHex(
            string? value,
            int expectedByteCount,
            out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            if (value == null || value.Length != expectedByteCount * 2) return false;

            var decoded = new byte[expectedByteCount];
            for (var index = 0; index < decoded.Length; index++)
            {
                var high = HexNibble(value[index * 2]);
                var low = HexNibble(value[index * 2 + 1]);
                if (high < 0 || low < 0) return false;
                decoded[index] = (byte)((high << 4) | low);
            }

            bytes = decoded;
            return true;
        }

        private static int HexNibble(char value)
        {
            if (value >= '0' && value <= '9') return value - '0';
            if (value >= 'a' && value <= 'f') return value - 'a' + 10;
            if (value >= 'A' && value <= 'F') return value - 'A' + 10;
            return -1;
        }
    }

    public sealed class DiscordInteractionSignatureVerifier :
        IDiscordInteractionSignatureVerifier
    {
        private readonly Ed25519SignatureVerifier verifier;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly TimeSpan maximumAge;

        public DiscordInteractionSignatureVerifier(
            string publicKeyHex,
            Func<DateTimeOffset> utcNow,
            TimeSpan maximumAge)
        {
            verifier = new Ed25519SignatureVerifier(publicKeyHex);
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            if (maximumAge <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(maximumAge));
            this.maximumAge = maximumAge;
        }

        public bool Verify(string? signatureHex, string? timestamp, byte[] rawBody)
        {
            if (rawBody == null || !TryParseTimestamp(timestamp, out var signedAtUtc))
                return false;

            DateTimeOffset observedAtUtc;
            try
            {
                observedAtUtc = utcNow().ToUniversalTime();
            }
            catch
            {
                return false;
            }

            var age = observedAtUtc - signedAtUtc;
            if (age > maximumAge || age < -maximumAge) return false;

            var timestampBytes = Encoding.ASCII.GetBytes(timestamp!);
            var signedMessage = new byte[timestampBytes.Length + rawBody.Length];
            Buffer.BlockCopy(timestampBytes, 0, signedMessage, 0, timestampBytes.Length);
            Buffer.BlockCopy(rawBody, 0, signedMessage, timestampBytes.Length, rawBody.Length);
            return verifier.Verify(signatureHex, signedMessage);
        }

        private static bool TryParseTimestamp(
            string? value,
            out DateTimeOffset signedAtUtc)
        {
            signedAtUtc = default;
            if (value == null || value.Length == 0 || value.Length > 20) return false;
            for (var index = 0; index < value.Length; index++)
            {
                if (value[index] < '0' || value[index] > '9') return false;
            }

            if (!long.TryParse(
                    value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var unixSeconds))
                return false;
            try
            {
                signedAtUtc = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }
    }
}
