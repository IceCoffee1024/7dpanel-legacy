using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using LSTY.SevenDPanel.Application.ServerConfiguration;

namespace LSTY.SevenDPanel.ServerConfiguration
{
    public sealed class LocalServerConfigurationStore : IServerConfigurationStore
    {
        private readonly string path;
        private readonly object sync = new object();

        public LocalServerConfigurationStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A server configuration path is required.", nameof(path));
            this.path = Path.GetFullPath(path);
        }

        public ServerConfigurationSnapshot Read(ServerConfigurationFieldCatalog catalog)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            lock (sync)
            {
                var bytes = File.ReadAllBytes(path);
                return ReadSnapshot(bytes, catalog);
            }
        }

        public ServerConfigurationUpdateResult Update(
            UpdateServerConfigurationRequest request,
            ServerConfigurationFieldCatalog catalog)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            lock (sync)
            {
                var original = File.ReadAllBytes(path);
                var currentVersion = ComputeVersion(original);
                if (!string.Equals(currentVersion, request.Version, StringComparison.Ordinal))
                    return Result(ServerConfigurationUpdateStatus.Conflict, currentVersion, false);

                var document = Load(original);
                var property = document.SelectSingleNode(
                    "/ServerSettings/property[@name=" + QuoteXPath(request.Key) + "]") as XmlElement;
                if (property == null)
                    return Result(ServerConfigurationUpdateStatus.UnknownField, currentVersion, false);

                if (!catalog.TryGet(request.Key, out var definition))
                    definition = catalog.DescribeUnknown(request.Key);
                if (!definition.Editable)
                    return Result(ServerConfigurationUpdateStatus.ReadOnly, currentVersion, definition.RestartRequired);
                if (!TryNormalize(request.Value, definition, out var normalized))
                    return Result(ServerConfigurationUpdateStatus.InvalidValue, currentVersion, definition.RestartRequired);

                property.SetAttribute("value", normalized);
                var directory = Path.GetDirectoryName(path)!;
                var temporaryPath = Path.Combine(directory, Path.GetFileName(path) + ".7dpanel.tmp");
                var backupPath = Path.Combine(directory, Path.GetFileName(path) + ".7dpanel.bak");

                try
                {
                    Save(document, temporaryPath);
                    if (!string.Equals(ComputeVersion(File.ReadAllBytes(path)), currentVersion, StringComparison.Ordinal))
                        return Result(ServerConfigurationUpdateStatus.Conflict, ComputeVersion(File.ReadAllBytes(path)), definition.RestartRequired);

                    Replace(temporaryPath, backupPath);
                    var savedAt = DateTimeOffset.UtcNow;
                    var version = ComputeVersion(File.ReadAllBytes(path));
                    return new ServerConfigurationUpdateResult(
                        ServerConfigurationUpdateStatus.Updated, version, savedAt, definition.RestartRequired);
                }
                catch (IOException)
                {
                    return Result(ServerConfigurationUpdateStatus.WriteFailed, currentVersion, definition.RestartRequired);
                }
                catch (UnauthorizedAccessException)
                {
                    return Result(ServerConfigurationUpdateStatus.WriteFailed, currentVersion, definition.RestartRequired);
                }
                finally
                {
                    TryDelete(temporaryPath);
                    TryDelete(backupPath);
                }
            }
        }

        private ServerConfigurationSnapshot ReadSnapshot(byte[] bytes, ServerConfigurationFieldCatalog catalog)
        {
            var document = Load(bytes);
            var fields = new List<ServerConfigurationField>();
            var nodes = document.SelectNodes("/ServerSettings/property[@name][@value]");
            if (nodes != null)
            {
                foreach (XmlElement property in nodes)
                {
                    var key = property.GetAttribute("name");
                    if (!catalog.TryGet(key, out var definition))
                        definition = catalog.DescribeUnknown(key);
                    fields.Add(new ServerConfigurationField(
                        key,
                        definition.Sensitive ? string.Empty : property.GetAttribute("value"),
                        definition.Group,
                        definition.ValueType,
                        definition.Editable,
                        definition.Advanced,
                        definition.Sensitive,
                        !string.IsNullOrEmpty(property.GetAttribute("value")),
                        definition.RestartRequired,
                        definition.AllowedValues,
                        definition.Minimum,
                        definition.Maximum));
                }
            }
            return new ServerConfigurationSnapshot(ComputeVersion(bytes), DateTimeOffset.UtcNow, fields);
        }

        private static XmlDocument Load(byte[] bytes)
        {
            var document = new XmlDocument { PreserveWhitespace = true };
            using var stream = new MemoryStream(bytes, false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            });
            document.Load(reader);
            return document;
        }

        private static void Save(XmlDocument document, string destination)
        {
            using var stream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            document.Save(stream);
            stream.Flush(true);
        }

        private void Replace(string temporaryPath, string backupPath)
        {
            try
            {
                File.Replace(temporaryPath, path, backupPath, true);
            }
            catch (PlatformNotSupportedException)
            {
                File.Copy(path, backupPath, true);
                try
                {
                    File.Delete(path);
                    File.Move(temporaryPath, path);
                }
                catch
                {
                    if (File.Exists(backupPath))
                        File.Copy(backupPath, path, true);
                    throw;
                }
            }
        }

        private static bool TryNormalize(string value, ServerConfigurationFieldDefinition definition, out string normalized)
        {
            normalized = value ?? string.Empty;
            if (string.Equals(definition.Key, "AdminFileName", StringComparison.Ordinal)
                && (Path.IsPathRooted(normalized)
                    || !string.Equals(Path.GetFileName(normalized), normalized, StringComparison.Ordinal)
                    || normalized.IndexOfAny(new[] { '/', '\\' }) >= 0))
            {
                return false;
            }
            if (definition.ValueType == ServerConfigurationValueType.Integer)
            {
                if (!long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                    return false;
                if ((definition.Minimum.HasValue && number < definition.Minimum.Value)
                    || (definition.Maximum.HasValue && number > definition.Maximum.Value))
                    return false;
                normalized = number.ToString(CultureInfo.InvariantCulture);
            }
            else if (definition.ValueType == ServerConfigurationValueType.Boolean)
            {
                if (!bool.TryParse(normalized, out var flag))
                    return false;
                normalized = flag ? "true" : "false";
            }
            else if (definition.AllowedValues.Count > 0
                && !definition.AllowedValues.Contains(normalized, StringComparer.Ordinal))
            {
                return false;
            }
            return true;
        }

        private static string ComputeVersion(byte[] bytes)
        {
            using var sha256 = SHA256.Create();
            return string.Concat(sha256.ComputeHash(bytes).Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
        }

        private static ServerConfigurationUpdateResult Result(ServerConfigurationUpdateStatus status, string version, bool restartRequired)
        {
            return new ServerConfigurationUpdateResult(status, version ?? string.Empty, null, restartRequired);
        }

        private static string QuoteXPath(string value)
        {
            if (value.IndexOf('\'') < 0)
                return "'" + value + "'";
            if (value.IndexOf('"') < 0)
                return "\"" + value + "\"";
            return "''";
        }

        private static void TryDelete(string target)
        {
            try
            {
                if (File.Exists(target))
                    File.Delete(target);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
