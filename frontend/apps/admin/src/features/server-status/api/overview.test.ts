import { afterEach, describe, expect, it, vi } from 'vitest'

import { requestJson } from '../../../shared/api/http'
import { OverviewError, parseOverview } from '../model/overview'
import { fetchOverview } from './overview'

vi.mock('../../../shared/api/http', () => ({
  requestJson: vi.fn(),
}))

const runtimeObservedAtUtc = '2026-07-25T01:02:03.1234567Z'

function metric<T>(
  value: T | null,
  source: string,
  unit: string,
  warning: 'readFailed' | 'unsupported' | null = null,
) {
  return { value, source, unit, observedAtUtc: runtimeObservedAtUtc, warning }
}

function ownerOverview() {
  return {
    availability: 'available',
    game: {
      availability: 'available',
      sampledAtUtc: '2026-07-25T01:02:03.1234567Z',
      gameTitle: '7 Days to Die',
      saveGameName: 'save-1',
      worldName: 'world-1',
      worldSessionUptimeSeconds: 321,
      version: '2.4',
      gameMode: 'Survival',
      difficulty: 'Nomad',
      region: 'EU',
      language: 'English',
      connectionAddress: '127.0.0.1',
      connectionPort: 26900,
      maximumPlayerCount: 8,
      runtimeMetrics: {
        gameDayTime: metric('Day 3', 'World.worldTime', 'game-clock'),
        isBloodMoon: metric(false, 'World.aiDirector.BloodMoonComponent.BloodMoonActive', 'boolean'),
        framesPerSecond: metric(60.5, 'GameManager.frameTime', 'frames/second'),
        onlinePlayerCount: metric(0, 'World.Players.Count', 'count'),
        historicalPlayerCount: metric(10, 'GameManager.persistentPlayerCount', 'count'),
        animalCount: metric(4, 'World.Entities', 'count'),
        hostileEntityCount: metric(9, 'World.Entities', 'count'),
        activeEntityCount: metric(25, 'World.Entities', 'count'),
        chunkCount: metric(144, 'Chunk.InstanceCount', 'count'),
        droppedItemCount: metric(null, 'World.Entities', 'count', 'readFailed'),
        gameMemoryBytes: metric(null, 'GC.GetTotalMemory(false)', 'bytes', 'unsupported'),
      },
    },
    host: {
      availability: 'available',
      identityAvailability: 'available',
      sampledAtUtc: '2026-07-25T01:02:03+00:00',
      processUptimeSeconds: 456,
      residentSetBytes: 1024,
      managedHeapBytes: 789,
      otherMemoryBytes: 235,
      cpuUsagePercent: 12.5,
      operatingSystem: 'Windows',
      operatingSystemVersion: '11',
      processorCount: 8,
      memoryTotalBytes: 16384,
      memoryAvailableBytes: 8192,
      additionalMemory: {
        kind: 'virtualAddressSpace',
        totalBytes: 32768,
        usedBytes: 4096,
      },
      storageVolumes: [{
        name: 'system',
        rootPath: 'C:\\',
        totalBytes: 1000,
        availableBytes: 500,
        isPrimaryDataVolume: true,
      }],
      publicNetwork: {
        availability: 'available',
        ipv4: '203.0.113.4',
        ipv6: '2001:db8::4',
      },
      deviceId: 'device-1',
      currentSystemUser: 'system-user',
      osFamily: 'Windows',
      operatingSystemArchitecture: 'x64',
      runtimeVersion: '4.8',
      cpuModel: 'Example CPU',
      logicalCoreCount: 8,
      cpuFrequencyMhz: 3600.25,
      deviceName: 'host-1',
      deviceModel: 'model-1',
      deviceType: 'server',
      processId: 42,
      processStartedAtUtc: '2026-07-25T00:00:00Z',
    },
    restartPolicy: {
      availability: 'available',
      isConfigured: true,
      scheduleDescription: 'daily',
      nextRestartAtUtc: '2026-07-26T01:02:03Z',
    },
    recentActivity: {
      availability: 'available',
      sampledAtUtc: '2026-07-25T01:02:03Z',
      totalCount: 2,
      latestOccurredAtUtc: '2026-07-25T01:01:00Z',
      items: [{
        occurredAtUtc: '2026-07-25T01:01:00Z',
        messageKey: 'player_joined',
        messageArguments: { player: 'Ada', entityId: '42' },
      }],
    },
    attention: [{ code: 'low_disk_space' }],
  }
}

