using System;
using System.Text;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Authentication
{
    internal sealed class PanelCredentialVerifier
    {
        private readonly PanelAuthenticationOptions options;

        public PanelCredentialVerifier(PanelAuthenticationOptions options)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public bool Verify(string? username, string? password)
        {
            if (!options.Enabled) return false;
            return FixedTimeEquals(options.Username, username ?? string.Empty) &
                FixedTimeEquals(options.Password, password ?? string.Empty);
        }

        private static bool FixedTimeEquals(string expected, string supplied)
        {
            var expectedBytes = Encoding.UTF8.GetBytes(expected);
            var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
            try
            {
                var difference = expectedBytes.Length ^ suppliedBytes.Length;
                var length = Math.Max(expectedBytes.Length, suppliedBytes.Length);
                for (var index = 0; index < length; index++)
                {
                    var expectedByte = index < expectedBytes.Length ? expectedBytes[index] : 0;
                    var suppliedByte = index < suppliedBytes.Length ? suppliedBytes[index] : 0;
                    difference |= expectedByte ^ suppliedByte;
                }

                return difference == 0;
            }
            finally
            {
                Array.Clear(expectedBytes, 0, expectedBytes.Length);
                Array.Clear(suppliedBytes, 0, suppliedBytes.Length);
            }
        }
    }
}
