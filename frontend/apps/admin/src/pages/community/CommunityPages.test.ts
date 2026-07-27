import type { CommunityController } from '../../features/community'
import type { CommunityViewState } from '../../features/community/model/useCommunity'

import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { nextTick, readonly, shallowRef } from 'vue'

import { CitiesView, TeleportSettingsView, VoteConfigurationView } from '../../features/community'
import CitiesPage from './cities.vue'
import TeleportPage from './teleport.vue'
import VotesPage from './votes.vue'

const mocks = vi.hoisted(() => ({ useCommunity: vi.fn() }))

vi.mock('../../features/community', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../features/community')>()
  return { ...actual, useCommunity: mocks.useCommunity }
})

function state(value: CommunityViewState = 'idle') {
  return readonly(shallowRef(value))
}

function controller(): CommunityController {
  return {
    teleportSettingsState: state(),
    teleportSettings: readonly(shallowRef([])),
    homesState: state(),
    homes: readonly(shallowRef([])),
    friendshipState: state(),
    friendship: readonly(shallowRef(null)),
    friendshipRecordsState: state(),
    friendshipRecords: readonly(shallowRef([])),
    teleportOperationState: state(),
    teleportOperation: readonly(shallowRef(null)),
    teleportOperationsState: state(),
    teleportOperations: readonly(shallowRef([])),
    citiesState: state(),
    cities: readonly(shallowRef([])),
    fullCityListState: state(),
    fullCities: readonly(shallowRef([])),
    voteConfigurationsState: state(),
    voteConfigurations: readonly(shallowRef([])),
    voteRoundsState: state(),
    voteRounds: readonly(shallowRef([])),
    fullVoteRoundListState: state(),
    fullVoteRounds: readonly(shallowRef([])),
    voteRoundState: state(),
    voteRound: readonly(shallowRef(null)),
    settlement: readonly(shallowRef(null)),
    mutationState: readonly(shallowRef('idle')),
    mutationTarget: readonly(shallowRef(null)),
    loadTeleportSettings: vi.fn().mockResolvedValue(undefined),
    saveTeleportSetting: vi.fn(),
    queryHomes: vi.fn(),
    queryFriendship: vi.fn(),
    loadFriendshipRecords: vi.fn().mockResolvedValue(undefined),
    queryTeleportOperation: vi.fn(),
    loadTeleportOperations: vi.fn().mockResolvedValue(undefined),
    loadCities: vi.fn().mockResolvedValue(undefined),
    loadAllCities: vi.fn().mockResolvedValue(undefined),
    saveCity: vi.fn(),
    loadVoteConfigurations: vi.fn().mockResolvedValue(undefined),
    saveVoteConfiguration: vi.fn(),
    loadVoteRounds: vi.fn().mockResolvedValue(undefined),
    loadAllVoteRounds: vi.fn().mockResolvedValue(undefined),
    queryVoteRound: vi.fn(),
    settleVote: vi.fn(),
    clearMutationState: vi.fn(),
    dispose: vi.fn(),
  } as CommunityController
}

describe('community route pages', () => {
  it('loads full and available city lists on mount and refresh', async () => {
    const current = controller()
    mocks.useCommunity.mockReturnValue(current)

    const wrapper = mount(CitiesPage)

    expect(current.loadCities).toHaveBeenCalledOnce()
    expect(current.loadAllCities).toHaveBeenCalledOnce()

    wrapper.findComponent(CitiesView).vm.$emit('refresh')
    await nextTick()

    expect(current.loadCities).toHaveBeenCalledTimes(2)
    expect(current.loadAllCities).toHaveBeenCalledTimes(2)

    wrapper.unmount()
    expect(current.dispose).toHaveBeenCalledOnce()
  })

  it('loads teleport settings and full history on mount and refresh', async () => {
    const current = controller()
    mocks.useCommunity.mockReturnValue(current)

    const wrapper = mount(TeleportPage)

    expect(current.loadTeleportSettings).toHaveBeenCalledOnce()
    expect(current.loadFriendshipRecords).toHaveBeenCalledOnce()
    expect(current.loadTeleportOperations).toHaveBeenCalledOnce()

    wrapper.findComponent(TeleportSettingsView).vm.$emit('refresh')
    await nextTick()

    expect(current.loadTeleportSettings).toHaveBeenCalledTimes(2)
    expect(current.loadFriendshipRecords).toHaveBeenCalledTimes(2)
    expect(current.loadTeleportOperations).toHaveBeenCalledTimes(2)

    wrapper.unmount()
    expect(current.dispose).toHaveBeenCalledOnce()
  })

  it('loads vote configuration, queued work, and full history on mount and refresh', async () => {
    const current = controller()
    mocks.useCommunity.mockReturnValue(current)

    const wrapper = mount(VotesPage)

    expect(current.loadVoteConfigurations).toHaveBeenCalledOnce()
    expect(current.loadVoteRounds).toHaveBeenCalledOnce()
    expect(current.loadAllVoteRounds).toHaveBeenCalledOnce()

    wrapper.findComponent(VoteConfigurationView).vm.$emit('refresh')
    await nextTick()

    expect(current.loadVoteConfigurations).toHaveBeenCalledTimes(2)
    expect(current.loadVoteRounds).toHaveBeenCalledTimes(2)
    expect(current.loadAllVoteRounds).toHaveBeenCalledTimes(2)

    wrapper.unmount()
    expect(current.dispose).toHaveBeenCalledOnce()
  })

  it('refreshes full city data only after a confirmed save', async () => {
    const current = controller()
    vi.mocked(current.saveCity).mockResolvedValueOnce(true).mockResolvedValueOnce(false)
    mocks.useCommunity.mockReturnValue(current)

    const wrapper = mount(CitiesPage)
    vi.mocked(current.loadAllCities).mockClear()

    wrapper.findComponent(CitiesView).vm.$emit('save', {
      cityId: 'city-1',
      name: 'Trader',
      description: '',
      enabled: true,
      position: { worldId: 'Navezgane', x: 1, y: 2, z: 3, yaw: 4 },
      sortOrder: 0,
    })
    await flushPromises()

    expect(current.loadAllCities).toHaveBeenCalledOnce()

    wrapper.findComponent(CitiesView).vm.$emit('save', {
      cityId: 'city-1',
      name: 'Trader',
      description: '',
      enabled: false,
      position: { worldId: 'Navezgane', x: 1, y: 2, z: 3, yaw: 4 },
      sortOrder: 0,
    })
    await flushPromises()

    expect(current.loadAllCities).toHaveBeenCalledOnce()

    wrapper.unmount()
  })

  it('refreshes full vote history only after a confirmed settlement', async () => {
    const current = controller()
    vi.mocked(current.settleVote).mockResolvedValueOnce(true).mockResolvedValueOnce(false)
    mocks.useCommunity.mockReturnValue(current)

    const wrapper = mount(VotesPage)
    vi.mocked(current.loadAllVoteRounds).mockClear()

    wrapper.findComponent(VoteConfigurationView).vm.$emit('settle', 'round-1')
    await flushPromises()

    expect(current.loadAllVoteRounds).toHaveBeenCalledOnce()

    wrapper.findComponent(VoteConfigurationView).vm.$emit('settle', 'round-2')
    await flushPromises()

    expect(current.loadAllVoteRounds).toHaveBeenCalledOnce()

    wrapper.unmount()
  })
})
