using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Mods
{
    internal static class PlayerStoreXuiPatch
    {
        internal const string RelativePath = "Config/XUi_InGame/windows.xml";

        internal static IEnumerator WriteWhenServerIpAvailable(
            string modDirectory,
            PanelHostOptions options,
            Func<string?> getServerIp,
            Action<string> log,
            Func<DateTime>? utcNow = null,
            TimeSpan? timeout = null)
        {
            if (getServerIp == null) throw new ArgumentNullException(nameof(getServerIp));
            var clock = utcNow ?? (() => DateTime.UtcNow);
            var deadline = clock().Add(timeout ?? TimeSpan.FromSeconds(30));
            string? serverIp;

            do
            {
                serverIp = options.PlayerStoreServerIp ?? getServerIp();
                if (TryCreateStoreUri(options.Url, serverIp, out _)) break;
                if (clock() >= deadline) break;
                yield return null;
            }
            while (true);

            try
            {
                Write(modDirectory, options, serverIp, log);
            }
            catch (Exception ex)
            {
                log("Player store XUi link could not be configured: " + ex.Message);
            }
        }

        internal static void Write(
            string modDirectory,
            PanelHostOptions options,
            string? serverIp,
            Action<string> log)
        {
            if (string.IsNullOrWhiteSpace(modDirectory))
                throw new ArgumentException("The mod directory is required.", nameof(modDirectory));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (log == null) throw new ArgumentNullException(nameof(log));

            var patchPath = Path.Combine(
                modDirectory,
                RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!TryCreateStoreUri(options.Url, serverIp, out var storeUri))
            {
                if (File.Exists(patchPath)) File.Delete(patchPath);
                log("Player store XUi link is disabled because ServerIP is empty, wildcard, loopback, or invalid.");
                return;
            }

            var document = CreateDocument(storeUri!);
            var directory = Path.GetDirectoryName(patchPath)!;
            Directory.CreateDirectory(directory);
            using (var writer = XmlWriter.Create(patchPath, new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                OmitXmlDeclaration = false
            }))
            {
                document.Save(writer);
            }

            log("Player store XUi link configured: " + storeUri);
        }

        internal static bool TryCreateStoreUri(
            string listenerUrl,
            string? serverIp,
            out Uri? storeUri)
        {
            storeUri = null;
            var normalizedIp = (serverIp ?? string.Empty).Trim().Trim('[', ']');
            if (!IPAddress.TryParse(normalizedIp, out var address) ||
                IPAddress.Any.Equals(address) ||
                IPAddress.IPv6Any.Equals(address) ||
                IPAddress.IsLoopback(address))
            {
                return false;
            }

            var parseableListenerUrl = listenerUrl.Replace(
                "://*:",
                "://127.0.0.1:");
            if (!Uri.TryCreate(parseableListenerUrl, UriKind.Absolute, out var listener))
                return false;

            var builder = new UriBuilder(
                listener.Scheme,
                normalizedIp,
                listener.Port,
                "/player/store");
            storeUri = builder.Uri;
            return true;
        }

        internal static XDocument CreateDocument(Uri storeUri)
        {
            if (storeUri == null) throw new ArgumentNullException(nameof(storeUri));

            return new XDocument(
                new XElement("configs",
                    new XElement("set",
                        new XAttribute("xpath", "/windows/window[@name='chat']/@height"),
                        "314"),
                    new XElement("append",
                        new XAttribute("xpath", "/windows/window[@name='chat']"),
                        new XElement("label",
                            new XAttribute("name", "playerStoreLink"),
                            new XAttribute("pos", "6,-286"),
                            new XAttribute("width", "488"),
                            new XAttribute("height", "24"),
                            new XAttribute("depth", "3"),
                            new XAttribute("font_size", "22"),
                            new XAttribute("justify", "left"),
                            new XAttribute("support_urls", "true"),
                            new XAttribute(
                                "text",
                                "[url=" + storeUri.AbsoluteUri + "][F0BD57]游戏商店[-][/url]")))));
        }
    }
}
