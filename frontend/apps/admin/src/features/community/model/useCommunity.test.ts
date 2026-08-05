import type { City, FriendshipRecord, FriendshipStatus, PlayerHome, TeleportOperation, TeleportSettings, VoteRound, VoteSettlement } from '../api/community'

import { describe, expect, it, vi } from 'vitest'
import { isReadonly } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useCommunity } from './useCommunity'

const setting = Object.freeze<TeleportSettings>({
  kind: 'Home',
  enabled: true,
  maxHomes: 3,
  cooldownMs: 30_000n,
  globalCooldownMs: 5_000n,
  denyDuringBloodMoon: true,
  feeAmount: 25n,
  updatedAtUtc: '2026-07-27T02:00:00Z',
  rowVersion: 4n,
})

const auth = {
  authorizationHeader: 'Bearer owner' as string | null,
  expireSession: vi.fn(),
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((complete) => {
    resolve = complete
  })
  return { promise, resolve }
}

const home = Object.freeze<PlayerHome>({
  homeId: 'home-1',
  crossplatformId: 'EOS_1',
  name: 'Base',
  position: Object.freeze({ worldId: 'Navezgane', x: 10, y: 20, z: 30, yaw: 40 }),
  createdAtUtc: '2026-07-27T01:00:00Z',
  updatedAtUtc: '2026-07-27T02:00:00Z',
  rowVersion: 1n,
})

const friendship = Object.freeze<FriendshipStatus>({
  firstCrossplatformId: 'EOS_1',
  secondCrossplatformId: 'EOS_2',
  areFriends: true,
})

const city = Object.freeze<City>({
  cityId: 'city-1',
  name: 'Trader',
  description: 'Public destination',
  enabled: false,
  position: Object.freeze({ worldId: 'Navezgane', x: 10, y: 20, z: 30, yaw: 40 }),
  sortOrder: 10,
  createdAtUtc: '2026-07-27T01:00:00Z',
  updatedAtUtc: '2026-07-27T02:00:00Z',
  rowVersion: 2n,
})

const friendshipRecord = Object.freeze<FriendshipRecord>({
  friendshipId: 'friendship-1',
  memberACrossplatformId: 'EOS_1',
  memberBCrossplatformId: 'EOS_2',
  createdByCrossplatformId: 'EOS_1',
  acceptedAtUtc: '2026-07-27T01:30:00Z',
})

const teleportOperation = Object.freeze<TeleportOperation>({
  operationId: 'teleport-1',
  kind: 'City',
  crossplatformId: 'EOS_1',
  targetCrossplatformId: null,
  destination: Object.freeze({ worldId: 'Navezgane', x: 1, y: 2, z: 3, yaw: 4 }),
  origin: null,
  state: 'Completed',
  errorCode: null,
  correlationId: null,
  createdAtUtc: '2026-07-27T01:00:00Z',
  updatedAtUtc: '2026-07-27T02:00:00Z',
  completedAtUtc: '2026-07-27T02:00:00Z',
  rowVersion: 3n,
})

const voteRound = Object.freeze<VoteRound>({
  roundId: 'round-1',
  configurationId: 'vote-kick',
  kind: 'Kick',
  state: 'Open',
  initiatorCrossplatformId: 'EOS_1',
  targetCrossplatformId: 'EOS_2',
  scopeKey: 'community',
  eligibleCount: 4,
  thresholdPercent: 60,
  minimumParticipants: 2,
  allowVoteChange: true,
  actionJobId: null,
  actionOperationId: null,
  correlationId: null,
  openedAtUtc: '2026-07-27T01:00:00Z',
  expiresAtUtc: '2026-07-27T01:01:00Z',
  settledAtUtc: null,
  actionCompletedAtUtc: null,
  rowVersion: 8n,
})

