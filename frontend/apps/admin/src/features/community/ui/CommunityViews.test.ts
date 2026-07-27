import type { CommunityController, CommunityViewState } from '../model/useCommunity'

import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { readonly, shallowRef } from 'vue'

import CitiesView from './CitiesView.vue'
import TeleportSettingsView from './TeleportSettingsView.vue'
import VoteConfigurationView from './VoteConfigurationView.vue'

function state(value: CommunityViewState = 'idle') {
  return readonly(shallowRef(value))
}

function controller(overrides: Partial<CommunityController> = {}): CommunityController {
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
    fullCityListState: readonly(shallowRef('unavailable')),
    fullCities: readonly(shallowRef([])),
    voteConfigurationsState: state(),
    voteConfigurations: readonly(shallowRef([])),
    voteRoundsState: state(),
    voteRounds: readonly(shallowRef([])),
    fullVoteRoundListState: readonly(shallowRef('unavailable')),
    fullVoteRounds: readonly(shallowRef([])),
    voteRoundState: state(),
    voteRound: readonly(shallowRef(null)),
    settlement: readonly(shallowRef(null)),
    mutationState: readonly(shallowRef('idle')),
    mutationTarget: readonly(shallowRef(null)),
    loadTeleportSettings: vi.fn(),
    saveTeleportSetting: vi.fn(),
    queryHomes: vi.fn(),
    queryFriendship: vi.fn(),
    loadFriendshipRecords: vi.fn(),
    queryTeleportOperation: vi.fn(),
    loadTeleportOperations: vi.fn(),
    loadCities: vi.fn(),
    loadAllCities: vi.fn(),
    saveCity: vi.fn(),
    loadVoteConfigurations: vi.fn(),
    saveVoteConfiguration: vi.fn(),
    loadVoteRounds: vi.fn(),
    loadAllVoteRounds: vi.fn(),
    queryVoteRound: vi.fn(),
    settleVote: vi.fn(),
    clearMutationState: vi.fn(),
    dispose: vi.fn(),
    ...overrides,
  } as CommunityController
}

const dashboardStub = {
  template: '<section><slot name="header"/><slot name="body"/></section>',
}

