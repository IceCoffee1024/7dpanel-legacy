using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application.WorldOperations
{
    public sealed class UndoWorldChangeSetRequest
    {
        public UndoWorldChangeSetRequest(
            string actorSubject,
            string sourceOperationId,
            string changeSetId,
            string worldId,
            string worldVersion,
            string currentRegionHash,
            string correlationId,
            bool confirmed,
            bool strongConfirmed,
            DateTimeOffset requestedAtUtc)
        {
            ActorSubject = MapWorldOperationValidation.RequireText(actorSubject, nameof(actorSubject));
            SourceOperationId = MapWorldOperationValidation.RequireText(sourceOperationId, nameof(sourceOperationId));
            ChangeSetId = MapWorldOperationValidation.RequireText(changeSetId, nameof(changeSetId));
            WorldId = MapWorldOperationValidation.RequireText(worldId, nameof(worldId));
            WorldVersion = MapWorldOperationValidation.RequireText(worldVersion, nameof(worldVersion));
            CurrentRegionHash = WorldChangeSetValidation.RequireHash(
                currentRegionHash,
                nameof(currentRegionHash));
            CorrelationId = MapWorldOperationValidation.RequireText(correlationId, nameof(correlationId));
            Confirmed = confirmed;
            StrongConfirmed = strongConfirmed;
            MapWorldOperationValidation.RequireUtc(requestedAtUtc, nameof(requestedAtUtc));
            RequestedAtUtc = requestedAtUtc;
        }

        public string ActorSubject { get; }
        public string SourceOperationId { get; }
        public string ChangeSetId { get; }
        public string WorldId { get; }
        public string WorldVersion { get; }
        public string CurrentRegionHash { get; }
        public string CorrelationId { get; }
        public bool Confirmed { get; }
        public bool StrongConfirmed { get; }
        public DateTimeOffset RequestedAtUtc { get; }
    }

    public sealed class UndoWorldChangeSetUseCase
    {
        public const string ChangeSetInvalid = "undo_change_set_invalid";
        public const string SourceOperationMismatch = "undo_source_operation_mismatch";
        public const string WorldMismatch = "undo_world_mismatch";
        public const string WorldVersionMismatch = "undo_world_version_mismatch";
        public const string ChangeSetExpired = "undo_change_set_expired";
        public const string CurrentRegionChanged = "undo_current_region_changed";
        public const string ChangeSetCorrupt = "undo_change_set_corrupt";
        public const string AlreadyUndone = "undo_change_set_already_undone";

        private readonly IWorldOperationJobBridge bridge;
        private readonly IWorldChangeSetMetadataStore changeSets;
        private readonly IWorldChangeSetBlobStore blobs;
        private readonly IWorldChangeSetPreflightGateway preflightGateway;

        public UndoWorldChangeSetUseCase(
            IWorldOperationJobBridge bridge,
            IWorldChangeSetMetadataStore changeSets,
            IWorldChangeSetBlobStore blobs,
            IWorldChangeSetPreflightGateway preflightGateway)
        {
            this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            this.changeSets = changeSets ?? throw new ArgumentNullException(nameof(changeSets));
            this.blobs = blobs ?? throw new ArgumentNullException(nameof(blobs));
            this.preflightGateway = preflightGateway ??
                throw new ArgumentNullException(nameof(preflightGateway));
        }

        public async Task<UndoWorldChangeSetPreflight> PreflightAsync(
            string sourceOperationId,
            DateTimeOffset requestedAtUtc,
            CancellationToken cancellationToken)
        {
            sourceOperationId = MapWorldOperationValidation.RequireText(
                sourceOperationId,
                nameof(sourceOperationId));
            MapWorldOperationValidation.RequireUtc(requestedAtUtc, nameof(requestedAtUtc));

            WorldOperationRecord source;
            try { source = bridge.Get(sourceOperationId); }
            catch { throw Conflict(SourceOperationMismatch); }
            if (source == null || source.Kind == WorldOperationKind.UndoChangeSet ||
                !source.IsReversible || source.Status != WorldOperationStatus.Succeeded ||
                string.IsNullOrWhiteSpace(source.ChangeSetId))
            {
                throw Conflict(SourceOperationMismatch);
            }

            var descriptor = ReadDescriptor(source.ChangeSetId!);
            if (!ValidDescriptor(descriptor) || descriptor == null)
                throw Conflict(ChangeSetInvalid);
            ValidateSourceOperation(source, descriptor);
            if (descriptor.ExpiresAtUtc <= requestedAtUtc)
                throw Conflict(ChangeSetExpired);
            if (string.Equals(descriptor.BeforeHash, descriptor.AfterHash, StringComparison.Ordinal))
                throw Conflict(AlreadyUndone);
            ValidateBlob(descriptor);
            RejectExistingUndo(descriptor.ChangeSetId);

            WorldChangeSetRuntimeHashResult runtime;
            try
            {
                runtime = await preflightGateway
                    .ReadCurrentRegionHashAsync(descriptor, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                runtime = WorldChangeSetRuntimeHashResult.Unavailable(
                    "undo_preflight_runtime_unavailable");
            }

            if (runtime == null || string.IsNullOrWhiteSpace(runtime.CurrentRegionHash))
            {
                return new UndoWorldChangeSetPreflight(
                    descriptor.SourceOperationId,
                    descriptor.ChangeSetId,
                    descriptor.WorldId,
                    descriptor.WorldVersion,
                    descriptor.AfterHash,
                    null,
                    null,
                    runtime?.ErrorCode ?? "undo_preflight_runtime_unavailable");
            }

            var matches = string.Equals(
                descriptor.AfterHash,
                runtime.CurrentRegionHash,
                StringComparison.Ordinal);
            return new UndoWorldChangeSetPreflight(
                descriptor.SourceOperationId,
                descriptor.ChangeSetId,
                descriptor.WorldId,
                descriptor.WorldVersion,
                descriptor.AfterHash,
                runtime.CurrentRegionHash,
                matches,
                matches ? "ready" : CurrentRegionChanged);
        }

        public WorldOperationReceipt Execute(UndoWorldChangeSetRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            MapWorldOperationValidation.RequireConfirmation(request.Confirmed);
            if (!request.StrongConfirmed)
                throw new WorldOperationStrongConfirmationRequiredException();

            var descriptor = ReadDescriptor(request.ChangeSetId);
            if (!ValidDescriptor(descriptor) || descriptor == null ||
                !string.Equals(descriptor.ChangeSetId, request.ChangeSetId, StringComparison.Ordinal))
            {
                throw Conflict(ChangeSetInvalid);
            }
            if (!string.Equals(descriptor.SourceOperationId, request.SourceOperationId, StringComparison.Ordinal))
                throw Conflict(SourceOperationMismatch);
            if (!string.Equals(descriptor.WorldId, request.WorldId, StringComparison.Ordinal))
                throw Conflict(WorldMismatch);
            if (!string.Equals(descriptor.WorldVersion, request.WorldVersion, StringComparison.Ordinal))
                throw Conflict(WorldVersionMismatch);
            if (descriptor.ExpiresAtUtc <= request.RequestedAtUtc)
                throw Conflict(ChangeSetExpired);
            if (string.Equals(descriptor.BeforeHash, descriptor.AfterHash, StringComparison.Ordinal))
                throw Conflict(AlreadyUndone);
            if (!string.Equals(descriptor.AfterHash, request.CurrentRegionHash, StringComparison.Ordinal))
                throw Conflict(CurrentRegionChanged);

            ValidateSourceOperation(request, descriptor);
            ValidateBlob(descriptor);
            RejectExistingUndo(request.ChangeSetId);

            var region = descriptor.Region;
            return bridge.Enqueue(new WorldOperationIntent(
                request.ActorSubject,
                WorldOperationKind.UndoChangeSet,
                request.WorldId,
                request.WorldVersion,
                null,
                request.CorrelationId,
                "Undo change set " + request.ChangeSetId,
                true,
                new WorldRegionOperationTarget(
                    checked((int)region.Minimum.X),
                    checked((int)region.Minimum.Y),
                    checked((int)region.Minimum.Z),
                    checked((int)region.Maximum.X),
                    checked((int)region.Maximum.Y),
                    checked((int)region.Maximum.Z),
                    descriptor.ChangeSetId,
                    null),
                request.RequestedAtUtc));
        }

        private WorldChangeSetDescriptor? ReadDescriptor(string changeSetId)
        {
            try { return changeSets.Read(changeSetId); }
            catch { throw Conflict(ChangeSetInvalid); }
        }

        private void ValidateSourceOperation(
            UndoWorldChangeSetRequest request,
            WorldChangeSetDescriptor descriptor)
        {
            WorldOperationRecord source;
            try { source = bridge.Get(request.SourceOperationId); }
            catch { throw Conflict(SourceOperationMismatch); }
            if (source == null ||
                !string.Equals(source.OperationId, descriptor.SourceOperationId, StringComparison.Ordinal) ||
                !string.Equals(source.ChangeSetId, descriptor.ChangeSetId, StringComparison.Ordinal) ||
                !string.Equals(source.WorldId, descriptor.WorldId, StringComparison.Ordinal) ||
                !string.Equals(source.WorldVersion, descriptor.WorldVersion, StringComparison.Ordinal) ||
                source.Kind == WorldOperationKind.UndoChangeSet ||
                !source.IsReversible ||
                source.Status != WorldOperationStatus.Succeeded)
            {
                throw Conflict(SourceOperationMismatch);
            }
        }

        private static void ValidateSourceOperation(
            WorldOperationRecord source,
            WorldChangeSetDescriptor descriptor)
        {
            if (!string.Equals(source.OperationId, descriptor.SourceOperationId, StringComparison.Ordinal) ||
                !string.Equals(source.ChangeSetId, descriptor.ChangeSetId, StringComparison.Ordinal) ||
                !string.Equals(source.WorldId, descriptor.WorldId, StringComparison.Ordinal) ||
                !string.Equals(source.WorldVersion, descriptor.WorldVersion, StringComparison.Ordinal))
            {
                throw Conflict(SourceOperationMismatch);
            }
        }

        private static bool ValidDescriptor(WorldChangeSetDescriptor? descriptor) =>
            descriptor != null && descriptor.Region != null &&
            GeneratedStorageResourceId(descriptor.StorageResourceId);

        private void ValidateBlob(WorldChangeSetDescriptor descriptor)
        {
            try
            {
                WorldChangeSetValidation.RequireHash(descriptor.BeforeHash, nameof(descriptor.BeforeHash));
                WorldChangeSetValidation.RequireHash(descriptor.AfterHash, nameof(descriptor.AfterHash));
                var read = blobs.Read(descriptor.StorageResourceId, descriptor.BeforeHash);
                if (read == null ||
                    !string.Equals(read.StorageResourceId, descriptor.StorageResourceId, StringComparison.Ordinal) ||
                    !string.Equals(read.ContentHash, descriptor.BeforeHash, StringComparison.Ordinal) ||
                    !string.Equals(Hash(read.Content), descriptor.BeforeHash, StringComparison.Ordinal))
                {
                    throw Conflict(ChangeSetCorrupt);
                }
            }
            catch (WorldOperationConflictException) { throw; }
            catch { throw Conflict(ChangeSetCorrupt); }
        }

        private void RejectExistingUndo(string changeSetId)
        {
            WorldOperationCursor? cursor = null;
            do
            {
                var page = bridge.Query(new WorldOperationQuery(
                    100,
                    WorldOperationKind.UndoChangeSet,
                    null,
                    null,
                    null,
                    cursor));
                foreach (var operation in page.Items)
                {
                    if (string.Equals(operation.ChangeSetId, changeSetId, StringComparison.Ordinal))
                        throw Conflict(AlreadyUndone);
                }
                cursor = page.NextCursor;
            } while (cursor != null);
        }

        private static bool GeneratedStorageResourceId(string? value)
        {
            if (value == null || value.Length != 36 || !value.StartsWith("wcs-", StringComparison.Ordinal))
                return false;
            for (var index = 4; index < value.Length; index++)
            {
                var character = value[index];
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f')))
                    return false;
            }
            return true;
        }

        private static string Hash(byte[] content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            using var algorithm = SHA256.Create();
            return BitConverter.ToString(algorithm.ComputeHash(content))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }

        private static WorldOperationConflictException Conflict(string code) =>
            new WorldOperationConflictException(code);
    }
}
