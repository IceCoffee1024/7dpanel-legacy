using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.GameResources
{
    public sealed class SevenDaysGameResourceCatalog : IGameResourceCatalog
    {
        private const long MaximumIconBytes = 16L * 1024L * 1024L;

        private readonly object buildSync = new object();
        private readonly Func<CancellationToken, Task<GameResourceScalarDraft>> readDraft;
        private readonly Func<
            GameResourceScalarDraft,
            CancellationToken,
            GameResourceIconIndex> buildIndex;
        private readonly Action<string> log;
        private CatalogHolder holder = CatalogHolder.Building();
        private Task? buildTask;

        public SevenDaysGameResourceCatalog()
            : this(new SevenDaysGameResourceDraftReader().ReadAsync)
        {
        }

        internal SevenDaysGameResourceCatalog(
            Func<CancellationToken, Task<GameResourceScalarDraft>> readDraft,
            Func<GameResourceScalarDraft, CancellationToken, GameResourceIconIndex>? buildIndex = null,
            Action<string>? log = null)
        {
            this.readDraft = readDraft ?? throw new ArgumentNullException(nameof(readDraft));
            this.buildIndex = buildIndex ?? ((draft, cancellationToken) =>
                GameResourceIconIndex.Build(
                    draft.Resources,
                    draft.IconRoots,
                    cancellationToken));
            this.log = log ?? (_ => { });
        }

        public Task BuildAsync(CancellationToken cancellationToken)
        {
            lock (buildSync)
            {
                if (buildTask != null) return buildTask;

                var completion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                buildTask = completion.Task;
                _ = BuildCoreAsync(completion, cancellationToken);
                return buildTask;
            }
        }

        public GameResourceCatalogReadResult Read()
        {
            var current = Volatile.Read(ref holder);
            switch (current.Status)
            {
                case GameResourceCatalogReadStatus.Building:
                    return GameResourceCatalogReadResult.Building();
                case GameResourceCatalogReadStatus.Available:
                    return GameResourceCatalogReadResult.Available(current.Snapshot!);
                default:
                    return GameResourceCatalogReadResult.Unavailable();
            }
        }

        public async Task<GameResourceIconReadResult> ReadIconAsync(
            string catalogVersion,
            string resourceId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = Volatile.Read(ref holder);
            if (current.Status != GameResourceCatalogReadStatus.Available)
                return GameResourceIconReadResult.Unavailable();
            if (string.IsNullOrEmpty(catalogVersion) ||
                string.IsNullOrEmpty(resourceId) ||
                !string.Equals(
                    catalogVersion,
                    current.Snapshot!.CatalogVersion,
                    StringComparison.Ordinal) ||
                !current.Index!.TryGetIcon(resourceId, out var icon))
            {
                return GameResourceIconReadResult.Missing();
            }

            if (!GameResourceIconIndex.IsSafeIndexedFile(icon))
                return GameResourceIconReadResult.Missing();

            try
            {
                var before = new FileInfo(icon.CanonicalPath);
                before.Refresh();
                if (!MatchesIndexedFile(before, icon) ||
                    before.Length > MaximumIconBytes ||
                    before.Length > int.MaxValue)
                {
                    return GameResourceIconReadResult.Missing();
                }

                byte[] content;
                using (var stream = new FileStream(
                           icon.CanonicalPath,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.Read,
                           81920,
                           true))
                {
                    if (stream.Length != icon.Length)
                        return GameResourceIconReadResult.Missing();

                    content = new byte[(int)stream.Length];
                    var offset = 0;
                    while (offset < content.Length)
                    {
                        var read = await stream.ReadAsync(
                                content,
                                offset,
                                content.Length - offset,
                                cancellationToken)
                            .ConfigureAwait(false);
                        if (read == 0) return GameResourceIconReadResult.Missing();
                        offset += read;
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                var after = new FileInfo(icon.CanonicalPath);
                after.Refresh();
                if (!MatchesIndexedFile(after, icon) ||
                    !GameResourceIconIndex.IsSafeIndexedFile(icon))
                {
                    return GameResourceIconReadResult.Missing();
                }

                return GameResourceIconReadResult.Available(
                    content,
                    CreateEtag(catalogVersion, resourceId, icon));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (IsFileSystemException(exception))
            {
                SafeLog("A game resource icon could not be read.");
                return GameResourceIconReadResult.Missing();
            }
        }

        private async Task BuildCoreAsync(
            TaskCompletionSource<bool> completion,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var draft = await readDraft(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var index = await Task.Run(
                        () => buildIndex(draft, cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                var catalogVersion = CreateOpaqueId();
                var resources = index.Resources.Select(resource =>
                    new GameResourceCatalogEntry(
                        resource.ResourceId,
                        resource.Scalar.NumericId,
                        resource.Scalar.InternalName,
                        resource.Scalar.SimplifiedChineseName,
                        resource.Scalar.EnglishName,
                        resource.Scalar.IsBlock
                            ? GameResourceKind.Block
                            : GameResourceKind.Item,
                        resource.Scalar.IsPublic
                            ? GameResourceVisibility.Public
                            : GameResourceVisibility.Hidden,
                        resource.Scalar.MaxStack,
                        resource.Scalar.HasQuality,
                        resource.IconStatus,
                        resource.Scalar.IconTintHex));
                var snapshot = new GameResourceCatalogSnapshot(
                    catalogVersion,
                    draft.GameVersion,
                    draft.ObservedAtUtc,
                    resources,
                    draft.Warnings.Concat(index.Warnings));
                Interlocked.Exchange(
                    ref holder,
                    CatalogHolder.Available(snapshot, index));
                completion.TrySetResult(true);
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled();
            }
            catch
            {
                Interlocked.Exchange(ref holder, CatalogHolder.Unavailable());
                SafeLog("The game resource catalog build failed.");
                completion.TrySetResult(true);
            }
        }

        private void SafeLog(string message)
        {
            try { log(message); }
            catch { }
        }

        private static bool MatchesIndexedFile(
            FileInfo current,
            GameResourceIndexedIcon indexed) =>
            current.Exists &&
            current.Length == indexed.Length &&
            current.LastWriteTimeUtc.Ticks == indexed.LastWriteTimeUtcTicks &&
            current.CreationTimeUtc.Ticks == indexed.CreationTimeUtcTicks;

        private static string CreateEtag(
            string catalogVersion,
            string resourceId,
            GameResourceIndexedIcon icon)
        {
            var source = string.Join(
                "\n",
                catalogVersion,
                resourceId,
                icon.Length.ToString(CultureInfo.InvariantCulture),
                icon.LastWriteTimeUtcTicks.ToString(CultureInfo.InvariantCulture));
            byte[] hash;
            using (var algorithm = SHA256.Create())
                hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(source));
            return "\"" + BitConverter.ToString(hash).Replace("-", string.Empty) + "\"";
        }

        private static string CreateOpaqueId()
        {
            var bytes = new byte[24];
            using (var random = RandomNumberGenerator.Create())
                random.GetBytes(bytes);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static bool IsFileSystemException(Exception exception) =>
            exception is IOException ||
            exception is UnauthorizedAccessException ||
            exception is ArgumentException ||
            exception is NotSupportedException ||
            exception is System.Security.SecurityException;

        private sealed class CatalogHolder
        {
            private CatalogHolder(
                GameResourceCatalogReadStatus status,
                GameResourceCatalogSnapshot? snapshot,
                GameResourceIconIndex? index)
            {
                Status = status;
                Snapshot = snapshot;
                Index = index;
            }

            public GameResourceCatalogReadStatus Status { get; }
            public GameResourceCatalogSnapshot? Snapshot { get; }
            public GameResourceIconIndex? Index { get; }

            public static CatalogHolder Building() =>
                new CatalogHolder(GameResourceCatalogReadStatus.Building, null, null);

            public static CatalogHolder Available(
                GameResourceCatalogSnapshot snapshot,
                GameResourceIconIndex index) =>
                new CatalogHolder(
                    GameResourceCatalogReadStatus.Available,
                    snapshot,
                    index);

            public static CatalogHolder Unavailable() =>
                new CatalogHolder(GameResourceCatalogReadStatus.Unavailable, null, null);
        }
    }
}