describe('community views', () => {
  it('keeps a pending teleport operation visible as an unknown result', () => {
    const wrapper = mount(TeleportSettingsView, {
      props: {
        controller: controller({
          teleportOperationState: state('ready'),
          teleportOperation: readonly(shallowRef({
            operationId: 'operation-1',
            kind: 'City' as const,
            crossplatformId: 'EOS_1',
            targetCrossplatformId: null,
            destination: { worldId: 'Navezgane', x: 1, y: 2, z: 3, yaw: 4 },
            origin: null,
            state: 'PendingReconciliation' as const,
            errorCode: 'result_unknown',
            correlationId: null,
            createdAtUtc: '2026-07-27T01:00:00Z',
            updatedAtUtc: '2026-07-27T02:00:00Z',
            completedAtUtc: null,
            rowVersion: 2n,
          })),
        }),
      },
      global: { stubs: { UDashboardPanel: dashboardStub } },
    })

    expect(wrapper.text()).toContain('等待人工核对')
  })

  it('shows the full city list with disabled cities and exposes no delete action', () => {
    const wrapper = mount(CitiesView, {
      props: {
        controller: controller({
          fullCityListState: state('ready'),
          fullCities: readonly(shallowRef([{
            cityId: 'city-1',
            name: 'Closed Trader',
            description: 'Public',
            enabled: false,
            position: { worldId: 'Navezgane', x: 1, y: 2, z: 3, yaw: 4 },
            sortOrder: 1,
            createdAtUtc: '2026-07-27T01:00:00Z',
            updatedAtUtc: '2026-07-27T02:00:00Z',
            rowVersion: 1n,
          }])),
        }),
      },
      global: { stubs: { UDashboardPanel: dashboardStub } },
    })

    expect(wrapper.text()).toContain('Closed Trader')
    expect(wrapper.text()).toContain('已禁用')
    expect(wrapper.find('[data-testid="delete-city"]').exists()).toBe(false)
  })

  it('shows full friendship and teleport-operation history alongside direct lookup results', () => {
    const wrapper = mount(TeleportSettingsView, {
      props: {
        controller: controller({
          friendshipState: state('ready'),
          friendship: readonly(shallowRef({
            firstCrossplatformId: 'EOS_DIRECT_A',
            secondCrossplatformId: 'EOS_DIRECT_B',
            areFriends: true,
          })),
          friendshipRecordsState: state('ready'),
          friendshipRecords: readonly(shallowRef([{
            friendshipId: 'friendship-1',
            memberACrossplatformId: 'EOS_A',
            memberBCrossplatformId: 'EOS_B',
            createdByCrossplatformId: 'EOS_A',
            acceptedAtUtc: '2026-07-27T01:00:00Z',
          }])),
          teleportOperationState: state('ready'),
          teleportOperation: readonly(shallowRef({
            operationId: 'lookup-operation-1',
            kind: 'Friend' as const,
            crossplatformId: 'EOS_DIRECT_A',
            targetCrossplatformId: 'EOS_DIRECT_B',
            destination: { worldId: 'Navezgane', x: 4, y: 5, z: 6, yaw: 7 },
            origin: null,
            state: 'Completed' as const,
            errorCode: null,
            correlationId: null,
            createdAtUtc: '2026-07-27T01:00:00Z',
            updatedAtUtc: '2026-07-27T02:00:00Z',
            completedAtUtc: '2026-07-27T02:00:00Z',
            rowVersion: 1n,
          })),
          teleportOperationsState: state('ready'),
          teleportOperations: readonly(shallowRef([{
            operationId: 'history-operation-1',
            kind: 'City' as const,
            crossplatformId: 'EOS_1',
            targetCrossplatformId: null,
            destination: { worldId: 'Navezgane', x: 1, y: 2, z: 3, yaw: 4 },
            origin: null,
            state: 'Completed' as const,
            errorCode: null,
            correlationId: null,
            createdAtUtc: '2026-07-27T01:00:00Z',
            updatedAtUtc: '2026-07-27T02:00:00Z',
            completedAtUtc: '2026-07-27T02:00:00Z',
            rowVersion: 2n,
          }])),
        }),
      },
      global: { stubs: { UDashboardPanel: dashboardStub } },
    })

    expect(wrapper.text()).toContain('friendship-1')
    expect(wrapper.text()).toContain('history-operation-1')
    expect(wrapper.text()).toContain('EOS_DIRECT_A ↔ EOS_DIRECT_B')
    expect(wrapper.text()).toContain('lookup-operation-1')
  })

  it('shows general vote history separately from queued action work and distinguishes unknown action results', () => {
    const round = {
      roundId: 'round-1',
      configurationId: 'vote-kick',
      kind: 'Kick' as const,
      state: 'ActionResultUnknown' as const,
      initiatorCrossplatformId: 'EOS_1',
      targetCrossplatformId: 'EOS_2',
      scopeKey: 'community',
      eligibleCount: 4,
      thresholdPercent: 60,
      minimumParticipants: 2,
      allowVoteChange: true,
      actionJobId: 'job-1',
      actionOperationId: null,
      correlationId: null,
      openedAtUtc: '2026-07-27T01:00:00Z',
      expiresAtUtc: '2026-07-27T01:01:00Z',
      settledAtUtc: '2026-07-27T01:02:00Z',
      actionCompletedAtUtc: null,
      rowVersion: 3n,
    }
    const wrapper = mount(VoteConfigurationView, {
      props: {
        controller: controller({
          fullVoteRoundListState: state('ready'),
          fullVoteRounds: readonly(shallowRef([{ ...round, roundId: 'history-round-1' }])),
          voteRoundsState: state('ready'),
          voteRounds: readonly(shallowRef([{ ...round, roundId: 'queued-round-1', state: 'ActionQueued' as const }])),
          voteRoundState: state('ready'),
          voteRound: readonly(shallowRef(round)),
        }),
      },
      global: { stubs: { UDashboardPanel: dashboardStub } },
    })

    expect(wrapper.text()).toContain('history-round-1')
    expect(wrapper.text()).toContain('queued-round-1')
    expect(wrapper.text()).not.toContain('全量轮次列表不可用')
    expect(wrapper.text()).toContain('动作结果未知')

    const historySection = wrapper.findAll('section').find(section => section.text().includes('全部投票轮次'))
    const queuedSection = wrapper.findAll('section').find(section => section.text().includes('待动作轮次'))

    expect(historySection?.text()).toContain('history-round-1')
    expect(historySection?.text()).not.toContain('queued-round-1')
    expect(queuedSection?.text()).toContain('queued-round-1')
    expect(queuedSection?.text()).not.toContain('history-round-1')
  })
})
