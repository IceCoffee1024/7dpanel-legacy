using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using LSTY.SevenDPanel.Adapters.Local.Files;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Domain.Backups;
using LSTY.SevenDPanel.Domain.Jobs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LSTY.SevenDPanel.Adapters.Local.Restore
{
    public sealed class JsonPendingRestoreStore : IPendingRestoreMarkerStore
    {
        public const string StateDirectoryName = "restore-state";
        public const string MarkerFileName = "pending-restore.v1.json";
        public const string ReceiptFileName = "restore-result.v1.json";
        public const string MarkerAlreadyExistsError = "pending_restore_already_exists";
        public const string MarkerInvalidError = "pending_restore_marker_invalid";
        public const string ReceiptInvalidError = "restore_result_receipt_invalid";
        public const string ReceiptConflictError = "restore_result_receipt_conflict";

        private readonly ApprovedStorageRoots roots;
        private readonly string stateDirectory;
        private readonly string markerPath;
        private readonly string receiptPath;
        private readonly object gate = new object();

        public JsonPendingRestoreStore(ApprovedStorageRoots roots)
        {
            this.roots = roots ?? throw new ArgumentNullException(nameof(roots));
            stateDirectory = Path.Combine(roots.PanelStateRoot, StateDirectoryName);
            markerPath = Path.Combine(stateDirectory, MarkerFileName);
            receiptPath = Path.Combine(stateDirectory, ReceiptFileName);
            roots.ValidatePanelStatePath(stateDirectory);
            roots.ValidatePanelStatePath(markerPath);
            roots.ValidatePanelStatePath(receiptPath);
        }

        public void CreateMarker(PendingRestoreMarker marker)
        {
            if (marker == null) throw new ArgumentNullException(nameof(marker));
            try
            {
                marker.Validate();
            }
            catch (Exception exception) when (exception is FormatException || exception is ArgumentException)
            {
                throw new RestoreStateException(MarkerInvalidError, exception);
            }

            lock (gate)
            {
                EnsureStateDirectory();
                if (File.Exists(markerPath))
                    throw new RestoreStateException(MarkerAlreadyExistsError);
                AtomicWrite(markerPath, RestoreJsonCodec.Serialize(marker));
            }
        }

        public bool TryCreateMarker(
            BackupArtifact artifact,
            LSTY.SevenDPanel.Application.Jobs.JobRecord pendingRestartJob)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            if (pendingRestartJob == null) throw new ArgumentNullException(nameof(pendingRestartJob));
            try
            {
                CreateMarker(new PendingRestoreMarker(
                    PendingRestoreMarker.CurrentVersion,
                    artifact.Id,
                    artifact.Kind,
                    artifact.BackupRootId,
                    artifact.RelativeResourceId,
                    artifact.Sha256,
                    new RestoreJobSnapshot(
                        pendingRestartJob.Id,
                        pendingRestartJob.Kind,
                        pendingRestartJob.Status,
                        pendingRestartJob.ActorSubject,
                        pendingRestartJob.IdempotencyKey,
                        pendingRestartJob.CorrelationId,
                        pendingRestartJob.CreatedAtUtc),
                    RestoreExecutionStage.Prepared));
                return true;
            }
            catch (RestoreStateException exception)
                when (exception.ErrorCode == MarkerAlreadyExistsError)
            {
                return false;
            }
        }

        public PendingRestoreMarker? ReadMarker()
        {
            lock (gate)
            {
                if (!File.Exists(markerPath)) return null;
                var json = File.ReadAllText(markerPath);
                try
                {
                    var marker = RestoreJsonCodec.ParseMarker(json);
                    marker.Validate();
                    return marker;
                }
                catch (Exception exception) when (!(exception is RestoreStateException))
                {
                    throw new RestoreStateException(MarkerInvalidError, exception);
                }
            }
        }

        public void DeleteMarker(Guid jobId)
        {
            lock (gate)
            {
                var marker = ReadMarker();
                if (marker == null) return;
                if (marker.JobSnapshot.JobId != jobId)
                    throw new RestoreStateException(MarkerAlreadyExistsError);
                File.Delete(markerPath);
            }
        }

        public void WriteReceipt(RestoreResultReceipt receipt)
        {
            if (receipt == null) throw new ArgumentNullException(nameof(receipt));
            try
            {
                receipt.Validate();
            }
            catch (Exception exception) when (exception is FormatException || exception is ArgumentException)
            {
                throw new RestoreStateException(ReceiptInvalidError, exception);
            }

            lock (gate)
            {
                EnsureStateDirectory();
                var existing = ReadReceipt();
                if (existing != null)
                {
                    if (!existing.HasSameIdentity(receipt) ||
                        (existing.Stage != receipt.Stage &&
                         existing.Stage != RestoreExecutionStage.Prepared))
                    {
                        throw new RestoreStateException(ReceiptConflictError);
                    }
                    if (existing.Stage == receipt.Stage) return;
                }
                AtomicWrite(receiptPath, RestoreJsonCodec.Serialize(receipt));
            }
        }

        public RestoreResultReceipt? ReadReceipt()
        {
            lock (gate)
            {
                if (!File.Exists(receiptPath)) return null;
                var json = File.ReadAllText(receiptPath);
                try
                {
                    var receipt = RestoreJsonCodec.ParseReceipt(json);
                    receipt.Validate();
                    return receipt;
                }
                catch (Exception exception) when (!(exception is RestoreStateException))
                {
                    throw new RestoreStateException(ReceiptInvalidError, exception);
                }
            }
        }

        public void DeleteReceipt(Guid jobId)
        {
            lock (gate)
            {
                var receipt = ReadReceipt();
                if (receipt == null) return;
                if (receipt.JobSnapshot.JobId != jobId)
                    throw new RestoreStateException(ReceiptConflictError);
                File.Delete(receiptPath);
            }
        }

        private void EnsureStateDirectory()
        {
            roots.ValidatePanelStatePath(stateDirectory);
            Directory.CreateDirectory(stateDirectory);
            roots.ValidatePanelStatePath(stateDirectory);
        }

        private void AtomicWrite(string destination, string content)
        {
            roots.ValidatePanelStatePath(destination);
            var temporaryPath = Path.Combine(
                stateDirectory,
                "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            roots.ValidatePanelStatePath(temporaryPath);
            try
            {
                File.WriteAllText(temporaryPath, content);
                if (File.Exists(destination))
                    File.Replace(temporaryPath, destination, null);
                else
                    File.Move(temporaryPath, destination);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
    }

    internal static class RestoreJsonCodec
    {
        private static readonly string[] StateProperties =
        {
            "version", "artifactId", "backupKind", "backupRootId",
            "relativeResourceId", "sha256", "jobSnapshot", "stage"
        };

        private static readonly string[] SnapshotProperties =
        {
            "jobId", "jobKind", "jobStatus", "actorSubject",
            "idempotencyKey", "correlationId", "createdAtUtc"
        };

        internal static string Serialize(PendingRestoreMarker marker) => SerializeState(
            marker.Version,
            marker.ArtifactId,
            marker.BackupKind,
            marker.BackupRootId,
            marker.RelativeResourceId,
            marker.Sha256,
            marker.JobSnapshot,
            marker.Stage);

        internal static string Serialize(RestoreResultReceipt receipt) => SerializeState(
            receipt.Version,
            receipt.ArtifactId,
            receipt.BackupKind,
            receipt.BackupRootId,
            receipt.RelativeResourceId,
            receipt.Sha256,
            receipt.JobSnapshot,
            receipt.Stage);

        internal static PendingRestoreMarker ParseMarker(string json)
        {
            var value = ParseState(json);
            return new PendingRestoreMarker(
                value.Version,
                value.ArtifactId,
                value.BackupKind,
                value.BackupRootId,
                value.RelativeResourceId,
                value.Sha256,
                value.JobSnapshot,
                value.Stage);
        }

        internal static RestoreResultReceipt ParseReceipt(string json)
        {
            var value = ParseState(json);
            return new RestoreResultReceipt(
                value.Version,
                value.ArtifactId,
                value.BackupKind,
                value.BackupRootId,
                value.RelativeResourceId,
                value.Sha256,
                value.JobSnapshot,
                value.Stage);
        }

        internal static Dictionary<string, object?> ParseObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new FormatException("json_invalid");
            if (json.Length > 2 * 1024 * 1024) throw new FormatException("json_invalid");
            using var reader = new JsonTextReader(new StringReader(json))
            {
                MaxDepth = 32,
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Decimal
            };
            var value = JToken.ReadFrom(reader);
            if (value is not JObject dictionary)
                throw new FormatException("json_object_required");
            return dictionary.Properties().ToDictionary(
                pair => pair.Name,
                pair => ToPlainValue(pair.Value),
                StringComparer.Ordinal);
        }

        internal static void RequireProperties(
            IReadOnlyDictionary<string, object?> value,
            params string[] expected)
        {
            if (value.Count != expected.Length || expected.Any(name => !value.ContainsKey(name)))
                throw new FormatException("json_properties_invalid");
        }

        internal static string ReadString(IReadOnlyDictionary<string, object?> value, string name)
        {
            if (!value.TryGetValue(name, out var item) || item is not string text)
                throw new FormatException("json_string_invalid");
            return text;
        }

        internal static string? ReadNullableString(IReadOnlyDictionary<string, object?> value, string name)
        {
            if (!value.TryGetValue(name, out var item)) throw new FormatException("json_property_missing");
            if (item == null) return null;
            if (item is not string text) throw new FormatException("json_string_invalid");
            return text;
        }

        internal static int ReadInt32(IReadOnlyDictionary<string, object?> value, string name)
        {
            if (!value.TryGetValue(name, out var item) || item is not int number)
                throw new FormatException("json_integer_invalid");
            return number;
        }

        internal static long ReadInt64(IReadOnlyDictionary<string, object?> value, string name)
        {
            if (!value.TryGetValue(name, out var item)) throw new FormatException("json_integer_invalid");
            return item switch
            {
                int intValue => intValue,
                long longValue => longValue,
                decimal decimalValue when decimal.Truncate(decimalValue) == decimalValue => checked((long)decimalValue),
                _ => throw new FormatException("json_integer_invalid")
            };
        }

        internal static object[] ReadArray(IReadOnlyDictionary<string, object?> value, string name)
        {
            if (!value.TryGetValue(name, out var item)) throw new FormatException("json_array_invalid");
            if (item is object[] array) return array;
            if (item is ArrayList list) return list.Cast<object>().ToArray();
            throw new FormatException("json_array_invalid");
        }

        internal static Dictionary<string, object?> RequireObject(object value)
        {
            if (value is not Dictionary<string, object> dictionary)
                throw new FormatException("json_object_required");
            return dictionary.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal);
        }

        internal static Guid ParseGuid(string value)
        {
            if (!Guid.TryParseExact(value, "D", out var result) || result == Guid.Empty)
                throw new FormatException("json_guid_invalid");
            return result;
        }

        internal static DateTimeOffset ParseUtc(string value)
        {
            if (!DateTimeOffset.TryParseExact(
                    value,
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var result) ||
                result.Offset != TimeSpan.Zero)
            {
                throw new FormatException("json_utc_invalid");
            }
            return result;
        }

        private static ParsedState ParseState(string json)
        {
            var root = ParseObject(json);
            RequireProperties(root, StateProperties);
            var snapshotValue = root["jobSnapshot"] ?? throw new FormatException("job_snapshot_invalid");
            var snapshotObject = RequireObject(snapshotValue);
            RequireProperties(snapshotObject, SnapshotProperties);
            var snapshot = new RestoreJobSnapshot(
                ParseGuid(ReadString(snapshotObject, "jobId")),
                ParseEnum<JobKind>(ReadString(snapshotObject, "jobKind")),
                ParseEnum<JobStatus>(ReadString(snapshotObject, "jobStatus")),
                ReadNullableString(snapshotObject, "actorSubject"),
                ReadString(snapshotObject, "idempotencyKey"),
                ReadNullableString(snapshotObject, "correlationId"),
                ParseUtc(ReadString(snapshotObject, "createdAtUtc")));
            return new ParsedState(
                ReadInt32(root, "version"),
                ParseGuid(ReadString(root, "artifactId")),
                ParseEnum<BackupKind>(ReadString(root, "backupKind")),
                ReadString(root, "backupRootId"),
                ReadString(root, "relativeResourceId"),
                ReadString(root, "sha256"),
                snapshot,
                ParseEnum<RestoreExecutionStage>(ReadString(root, "stage")));
        }

        private static string SerializeState(
            int version,
            Guid artifactId,
            BackupKind backupKind,
            string backupRootId,
            string relativeResourceId,
            string sha256,
            RestoreJobSnapshot snapshot,
            RestoreExecutionStage stage)
        {
            var value = new Dictionary<string, object?>
            {
                ["version"] = version,
                ["artifactId"] = artifactId.ToString("D"),
                ["backupKind"] = backupKind.ToString(),
                ["backupRootId"] = backupRootId,
                ["relativeResourceId"] = relativeResourceId,
                ["sha256"] = sha256.ToLowerInvariant(),
                ["jobSnapshot"] = new Dictionary<string, object?>
                {
                    ["jobId"] = snapshot.JobId.ToString("D"),
                    ["jobKind"] = snapshot.JobKind.ToString(),
                    ["jobStatus"] = snapshot.JobStatus.ToString(),
                    ["actorSubject"] = snapshot.ActorSubject,
                    ["idempotencyKey"] = snapshot.IdempotencyKey,
                    ["correlationId"] = snapshot.CorrelationId,
                    ["createdAtUtc"] = snapshot.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)
                },
                ["stage"] = stage.ToString()
            };
            return JsonConvert.SerializeObject(value);
        }

        private static T ParseEnum<T>(string value) where T : struct
        {
            if (!Enum.TryParse(value, ignoreCase: false, out T result) ||
                !Enum.IsDefined(typeof(T), result))
            {
                throw new FormatException("json_enum_invalid");
            }
            return result;
        }

        private static object? ToPlainValue(JToken value) => value.Type switch
        {
            JTokenType.Object => ((JObject)value).Properties().ToDictionary(
                pair => pair.Name,
                pair => ToPlainValue(pair.Value),
                StringComparer.Ordinal),
            JTokenType.Array => ((JArray)value).Select(ToPlainValue).ToArray(),
            JTokenType.Integer => ToInteger(Convert.ToInt64(
                ((JValue)value).Value,
                CultureInfo.InvariantCulture)),
            JTokenType.Float => Convert.ToDecimal(
                ((JValue)value).Value,
                CultureInfo.InvariantCulture),
            JTokenType.String => (string?)((JValue)value).Value,
            JTokenType.Boolean => (bool)((JValue)value).Value!,
            JTokenType.Null => null,
            _ when value is JValue scalar => scalar.Value,
            _ => throw new FormatException("json_value_invalid")
        };

        private static object ToInteger(long value) =>
            value >= int.MinValue && value <= int.MaxValue ? (object)(int)value : value;

        private sealed record ParsedState(
            int Version,
            Guid ArtifactId,
            BackupKind BackupKind,
            string BackupRootId,
            string RelativeResourceId,
            string Sha256,
            RestoreJobSnapshot JobSnapshot,
            RestoreExecutionStage Stage);
    }
}
