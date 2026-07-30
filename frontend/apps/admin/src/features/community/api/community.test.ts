import { afterEach, describe, expect, it, vi } from 'vitest'

import { requestJson } from '../../../shared/api/http'
import {
  fetchActionQueuedVoteRounds,
  fetchAllCities,
  fetchCities,
  fetchFriendship,
  fetchFriendshipRecords,
  fetchHomes,
  fetchTeleportOperation,
  fetchTeleportOperations,
  fetchVoteRounds,
  parseCity,
  parseFriendshipRecord,
  parseFriendshipStatus,
  parsePlayerHome,
  parseTeleportOperation,
  parseTeleportSettings,
  parseVoteConfiguration,
  parseVoteRound,
  parseVoteSettlement,
  settleVoteRound,
  updateTeleportSetting,
} from './community'

vi.mock('../../../shared/api/http', () => ({ requestJson: vi.fn() }))

const position = {
  worldId: 'Navezgane',
  x: 10.5,
  y: 42,
  z: -7.25,
  yaw: 90,
}

const homeExperience = {
  setFeeAmount: 10,
  listCommandName: 'homes',
  setCommandName: 'sethome',
  deleteCommandName: 'delhome',
  teleportCommandName: 'home',
  noHomesMessage: 'You have no saved homes.',
  limitMessage: 'Home limit reached.',
  setSuccessMessage: 'Home saved.',
  overwriteMessage: 'Home updated.',
  deleteSuccessMessage: 'Home deleted.',
  notFoundMessage: 'Home not found.',
  cooldownMessage: 'Teleport cooldown is active.',
  teleportSuccessMessage: 'Teleported home.',
  setInsufficientFundsMessage: 'Not enough balance to set a home.',
  teleportInsufficientFundsMessage: 'Not enough balance to teleport home.',
  bloodMoonMessage: 'Home teleport is disabled during a blood moon.',
}

const teleportSetting = {
  kind: 'Home',
  enabled: true,
  maxHomes: 3,
  cooldownMs: 30_000,
  globalCooldownMs: 5_000,
  denyDuringBloodMoon: true,
  feeAmount: 25,
  homeExperience,
  updatedAtUtc: '2026-07-27T02:00:00Z',
  rowVersion: 4,
}

const home = {
  homeId: 'home-1',
  crossplatformId: 'EOS_1',
  name: 'base',
  position,
  createdAtUtc: '2026-07-27T01:00:00Z',
  updatedAtUtc: '2026-07-27T02:00:00Z',
  rowVersion: 2,
}

const city = {
  cityId: 'city-1',
  name: 'Trader',
  description: 'Public destination',
  enabled: true,
  position,
  sortOrder: 10,
  createdAtUtc: '2026-07-27T01:00:00Z',
  updatedAtUtc: '2026-07-27T02:00:00Z',
  rowVersion: 2,
}

const operation = {
  operationId: 'teleport-1',
  kind: 'City',
  crossplatformId: 'EOS_1',
  targetCrossplatformId: null,
  destination: position,
  origin: null,
  state: 'PendingReconciliation',
  errorCode: 'result_unknown',
  correlationId: 'correlation-1',
  createdAtUtc: '2026-07-27T01:00:00Z',
  updatedAtUtc: '2026-07-27T02:00:00Z',
  completedAtUtc: null,
  rowVersion: 3,
}

const friendshipRecord = {
  friendshipId: 'friendship-1',
  memberACrossplatformId: 'EOS_1',
  memberBCrossplatformId: 'EOS_2',
  createdByCrossplatformId: 'EOS_1',
  acceptedAtUtc: '2026-07-27T01:30:00Z',
}

const voteConfiguration = {
  configurationId: 'vote-kick',
  kind: 'Kick',
  enabled: true,
  durationMs: 60_000,
  thresholdPercent: 60,
  minimumParticipants: 2,
  initiatorMinimumOnlineMs: 300_000,
  participantMinimumOnlineMs: 60_000,
  initiatorCooldownMs: 600_000,
  targetCooldownMs: 600_000,
  globalCooldownMs: 60_000,
  mutualExclusionScope: 'community',
  allowVoteChange: true,
  updatedAtUtc: '2026-07-27T02:00:00Z',
  rowVersion: 7,
}

