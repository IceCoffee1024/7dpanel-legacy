using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Maps
{
    public sealed class SevenDaysMapProjectionRuntime : IModRuntime, IDisposable
    {
        private static readonly TimeSpan DefaultRefreshPeriod = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(5);

        private readonly SevenDaysMapMetadataProjection metadataProjection;
        private readonly SevenDaysMapGameTimeProjection gameTimeProjection;
        private readonly SevenDaysMapLayerProjection layerProjection;
        private readonly SevenDaysTransientEntityProjection transientEntityProjection;
        private readonly object lifecycleSync = new object();
        private readonly IModRuntime inner;
        private readonly Func<string, Func<SevenDaysMapSample>, TimeSpan, Task<SevenDaysMapSample>> dispatch;
        private readonly Func<SevenDaysMapSample> capture;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly TimeSpan refreshPeriod;
        private readonly Timer timer;
        private int ready;
        private int lifecycleGeneration;
        private int refreshingGeneration = -1;
        private int stopped;
        private int disposed;
        private Task refreshCompletion = Task.CompletedTask;

        public SevenDaysMapProjectionRuntime(
            SevenDaysMapMetadataProjection metadataProjection,
            SevenDaysMapGameTimeProjection gameTimeProjection,
            SevenDaysMapLayerProjection layerProjection,
            SevenDaysTransientEntityProjection transientEntityProjection,
            IModRuntime inner)
            : this(
                metadataProjection,
                gameTimeProjection,
                layerProjection,
                transientEntityProjection,
                inner,
                (operationName, action, timeout) => GameThreadDispatcher.Enqueue(
                    operationName,
                    action,
                    timeout,
                    CancellationToken.None),
                CaptureOnGameThread,
                () => DateTimeOffset.UtcNow,
                DefaultRefreshPeriod)
        {
        }

        internal SevenDaysMapProjectionRuntime(
            SevenDaysMapMetadataProjection metadataProjection,
            SevenDaysMapGameTimeProjection gameTimeProjection,
            IModRuntime inner,
            Func<string, Func<SevenDaysMapSample>, TimeSpan, Task<SevenDaysMapSample>> dispatch,
            Func<SevenDaysMapSample> capture,
            Func<DateTimeOffset> utcNow,
            TimeSpan refreshPeriod)
            : this(
                metadataProjection,
                gameTimeProjection,
                new SevenDaysMapLayerProjection(),
                new SevenDaysTransientEntityProjection(),
                inner,
                dispatch,
                capture,
                utcNow,
                refreshPeriod)
        {
        }

        internal SevenDaysMapProjectionRuntime(
            SevenDaysMapMetadataProjection metadataProjection,
            SevenDaysMapGameTimeProjection gameTimeProjection,
            SevenDaysMapLayerProjection layerProjection,
            SevenDaysTransientEntityProjection transientEntityProjection,
            IModRuntime inner,
            Func<string, Func<SevenDaysMapSample>, TimeSpan, Task<SevenDaysMapSample>> dispatch,
            Func<SevenDaysMapSample> capture,
            Func<DateTimeOffset> utcNow,
            TimeSpan refreshPeriod)
        {
            if (refreshPeriod <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(refreshPeriod));
            this.metadataProjection = metadataProjection ??
                throw new ArgumentNullException(nameof(metadataProjection));
            this.gameTimeProjection = gameTimeProjection ??
                throw new ArgumentNullException(nameof(gameTimeProjection));
            this.layerProjection = layerProjection ??
                throw new ArgumentNullException(nameof(layerProjection));
            this.transientEntityProjection = transientEntityProjection ??
                throw new ArgumentNullException(nameof(transientEntityProjection));
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
            this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            this.refreshPeriod = refreshPeriod;
            timer = new Timer(_ => BeginRefresh(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        internal Task RefreshCompletion => Volatile.Read(ref refreshCompletion);

        public void Start() => inner.Start();

        public void MarkGameReady()
        {
            lock (lifecycleSync)
            {
                if (disposed != 0) return;
                inner.MarkGameReady();
                stopped = 0;
                ready = 1;
                timer.Change(refreshPeriod, refreshPeriod);
            }
            BeginRefresh();
        }

        public void Stop()
        {
            var failures = new List<Exception>();
            lock (lifecycleSync)
            {
                if (stopped != 0) return;
                stopped = 1;
                ready = 0;
                lifecycleGeneration++;
                timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                try { metadataProjection.Clear(); } catch (Exception exception) { failures.Add(exception); }
                try { gameTimeProjection.Clear(); } catch (Exception exception) { failures.Add(exception); }
                try { layerProjection.Clear(); } catch (Exception exception) { failures.Add(exception); }
                try { transientEntityProjection.Stop(); } catch (Exception exception) { failures.Add(exception); }
                try { inner.Stop(); } catch (Exception exception) { failures.Add(exception); }
            }

            if (failures.Count > 0) throw new AggregateException(failures);
        }

        public void Dispose()
        {
            try { Stop(); }
            finally
            {
                lock (lifecycleSync)
                {
                    disposed = 1;
                    ready = 0;
                    stopped = 1;
                    lifecycleGeneration++;
                }
                timer.Dispose();
            }
        }

        private void BeginRefresh()
        {
            lock (lifecycleSync)
            {
                if (ready == 0 || stopped != 0 || disposed != 0)
                    return;
                var generation = lifecycleGeneration;
                if (refreshingGeneration == generation)
                    return;
                refreshingGeneration = generation;
                Volatile.Write(ref refreshCompletion, RefreshAsync(generation));
            }
        }

        private async Task RefreshAsync(int generation)
        {
            try
            {
                var sample = await dispatch(
                        "7DPanel.Map.Projection",
                        capture,
                        DispatchTimeout)
                    .ConfigureAwait(false);
                var observedAtUtc = utcNow();
                lock (lifecycleSync)
                {
                    if (ready != 0 && stopped == 0 && generation == lifecycleGeneration)
                    {
                        metadataProjection.Publish(sample, observedAtUtc);
                        gameTimeProjection.Publish(sample, observedAtUtc);
                        // Game entities and persistent map objects are not extracted by the
                        // supported game API boundary yet. Keep both projections explicit.
                        layerProjection.Clear();
                        transientEntityProjection.Stop();
                    }
                }
            }
            catch
            {
                lock (lifecycleSync)
                {
                    if (ready != 0 && stopped == 0 && generation == lifecycleGeneration)
                    {
                        metadataProjection.MarkCaptureFailed();
                        gameTimeProjection.MarkCaptureFailed();
                        layerProjection.Clear();
                        transientEntityProjection.Stop();
                    }
                }
            }
            finally
            {
                lock (lifecycleSync)
                {
                    if (refreshingGeneration == generation)
                        refreshingGeneration = -1;
                }
            }
        }

        private static SevenDaysMapSample CaptureOnGameThread()
        {
            var manager = global::GameManager.Instance;
            var world = manager?.World;
            if (manager == null || world == null)
                return new SevenDaysMapSample(null, null, worldAvailable: false);

            SevenDaysMapMetadataSample? metadata = null;
            SevenDaysMapGameTimeSample? gameTime = null;
            var metadataCaptureFailed = false;
            var gameTimeCaptureFailed = false;
            try
            {
                if (world.GetWorldExtent(out var minimum, out var maximum))
                {
                    var worldName = global::GamePrefs.GetString(global::EnumGamePrefs.GameWorld);
                    var worldId = world.Guid;
                    var tileSize = global::MapRendering.Constants.MapBlockSize;
                    var zoomLevelCount = global::MapRendering.Constants.Zoomlevels;
                    if (!string.IsNullOrWhiteSpace(worldName) &&
                        !string.IsNullOrWhiteSpace(worldId) &&
                        maximum.x > minimum.x &&
                        maximum.z > minimum.z &&
                        tileSize > 0 &&
                        zoomLevelCount > 0)
                    {
                        metadata = new SevenDaysMapMetadataSample(
                            worldName,
                            worldId,
                            minimum.x,
                            minimum.z,
                            maximum.x,
                            maximum.z,
                            tileSize,
                            zoomLevelCount);
                    }
                }
            }
            catch
            {
                metadataCaptureFailed = true;
            }

            try
            {
                var worldTime = world.worldTime;
                gameTime = new SevenDaysMapGameTimeSample(
                    global::GameUtils.WorldTimeToDays(worldTime),
                    global::GameUtils.WorldTimeToHours(worldTime),
                    global::GameUtils.WorldTimeToMinutes(worldTime));
            }
            catch
            {
                gameTimeCaptureFailed = true;
            }

            return new SevenDaysMapSample(
                metadata,
                gameTime,
                metadataCaptureFailed,
                gameTimeCaptureFailed);
        }
    }
}
