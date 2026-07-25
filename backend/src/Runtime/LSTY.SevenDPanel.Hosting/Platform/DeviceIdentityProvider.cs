using System;
using System.Security.Cryptography;
using System.Text;

namespace LSTY.SevenDPanel.Hosting.Platform
{
    public sealed class DeviceIdentityProvider
    {
        private readonly string productNamespace;

        public DeviceIdentityProvider(string productNamespace)
        {
            if (string.IsNullOrWhiteSpace(productNamespace)) throw new ArgumentException("A product namespace is required.", nameof(productNamespace));
            this.productNamespace = productNamespace;
        }

        public string? CreateDeviceId(HostPlatformInfo platformInfo)
        {
            if (platformInfo == null) throw new ArgumentNullException(nameof(platformInfo));
            if (string.IsNullOrWhiteSpace(platformInfo.MachineIdentity)) return null;
            using (var sha256 = SHA256.Create())
            {
                var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(productNamespace + ":" + platformInfo.MachineIdentity));
                var builder = new StringBuilder("7dp_device_");
                foreach (var value in bytes) builder.Append(value.ToString("x2"));
                return builder.ToString();
            }
        }
    }
}