describe('parseOverview', () => {
  it('recursively parses and freezes every Owner field', () => {
    const wire = ownerOverview()
    const result = parseOverview(wire)

    expect(result).toEqual(wire)
    expect(result).not.toBe(wire)
    expect(result.host).not.toBe(wire.host)
    expect(result.game.runtimeMetrics).not.toBe(wire.game.runtimeMetrics)
    expect(result.game.runtimeMetrics?.onlinePlayerCount).toEqual({
      value: 0,
      source: 'World.Players.Count',
      unit: 'count',
      observedAtUtc: runtimeObservedAtUtc,
      warning: null,
    })
    expect(result.game.runtimeMetrics?.gameMemoryBytes).toMatchObject({
      value: null,
      warning: 'unsupported',
    })
    expect(result.host.storageVolumes[0]).not.toBe(wire.host.storageVolumes[0])
    expect(result.recentActivity.items[0]?.messageArguments).not.toBe(wire.recentActivity.items[0]?.messageArguments)
    expect(Object.isFrozen(result)).toBe(true)
    expect(Object.isFrozen(result.host.storageVolumes)).toBe(true)
    expect(Object.isFrozen(result.recentActivity.items[0]?.messageArguments)).toBe(true)
  })

  it('accepts the complete NonOwner shape with omitted sensitive members and omitted additional memory', () => {
    const wire = ownerOverview()
    wire.host.identityAvailability = 'forbidden'
    delete (wire.host as Partial<typeof wire.host>).deviceId
    delete (wire.host as Partial<typeof wire.host>).currentSystemUser
    delete (wire.host as Partial<typeof wire.host>).additionalMemory
    delete (wire.host.publicNetwork as Partial<typeof wire.host.publicNetwork>).ipv4
    delete (wire.host.publicNetwork as Partial<typeof wire.host.publicNetwork>).ipv6
    delete (wire.host.storageVolumes[0] as Partial<typeof wire.host.storageVolumes[number]>).rootPath

    const result = parseOverview(wire)

    expect(result.host.identityAvailability).toBe('forbidden')
    expect(result.host).not.toHaveProperty('deviceId')
    expect(result.host).not.toHaveProperty('additionalMemory')
    expect(result.host.publicNetwork).not.toHaveProperty('ipv4')
    expect(result.host.storageVolumes[0]).not.toHaveProperty('rootPath')
  })

  it('accepts nullable Owner sensitive members and both additional-memory kinds', () => {
    const wire = ownerOverview()
    wire.host.deviceId = null as unknown as string
    wire.host.currentSystemUser = null as unknown as string
    wire.host.publicNetwork.ipv4 = null as unknown as string
    wire.host.publicNetwork.ipv6 = null as unknown as string
    wire.host.storageVolumes[0]!.rootPath = null as unknown as string
    wire.host.additionalMemory.kind = 'swap'

    expect(parseOverview(wire).host).toMatchObject({
      deviceId: null,
      currentSystemUser: null,
      additionalMemory: { kind: 'swap' },
      publicNetwork: { ipv4: null, ipv6: null },
      storageVolumes: [{ rootPath: null }],
    })
  })

  it.each([
    ['unknown availability', (wire: ReturnType<typeof ownerOverview>) => { wire.game.availability = 'ready' }],
    ['unknown additional-memory kind', (wire: ReturnType<typeof ownerOverview>) => { wire.host.additionalMemory.kind = 'physical' }],
    ['numeric string', (wire: ReturnType<typeof ownerOverview>) => { wire.game.runtimeMetrics.onlinePlayerCount.value = '2' as unknown as number }],
    ['negative byte count', (wire: ReturnType<typeof ownerOverview>) => { wire.host.memoryTotalBytes = -1 }],
    ['fractional integer', (wire: ReturnType<typeof ownerOverview>) => { wire.host.processId = 1.5 }],
    ['unsafe integer', (wire: ReturnType<typeof ownerOverview>) => { wire.game.worldSessionUptimeSeconds = Number.MAX_SAFE_INTEGER + 1 }],
    ['NaN', (wire: ReturnType<typeof ownerOverview>) => { wire.game.runtimeMetrics.framesPerSecond.value = Number.NaN }],
    ['missing warning for null metric', (wire: ReturnType<typeof ownerOverview>) => { wire.game.runtimeMetrics.droppedItemCount.warning = null }],
    ['warning on available metric', (wire: ReturnType<typeof ownerOverview>) => { wire.game.runtimeMetrics.animalCount.warning = 'readFailed' }],
    ['mismatched metric observation time', (wire: ReturnType<typeof ownerOverview>) => { wire.game.runtimeMetrics.chunkCount.observedAtUtc = '2026-07-25T01:02:04Z' }],
    ['Infinity', (wire: ReturnType<typeof ownerOverview>) => { wire.host.cpuUsagePercent = Number.POSITIVE_INFINITY }],
    ['non-UTC time', (wire: ReturnType<typeof ownerOverview>) => { wire.game.sampledAtUtc = '2026-07-25T09:02:03+08:00' }],
    ['impossible time', (wire: ReturnType<typeof ownerOverview>) => { wire.host.processStartedAtUtc = '2026-02-29T00:00:00Z' }],
    ['bad array item', (wire: ReturnType<typeof ownerOverview>) => { wire.attention = [null as unknown as { code: string }] }],
    ['missing nested field', (wire: ReturnType<typeof ownerOverview>) => { delete (wire.recentActivity as Partial<typeof wire.recentActivity>).items }],
    ['legacy gameName field', (wire: ReturnType<typeof ownerOverview>) => { Object.assign(wire.game, { gameName: 'legacy' }) }],
    ['legacy mapName field', (wire: ReturnType<typeof ownerOverview>) => { Object.assign(wire.game, { mapName: 'legacy' }) }],
    ['legacy unityHeapBytes field', (wire: ReturnType<typeof ownerOverview>) => { Object.assign(wire.host, { unityHeapBytes: 1 }) }],
    ['legacy serverUptimeSeconds field', (wire: ReturnType<typeof ownerOverview>) => { Object.assign(wire.game, { serverUptimeSeconds: 1 }) }],
    ['legacy gameTime field', (wire: ReturnType<typeof ownerOverview>) => { Object.assign(wire.game, { gameTime: 'legacy' }) }],
  ])('rejects %s with a stable safe error', (_name, mutate) => {
    const wire = ownerOverview()
    mutate(wire)

    const error = (() => {
      try {
        parseOverview(wire)
      }
      catch (value) {
        return value
      }
    })()

    expect(error).toBeInstanceOf(OverviewError)
    expect(error).toMatchObject({ name: 'OverviewError', code: 'invalid-response', message: 'Invalid overview response' })
    expect(error).not.toHaveProperty('detail')
  })
})

describe('fetchOverview', () => {
  afterEach(() => vi.clearAllMocks())

  it('performs the fixed authenticated GET with the supplied signal and parses the response', async () => {
    const wire = ownerOverview()
    vi.mocked(requestJson).mockResolvedValue(wire)
    const controller = new AbortController()

    await expect(fetchOverview('Bearer opaque.token', controller.signal)).resolves.toEqual(wire)

    expect(requestJson).toHaveBeenCalledExactlyOnceWith('/api/v1/overview', {
      headers: { Authorization: 'Bearer opaque.token' },
      method: 'GET',
      signal: controller.signal,
    })
  })

  it('preserves a safe 401 HttpError for the shared session-expiry flow', async () => {
    const unauthorized = Object.assign(new Error('safe'), { code: 'http', status: 401 })
    vi.mocked(requestJson).mockRejectedValue(unauthorized)

    await expect(fetchOverview('Bearer expired')).rejects.toBe(unauthorized)
  })
})