const voteRound = {
  roundId: 'round-1',
  configurationId: 'vote-kick',
  kind: 'Kick',
  state: 'ActionQueued',
  initiatorCrossplatformId: 'EOS_1',
  targetCrossplatformId: 'EOS_2',
  scopeKey: 'community',
  eligibleCount: 4,
  thresholdPercent: 60,
  minimumParticipants: 2,
  allowVoteChange: true,
  actionJobId: 'job-1',
  actionOperationId: null,
  correlationId: 'correlation-2',
  openedAtUtc: '2026-07-27T01:00:00Z',
  expiresAtUtc: '2026-07-27T01:01:00Z',
  settledAtUtc: '2026-07-27T01:02:00Z',
  actionCompletedAtUtc: null,
  rowVersion: 8,
}

afterEach(() => vi.clearAllMocks())

describe('community protocol parsers', () => {
  it('parses and freezes the approved community response shapes', () => {
    const setting = parseTeleportSettings(teleportSetting)
    const parsedHome = parsePlayerHome(home)
    const parsedCity = parseCity(city)
    const friendship = parseFriendshipStatus({
      firstCrossplatformId: 'EOS_1',
      secondCrossplatformId: 'EOS_2',
      areFriends: true,
    })

    expect(setting.rowVersion).toBe(4n)
    expect(parsedHome.position).toEqual(position)
    expect(parsedCity.sortOrder).toBe(10)
    expect(friendship.areFriends).toBe(true)
    expect(Object.isFrozen(parsedHome.position)).toBe(true)
  })

  it('rejects unknown fields, illegal states, non-UTC timestamps, and invalid row versions', () => {
    expect(() => parseTeleportSettings({ ...teleportSetting, databasePath: 'private' })).toThrow('Invalid community response')
    expect(() => parseTeleportSettings({ ...teleportSetting, updatedAtUtc: '2026-07-27T10:00:00+08:00' })).toThrow('Invalid community response')
    expect(() => parseCity({ ...city, rowVersion: -1 })).toThrow('Invalid community response')
    expect(() => parseTeleportOperation({ ...operation, state: 'Succeeded' })).toThrow('Invalid community response')
    expect(() => parseVoteRound({ ...voteRound, state: 'Running' })).toThrow('Invalid community response')
    expect(() => parseVoteConfiguration({ ...voteConfiguration, kind: 'Ban' })).toThrow('Invalid community response')
    expect(() => parseFriendshipRecord({ ...friendshipRecord, privateNote: 'secret' })).toThrow('Invalid community response')
  })

  it('preserves pending reconciliation and validates settlement totals', () => {
    expect(parseTeleportOperation(operation).state).toBe('PendingReconciliation')
    expect(parseVoteSettlement({
      status: 'Settled',
      round: voteRound,
      participantCount: 3,
      yesCount: 2,
      noCount: 1,
      wasSettled: true,
    }).yesCount).toBe(2)

    expect(() => parseVoteSettlement({
      status: 'Completed',
      round: voteRound,
      participantCount: 3,
      yesCount: 2,
      noCount: 1,
      wasSettled: true,
    })).toThrow('Invalid community response')
  })
})