describe('useCommunity', () => {
  it('exposes readonly state and coalesces concurrent loads', async () => {
    let release!: (value: readonly TeleportSettings[]) => void
    const fetchTeleportSettings = vi.fn(() => new Promise<readonly TeleportSettings[]>((resolve) => {
      release = resolve
    }))
    const controller = useCommunity({ auth, fetchTeleportSettings })

    const first = controller.loadTeleportSettings()
    const second = controller.loadTeleportSettings()

    expect(controller.teleportSettingsState.value).toBe('loading')
    expect(fetchTeleportSettings).toHaveBeenCalledOnce()
    expect(first).toBe(second)
    expect(isReadonly(controller.teleportSettings)).toBe(true)

    release([setting])
    await first

    expect(controller.teleportSettings.value).toEqual([setting])
    expect(controller.teleportSettingsState.value).toBe('ready')
  })

  it('does not change a setting until the server returns its authoritative version', async () => {
    let release!: (value: TeleportSettings) => void
    const updateTeleportSetting = vi.fn(() => new Promise<TeleportSettings>((resolve) => {
      release = resolve
    }))
    const controller = useCommunity({
      auth,
      fetchTeleportSettings: vi.fn().mockResolvedValue([setting]),
      updateTeleportSetting,
    })
    await controller.loadTeleportSettings()

    const save = controller.saveTeleportSetting(setting, {
      enabled: false,
      maxHomes: 2,
      cooldownMs: 1_000n,
      globalCooldownMs: 2_000n,
      denyDuringBloodMoon: false,
      feeAmount: 5n,
    })

    expect(controller.teleportSettings.value[0]).toBe(setting)
    expect(controller.mutationState.value).toBe('saving')

    const authoritative = Object.freeze({ ...setting, enabled: false, rowVersion: 5n })
    release(authoritative)
    await expect(save).resolves.toBe(true)

    expect(controller.teleportSettings.value[0]).toBe(authoritative)
    expect(controller.mutationState.value).toBe('confirmed')
  })

  it('keeps unavailable contracts explicit and maps authorization failures stably', async () => {
    const controller = useCommunity({
      auth,
      fetchCities: vi.fn().mockRejectedValue(new HttpError('http', 'forbidden', { status: 403 })),
      fetchActionQueuedVoteRounds: vi.fn().mockRejectedValue(new HttpError('http', 'unavailable', { status: 503 })),
    })

    await controller.loadCities()
    await controller.loadVoteRounds()

    expect(controller.fullCityListState.value).toBe('unavailable')
    expect(controller.fullVoteRoundListState.value).toBe('unavailable')
    expect(controller.citiesState.value).toBe('forbidden')
    expect(controller.voteRoundsState.value).toBe('unavailable')
  })

  it('preserves pending reconciliation from an operation query', async () => {
    const operation = Object.freeze({
      operationId: 'operation-1',
      kind: 'City' as const,
      crossplatformId: 'EOS_1',
      targetCrossplatformId: null,
      destination: Object.freeze({ worldId: 'Navezgane', x: 1, y: 2, z: 3, yaw: 4 }),
      origin: null,
      state: 'PendingReconciliation' as const,
      errorCode: 'result_unknown',
      correlationId: null,
      createdAtUtc: '2026-07-27T01:00:00Z',
      updatedAtUtc: '2026-07-27T02:00:00Z',
      completedAtUtc: null,
      rowVersion: 2n,
    })
    const controller = useCommunity({ auth, fetchTeleportOperation: vi.fn().mockResolvedValue(operation) })

    await controller.queryTeleportOperation('operation-1')

    expect(controller.teleportOperation.value?.state).toBe('PendingReconciliation')
    expect(controller.teleportOperationState.value).toBe('ready')
  })

  it('does not coalesce distinct normalized home queries and keeps the latest result', async () => {
    const firstResponse = deferred<readonly PlayerHome[]>()
    const secondResponse = deferred<readonly PlayerHome[]>()
    const fetchHomes = vi.fn()
      .mockReturnValueOnce(firstResponse.promise)
      .mockReturnValueOnce(secondResponse.promise)
    const controller = useCommunity({ auth, fetchHomes })

    const first = controller.queryHomes(' EOS_1 ')
    const same = controller.queryHomes('EOS_1')
    const second = controller.queryHomes('EOS_2')

    expect(same).toBe(first)
    expect(second).not.toBe(first)
    expect(fetchHomes).toHaveBeenCalledTimes(2)
    expect(fetchHomes).toHaveBeenNthCalledWith(1, 'Bearer owner', 'EOS_1', expect.any(AbortSignal))
    expect(fetchHomes).toHaveBeenNthCalledWith(2, 'Bearer owner', 'EOS_2', expect.any(AbortSignal))
    expect(fetchHomes.mock.calls[0][2].aborted).toBe(true)

    secondResponse.resolve([Object.freeze({ ...home, crossplatformId: 'EOS_2' })])
    await second
    firstResponse.resolve([home])
    await first

    expect(controller.homes.value).toEqual([expect.objectContaining({ crossplatformId: 'EOS_2' })])
  })

  it('does not coalesce distinct friendship queries and keeps the latest result', async () => {
    const firstResponse = deferred<FriendshipStatus>()
    const secondResponse = deferred<FriendshipStatus>()
    const fetchFriendship = vi.fn()
      .mockReturnValueOnce(firstResponse.promise)
      .mockReturnValueOnce(secondResponse.promise)
    const controller = useCommunity({ auth, fetchFriendship })

    const first = controller.queryFriendship(' EOS_1 ', ' EOS_2 ')
    const second = controller.queryFriendship('EOS_3', 'EOS_4')

    expect(fetchFriendship).toHaveBeenCalledTimes(2)
    expect(fetchFriendship).toHaveBeenNthCalledWith(1, 'Bearer owner', 'EOS_1', 'EOS_2', expect.any(AbortSignal))
    expect(fetchFriendship).toHaveBeenNthCalledWith(2, 'Bearer owner', 'EOS_3', 'EOS_4', expect.any(AbortSignal))
    expect(fetchFriendship.mock.calls[0][3].aborted).toBe(true)

    secondResponse.resolve(Object.freeze({ ...friendship, firstCrossplatformId: 'EOS_3', secondCrossplatformId: 'EOS_4' }))
    await second
    firstResponse.resolve(friendship)
    await first

    expect(controller.friendship.value).toEqual(expect.objectContaining({ firstCrossplatformId: 'EOS_3' }))
  })

  it('does not coalesce distinct teleport operation queries and keeps the latest result', async () => {
    const firstResponse = deferred<TeleportOperation>()
    const secondResponse = deferred<TeleportOperation>()
    const fetchTeleportOperation = vi.fn()
      .mockReturnValueOnce(firstResponse.promise)
      .mockReturnValueOnce(secondResponse.promise)
    const controller = useCommunity({ auth, fetchTeleportOperation })

    const first = controller.queryTeleportOperation(' operation-1 ')
    const second = controller.queryTeleportOperation('operation-2')

    expect(fetchTeleportOperation).toHaveBeenCalledTimes(2)
    expect(fetchTeleportOperation).toHaveBeenNthCalledWith(1, 'Bearer owner', 'operation-1', expect.any(AbortSignal))
    expect(fetchTeleportOperation).toHaveBeenNthCalledWith(2, 'Bearer owner', 'operation-2', expect.any(AbortSignal))
    expect(fetchTeleportOperation.mock.calls[0][2].aborted).toBe(true)

    secondResponse.resolve(Object.freeze({ ...teleportOperation, operationId: 'operation-2' }))
    await second
    firstResponse.resolve(teleportOperation)
    await first

    expect(controller.teleportOperation.value).toEqual(expect.objectContaining({ operationId: 'operation-2' }))
  })

  it('does not coalesce distinct vote round queries and keeps the latest result', async () => {
    const firstResponse = deferred<VoteRound>()
    const secondResponse = deferred<VoteRound>()
    const fetchVoteRound = vi.fn()
      .mockReturnValueOnce(firstResponse.promise)
      .mockReturnValueOnce(secondResponse.promise)
    const controller = useCommunity({ auth, fetchVoteRound })

    const first = controller.queryVoteRound(' round-1 ')
    const second = controller.queryVoteRound('round-2')

    expect(fetchVoteRound).toHaveBeenCalledTimes(2)
    expect(fetchVoteRound).toHaveBeenNthCalledWith(1, 'Bearer owner', 'round-1', expect.any(AbortSignal))
    expect(fetchVoteRound).toHaveBeenNthCalledWith(2, 'Bearer owner', 'round-2', expect.any(AbortSignal))
    expect(fetchVoteRound.mock.calls[0][2].aborted).toBe(true)

    secondResponse.resolve(Object.freeze({ ...voteRound, roundId: 'round-2' }))
    await second
    firstResponse.resolve(voteRound)
    await first

    expect(controller.voteRound.value).toEqual(expect.objectContaining({ roundId: 'round-2' }))
  })

  it('loads complete city, friendship, teleport, and vote record lists independently', async () => {
    const fetchAllCities = vi.fn().mockResolvedValue([city])
    const fetchFriendshipRecords = vi.fn().mockResolvedValue([friendshipRecord])
    const fetchTeleportOperations = vi.fn().mockResolvedValue([teleportOperation])
    const fetchVoteRounds = vi.fn().mockResolvedValue([voteRound])
    const controller = useCommunity({
      auth,
      fetchAllCities,
      fetchFriendshipRecords,
      fetchTeleportOperations,
      fetchVoteRounds,
    })

    await Promise.all([
      controller.loadAllCities(),
      controller.loadFriendshipRecords(),
      controller.loadTeleportOperations(),
      controller.loadAllVoteRounds(),
    ])

    expect(controller.fullCities.value).toEqual([city])
    expect(controller.fullCityListState.value).toBe('ready')
    expect(controller.friendshipRecords.value).toEqual([friendshipRecord])
    expect(controller.friendshipRecordsState.value).toBe('ready')
    expect(controller.teleportOperations.value).toEqual([teleportOperation])
    expect(controller.teleportOperationsState.value).toBe('ready')
    expect(controller.fullVoteRounds.value).toEqual([voteRound])
    expect(controller.fullVoteRoundListState.value).toBe('ready')
    expect(fetchAllCities).toHaveBeenCalledWith('Bearer owner', expect.any(AbortSignal))
    expect(fetchFriendshipRecords).toHaveBeenCalledWith('Bearer owner', expect.any(AbortSignal))
    expect(fetchTeleportOperations).toHaveBeenCalledWith('Bearer owner', expect.any(AbortSignal))
    expect(fetchVoteRounds).toHaveBeenCalledWith('Bearer owner', expect.any(AbortSignal))
  })

  it('updates both city projections after an authoritative mutation', async () => {
    const enabledCity = Object.freeze({ ...city, enabled: true })
    const authoritative = Object.freeze({ ...enabledCity, enabled: false, rowVersion: 3n })
    const controller = useCommunity({
      auth,
      fetchCities: vi.fn().mockResolvedValue([enabledCity]),
      fetchAllCities: vi.fn().mockResolvedValue([enabledCity]),
      upsertCity: vi.fn().mockResolvedValue(authoritative),
    })

    await controller.loadCities()
    await controller.loadAllCities()
    await expect(controller.saveCity({
      cityId: enabledCity.cityId,
      name: enabledCity.name,
      description: enabledCity.description,
      enabled: false,
      position: enabledCity.position,
      sortOrder: enabledCity.sortOrder,
    })).resolves.toBe(true)

    expect(controller.cities.value).toEqual([])
    expect(controller.fullCities.value).toEqual([])
    expect(controller.citiesState.value).toBe('empty')
    expect(controller.fullCityListState.value).toBe('empty')
  })

  it('updates the full vote projection while removing terminal rounds from the action queue', async () => {
    const queuedRound = Object.freeze({ ...voteRound, state: 'ActionQueued' as const })
    const settledRound = Object.freeze({
      ...queuedRound,
      state: 'ActionSucceeded' as const,
      settledAtUtc: '2026-07-27T01:02:00Z',
    })
    const settlement: VoteSettlement = {
      status: 'Settled',
      round: settledRound,
      participantCount: 3,
      yesCount: 2,
      noCount: 1,
      wasSettled: true,
    }
    const controller = useCommunity({
      auth,
      fetchActionQueuedVoteRounds: vi.fn().mockResolvedValue([queuedRound]),
      fetchVoteRounds: vi.fn().mockResolvedValue([queuedRound]),
      settleVoteRound: vi.fn().mockResolvedValue(settlement),
    })

    await controller.loadVoteRounds()
    await controller.loadAllVoteRounds()
    await expect(controller.settleVote('round-1')).resolves.toBe(true)

    expect(controller.voteRounds.value).toEqual([])
    expect(controller.fullVoteRounds.value).toEqual([settledRound])
    expect(controller.voteRoundsState.value).toBe('empty')
    expect(controller.fullVoteRoundListState.value).toBe('ready')
  })

  it('clears mutation state and target when disposing an in-flight mutation', async () => {
    const response = deferred<TeleportSettings>()
    const updateTeleportSetting = vi.fn().mockReturnValue(response.promise)
    const controller = useCommunity({ auth, updateTeleportSetting })

    const save = controller.saveTeleportSetting(setting, {
      enabled: false,
      maxHomes: 2,
      cooldownMs: 1_000n,
      globalCooldownMs: 2_000n,
      denyDuringBloodMoon: false,
      feeAmount: 5n,
    })

    expect(controller.mutationState.value).toBe('saving')
    expect(controller.mutationTarget.value).toEqual({ kind: 'teleport-setting', id: 'Home' })

    controller.dispose()

    expect(updateTeleportSetting.mock.calls[0][3].aborted).toBe(true)
    expect(controller.mutationState.value).toBe('idle')
    expect(controller.mutationTarget.value).toBeNull()

    response.resolve(Object.freeze({ ...setting, enabled: false, rowVersion: 5n }))
    await expect(save).resolves.toBe(false)
    expect(controller.mutationState.value).toBe('idle')
    expect(controller.mutationTarget.value).toBeNull()
  })
})