describe('community transport', () => {
  it('uses only the available list contracts and safely encoded identifiers', async () => {
    vi.mocked(requestJson)
      .mockResolvedValueOnce([{ ...home, crossplatformId: 'EOS 1' }])
      .mockResolvedValueOnce([city])
      .mockResolvedValueOnce({ firstCrossplatformId: 'EOS 1', secondCrossplatformId: 'EOS/2', areFriends: false })
      .mockResolvedValueOnce({ ...operation, operationId: 'operation/1' })
      .mockResolvedValueOnce([voteRound])

    await fetchHomes('Bearer owner', 'EOS 1')
    await fetchCities('Bearer owner')
    await fetchFriendship('Bearer owner', 'EOS 1', 'EOS/2')
    await fetchTeleportOperation('Bearer owner', 'operation/1')
    await fetchActionQueuedVoteRounds('Bearer owner')

    expect(vi.mocked(requestJson).mock.calls.map(call => call[0])).toEqual([
      '/api/v1/community/homes?crossplatformId=EOS+1',
      '/api/v1/community/cities?enabledOnly=true',
      '/api/v1/community/friendships?firstCrossplatformId=EOS+1&secondCrossplatformId=EOS%2F2',
      '/api/v1/community/teleport-operations/operation%2F1',
      '/api/v1/community/vote-rounds?actionQueuedOnly=true',
    ])
  })

  it('loads complete community records without weakening action-queued filtering', async () => {
    const disabledCity = { ...city, cityId: 'city-2', enabled: false }
    const openRound = {
      ...voteRound,
      roundId: 'round-2',
      state: 'Open',
      actionJobId: null,
      settledAtUtc: null,
    }
    vi.mocked(requestJson)
      .mockResolvedValueOnce([city, disabledCity])
      .mockResolvedValueOnce([friendshipRecord])
      .mockResolvedValueOnce([operation])
      .mockResolvedValueOnce([openRound, voteRound])
      .mockResolvedValueOnce([voteRound])

    await expect(fetchAllCities('Bearer owner')).resolves.toEqual(expect.arrayContaining([
      expect.objectContaining({ cityId: 'city-2', enabled: false }),
    ]))
    await expect(fetchFriendshipRecords('Bearer owner')).resolves.toEqual([
      expect.objectContaining({ friendshipId: 'friendship-1' }),
    ])
    await expect(fetchTeleportOperations('Bearer owner')).resolves.toEqual([
      expect.objectContaining({ operationId: 'teleport-1' }),
    ])
    await expect(fetchVoteRounds('Bearer owner')).resolves.toEqual(expect.arrayContaining([
      expect.objectContaining({ roundId: 'round-2', state: 'Open' }),
    ]))
    await expect(fetchActionQueuedVoteRounds('Bearer owner')).resolves.toEqual([
      expect.objectContaining({ state: 'ActionQueued' }),
    ])

    expect(vi.mocked(requestJson).mock.calls.map(call => call[0])).toEqual([
      '/api/v1/community/cities?enabledOnly=false',
      '/api/v1/community/friendships/records',
      '/api/v1/community/teleport-operations',
      '/api/v1/community/vote-rounds',
      '/api/v1/community/vote-rounds?actionQueuedOnly=true',
    ])
  })

  it('sends row versions without changing local state and parses authoritative mutation responses', async () => {
    vi.mocked(requestJson)
      .mockResolvedValueOnce({ ...teleportSetting, rowVersion: 5 })
      .mockResolvedValueOnce({
        status: 'AlreadySettled',
        round: { ...voteRound, roundId: 'round/1' },
        participantCount: 3,
        yesCount: 2,
        noCount: 1,
        wasSettled: false,
      })

    const saved = await updateTeleportSetting('Bearer owner', parseTeleportSettings(teleportSetting), {
      enabled: false,
      maxHomes: 2,
      cooldownMs: 1_000n,
      globalCooldownMs: 2_000n,
      denyDuringBloodMoon: false,
      feeAmount: 5n,
      homeExperience: {
        ...homeExperience,
        setFeeAmount: 10n,
      },
    })
    const settlement = await settleVoteRound('Bearer owner', 'round/1')

    expect(saved.rowVersion).toBe(5n)
    expect(settlement.status).toBe('AlreadySettled')
    expect(requestJson).toHaveBeenNthCalledWith(1, '/api/v1/community/teleport-settings/Home', expect.objectContaining({
      method: 'PUT',
      body: JSON.stringify({
        enabled: false,
        maxHomes: 2,
        cooldownMs: 1_000,
        globalCooldownMs: 2_000,
        denyDuringBloodMoon: false,
        feeAmount: 5,
        homeExperience,
        expectedRowVersion: 4,
      }),
    }))
    expect(requestJson).toHaveBeenNthCalledWith(2, '/api/v1/community/vote-rounds/round%2F1/settle', expect.objectContaining({
      method: 'POST',
    }))
  })
})
