import type {
  KickPlayerController,
  KickPlayerFeedback,
  KickPlayerResponse,
  OnlinePlayer,
  OnlinePlayersController,
  OnlinePlayersErrorCode,
  OnlinePlayersSnapshot,
  OnlinePlayersState,
} from '..'

import { flushPromises, mount, shallowMount } from '@vue/test-utils'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { nextTick, readonly, shallowRef } from 'vue'

import OnlinePlayersList from './OnlinePlayersList.vue'
import OnlinePlayersTable from './OnlinePlayersTable.vue'
import OnlinePlayersView from './OnlinePlayersView.vue'

const { authState, routerPushMock, routerReplaceMock, toastAddMock, useKickPlayerMock, useOnlinePlayersMock } = vi.hoisted(() => ({
  authState: { role: 'Owner' as 'Owner' | 'Admin' | 'Viewer' },
  routerPushMock: vi.fn(),
  routerReplaceMock: vi.fn(),
  toastAddMock: vi.fn(),
  useKickPlayerMock: vi.fn(),
  useOnlinePlayersMock: vi.fn(),
}))

vi.mock('@nuxt/ui/composables', async importOriginal => ({
  ...await importOriginal<typeof import('@nuxt/ui/composables')>(),
  useToast: () => ({ add: toastAddMock }),
}))

vi.mock('../model/useKickPlayer', () => ({
  useKickPlayer: useKickPlayerMock,
}))

vi.mock('../model/useOnlinePlayers', () => ({
  useOnlinePlayers: useOnlinePlayersMock,
}))

vi.mock('../../auth', () => ({
  useAuthStore: () => authState,
}))

vi.mock('vue-router', async importOriginal => ({
  ...await importOriginal<typeof import('vue-router')>(),
  useRouter: () => ({ push: routerPushMock, replace: routerReplaceMock }),
}))

const player: OnlinePlayer = {
  entityId: 7,
  name: 'Test Player',
  platformIdentity: {
    combinedId: 'Steam_76561198000000000',
    platform: 'Steam',
  },
  crossplatformIdentity: null,
  deviceType: 'windows',
  ip: '192.0.2.10',
  ping: 42,
  compatibilityVersion: 'V 3.0.1',
  discordUserId: '18446744073709551615',
  permissionLevel: 1000,
  position: { x: 100.5, y: 51, z: 200.25 },
  isDead: false,
  health: 93,
  maxHealth: 100,
  level: 18,
  playGroup: null,
  lastLoginUtc: null,
  gameStage: null,
  expToNextLevel: null,
  skillPoints: null,
  bedroll: null,
  score: 827,
  zombieKills: 317,
  playerKills: 2,
  deaths: 4,
  totalTimePlayedMinutes: 4823.5,
  distanceWalkedMeters: 127540.75,
  totalItemsCrafted: 2360,
  longestLifeMinutes: 920.25,
  currentLifeMinutes: 134.5,
  observedAtUtc: '2026-07-23T07:59:00Z',
}

interface ControllerValues {
  state?: OnlinePlayersState
  snapshot?: OnlinePlayersSnapshot | null
  errorCode?: OnlinePlayersErrorCode
  isRefreshing?: boolean
}

function mountOnlinePlayersView(values: ControllerValues = {}, kickOptions: {
  feedback?: KickPlayerFeedback | null
  response?: KickPlayerResponse | null
} = {}) {
  const refresh = vi.fn().mockResolvedValue(undefined)
  const stateRef = shallowRef<OnlinePlayersState>(values.state ?? 'loading')
  const errorCodeRef = shallowRef<OnlinePlayersErrorCode>(values.errorCode ?? null)
  const snapshotState = shallowRef<OnlinePlayersSnapshot | null>(values.snapshot ?? null)
  const feedback = shallowRef<KickPlayerFeedback | null>(kickOptions.feedback ?? null)
  const submit = vi.fn().mockImplementation(async () => {
    feedback.value = kickOptions.feedback ?? null
    return kickOptions.response ?? null
  })
  const controller: OnlinePlayersController = {
    state: readonly(stateRef),
    snapshot: readonly(snapshotState),
    errorCode: readonly(errorCodeRef),
    isRefreshing: readonly(shallowRef(values.isRefreshing ?? false)),
    refresh,
    dispose: vi.fn(),
  }
  useOnlinePlayersMock.mockReturnValue(controller)
  const kickController: KickPlayerController = {
    isSubmitting: readonly(shallowRef(false)),
    feedback: readonly(feedback),
    submit,
    clearFeedback: vi.fn(() => {
      feedback.value = null
    }),
    dispose: vi.fn(),
  }
  useKickPlayerMock.mockReturnValue(kickController)

  return {
    wrapper: mount(OnlinePlayersView, {
      global: {
        stubs: {
          DashboardSidebarCollapse: true,
          DashboardSidebarToggle: true,
          Icon: true,
          RouterLink: { props: ['to'], template: '<a :href="to"><slot /></a>' },
          Tooltip: { template: '<div><slot /></div>' },
          KickPlayerDialog: {
            props: ['open', 'player', 'isSubmitting', 'feedback'],
            emits: ['update:open', 'confirm', 'cancel'],
            template: `
              <section v-if="open" data-testid="kick-dialog">
                <span data-testid="kick-dialog-player">{{ player?.name }}</span>
                <span v-if="feedback" role="status">{{ feedback.code }}</span>
                <button data-testid="kick-dialog-confirm" @click="$emit('confirm', '违反服务器规则')">确认</button>
              </section>
            `,
          },
          OnlinePlayerDetailsSlideover: {
            props: ['open', 'player', 'unavailable', 'canKick', 'canOpenProfile'],
            emits: ['update:open', 'copyValue', 'kickPlayer', 'openProfile'],
            template: `
              <section v-if="open" data-testid="details-slideover">
                <span data-testid="details-player">{{ player?.name }}</span>
                <span data-testid="details-unavailable">{{ unavailable }}</span>
                <span data-testid="details-can-kick">{{ canKick }}</span>
                <span data-testid="details-can-open-profile">{{ canOpenProfile }}</span>
                <button data-testid="details-close" @click="$emit('update:open', false)">关闭</button>
                <button data-testid="details-kick" @click="$emit('kickPlayer', player)">踢出</button>
                <button v-if="canOpenProfile && player?.crossplatformIdentity" data-testid="details-profile" @click="$emit('openProfile', player.crossplatformIdentity.combinedId)">档案</button>
              </section>
            `,
          },
          UDashboardSidebarCollapse: true,
          UIcon: true,
          UTooltip: { template: '<div><slot /></div>' },
        },
      },
    }),
    errorCodeRef,
    refresh,
    snapshotState,
    stateRef,
    submit,
  }
}

function onePlayerSnapshot(overrides: Partial<OnlinePlayer> = {}): OnlinePlayersSnapshot {
  return { players: [{ ...player, ...overrides }] }
}

beforeEach(() => {
  authState.role = 'Owner'
  routerPushMock.mockReset()
  routerReplaceMock.mockReset()
  toastAddMock.mockReset()
  useKickPlayerMock.mockReset()
  useOnlinePlayersMock.mockReset()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

it('renders compact main list entries without complete identity or connection data', () => {
  const { wrapper } = mountOnlinePlayersView({ state: 'fresh', snapshot: onePlayerSnapshot() })

  expect(wrapper.getComponent(OnlinePlayersTable).text()).toContain('Test Player')
  expect(wrapper.getComponent(OnlinePlayersList).text()).toContain('93 / 100')
  expect(wrapper.text()).not.toContain('192.0.2.10')
  expect(wrapper.text()).not.toContain('Steam_76561198000000000')
})

it.each([
  ['desktop table', OnlinePlayersTable],
  ['mobile list', OnlinePlayersList],
] as const)('emits the complete selected player for details from the %s', (_, component) => {
  const wrapper = shallowMount(component, { props: { players: [player] } })

  wrapper.vm.$emit('viewDetails', player)

  expect(wrapper.emitted('viewDetails')).toEqual([[player]])
})

it('updates an open detail with the next fresh observation for the same key', async () => {
  const { wrapper, snapshotState } = mountOnlinePlayersView({
    state: 'fresh',
    snapshot: onePlayerSnapshot(),
  })

  wrapper.getComponent(OnlinePlayersTable).vm.$emit('viewDetails', player)
  await nextTick()
  snapshotState.value = onePlayerSnapshot({ name: 'Updated Player', score: 900 })
  await nextTick()

  expect(wrapper.get('[data-testid="details-player"]').text()).toBe('Updated Player')
  expect(wrapper.get('[data-testid="details-unavailable"]').text()).toBe('false')
})

it('locks the last detail observation unavailable after a successful refresh removes it', async () => {
  const { wrapper, snapshotState } = mountOnlinePlayersView({
    state: 'fresh',
    snapshot: onePlayerSnapshot(),
  })

  wrapper.getComponent(OnlinePlayersTable).vm.$emit('viewDetails', player)
  await nextTick()
  snapshotState.value = { players: [] }
  await nextTick()
  snapshotState.value = onePlayerSnapshot({ name: 'Reappeared Player' })
  await nextTick()

  expect(wrapper.get('[data-testid="details-player"]').text()).toBe('Test Player')
  expect(wrapper.get('[data-testid="details-unavailable"]').text()).toBe('true')
  expect(wrapper.get('[data-testid="details-can-kick"]').text()).toBe('false')
})

it('locks the last detail observation unavailable when the entity identity changes', async () => {
  const { wrapper, snapshotState } = mountOnlinePlayersView({
    state: 'fresh',
    snapshot: onePlayerSnapshot(),
  })

  wrapper.getComponent(OnlinePlayersTable).vm.$emit('viewDetails', player)
  await nextTick()
  snapshotState.value = onePlayerSnapshot({
    name: 'Replacement Player',
    platformIdentity: { combinedId: 'Steam_replacement', platform: 'Steam' },
  })
  await nextTick()

  expect(wrapper.get('[data-testid="details-player"]').text()).toBe('Test Player')
  expect(wrapper.get('[data-testid="details-unavailable"]').text()).toBe('true')
})

it('does not lock detail unavailable after a non-successful refresh state', async () => {
  const { wrapper, snapshotState, stateRef } = mountOnlinePlayersView({
    state: 'fresh',
    snapshot: onePlayerSnapshot(),
  })

  wrapper.getComponent(OnlinePlayersTable).vm.$emit('viewDetails', player)
  await nextTick()
  stateRef.value = 'stale'
  snapshotState.value = onePlayerSnapshot({ name: 'Last Success' })
  await nextTick()

  expect(wrapper.get('[data-testid="details-player"]').text()).toBe('Test Player')
  expect(wrapper.get('[data-testid="details-unavailable"]').text()).toBe('false')
  expect(wrapper.get('[data-testid="details-can-kick"]').text()).toBe('false')
})

it.each([
  ['stale', null],
  ['offline', null],
  ['forbidden', null],
  ['offline', 'game-not-ready'],
] as const)('disables detail kicks for %s state with %s error', async (state, errorCode) => {
  const { wrapper, errorCodeRef, stateRef } = mountOnlinePlayersView({
    state: 'fresh',
    snapshot: onePlayerSnapshot(),
  })

  wrapper.getComponent(OnlinePlayersTable).vm.$emit('viewDetails', player)
  await nextTick()
  errorCodeRef.value = errorCode
  stateRef.value = state
  await nextTick()

  expect(wrapper.get('[data-testid="details-can-kick"]').text()).toBe('false')
})

it('resets unavailable state when closed and reopened with a fresh player', async () => {
  const reappeared = { ...player, name: 'Reappeared Player' }
  const { wrapper, snapshotState } = mountOnlinePlayersView({
    state: 'fresh',
    snapshot: onePlayerSnapshot(),
  })

  wrapper.getComponent(OnlinePlayersTable).vm.$emit('viewDetails', player)
  await nextTick()
  snapshotState.value = { players: [] }
  await nextTick()
  await wrapper.get('[data-testid="details-close"]').trigger('click')
  snapshotState.value = { players: [reappeared] }
  await nextTick()
  wrapper.getComponent(OnlinePlayersTable).vm.$emit('viewDetails', reappeared)
  await nextTick()

  expect(wrapper.get('[data-testid="details-player"]').text()).toBe('Reappeared Player')
  expect(wrapper.get('[data-testid="details-unavailable"]').text()).toBe('false')
})

it('keeps the confirmation target fixed while detail refreshes or closes', async () => {
  const { wrapper, snapshotState, submit } = mountOnlinePlayersView({
    state: 'fresh',
    snapshot: onePlayerSnapshot(),
  })

  wrapper.getComponent(OnlinePlayersTable).vm.$emit('viewDetails', player)
  await nextTick()
  await wrapper.get('[data-testid="details-kick"]').trigger('click')
  snapshotState.value = onePlayerSnapshot({ name: 'Replacement Player' })
  await nextTick()
  await wrapper.get('[data-testid="details-close"]').trigger('click')
  await wrapper.get('[data-testid="kick-dialog-confirm"]').trigger('click')

  expect(wrapper.get('[data-testid="kick-dialog-player"]').text()).toBe('Test Player')
  expect(submit).toHaveBeenCalledWith(player, '违反服务器规则')
})

it('opens stable player profiles only for Owner', async () => {
  const stablePlayer = {
    ...player,
    crossplatformIdentity: { combinedId: 'EOS_profile/id', platform: 'EOS' },
  }
  const { wrapper } = mountOnlinePlayersView({
    state: 'fresh',
    snapshot: { players: [stablePlayer] },
  })

  wrapper.getComponent(OnlinePlayersTable).vm.$emit('viewDetails', stablePlayer)
  await nextTick()
  await wrapper.get('[data-testid="details-profile"]').trigger('click')

  expect(routerPushMock).toHaveBeenCalledWith('/players/profile/EOS_profile%2Fid')

  authState.role = 'Admin'
  const admin = mountOnlinePlayersView({
    state: 'fresh',
    snapshot: { players: [stablePlayer] },
  })
  admin.wrapper.getComponent(OnlinePlayersTable).vm.$emit('viewDetails', stablePlayer)
  await nextTick()
  expect(admin.wrapper.get('[data-testid="details-can-open-profile"]').text()).toBe('false')
  expect(admin.wrapper.find('[data-testid="details-profile"]').exists()).toBe(false)
})

it('closes, notifies and refreshes after a successful kick', async () => {
  const response: KickPlayerResponse = {
    operationId: '8f742dcfe65a454d8f919e164ace77d7',
    status: 'succeeded',
    target: { entityId: player.entityId, name: player.name, platformIdentity: player.platformIdentity },
    requestedAtUtc: '2026-07-22T08:00:00Z',
    completedAtUtc: '2026-07-22T08:00:00.100Z',
  }
  const { wrapper, refresh } = mountOnlinePlayersView({
    state: 'fresh',
    snapshot: onePlayerSnapshot(),
  }, { response })

  wrapper.getComponent(OnlinePlayersTable).vm.$emit('kickPlayer', player)
  await nextTick()
  await wrapper.get('[data-testid="kick-dialog-confirm"]').trigger('click')
  await flushPromises()

  expect(toastAddMock).toHaveBeenCalledWith({ title: '已踢出 Test Player', color: 'success' })
  expect(refresh).toHaveBeenCalledOnce()
  expect(wrapper.find('[data-testid="kick-dialog"]').exists()).toBe(false)
})

it('disables all future player kick actions after forbidden feedback', () => {
  const { wrapper } = mountOnlinePlayersView({
    state: 'fresh',
    snapshot: onePlayerSnapshot(),
  }, { feedback: { code: 'forbidden' } })

  expect(wrapper.getComponent(OnlinePlayersTable).props('canKick')).toBe(false)
})

it.each(['Admin', 'Viewer'] as const)('does not expose kick actions to %s when a stale player snapshot remains mounted', async (role) => {
  authState.role = role
  const { wrapper } = mountOnlinePlayersView({
    state: 'fresh',
    snapshot: onePlayerSnapshot(),
  })

  wrapper.getComponent(OnlinePlayersTable).vm.$emit('viewDetails', player)
  await nextTick()

  expect(wrapper.getComponent(OnlinePlayersTable).props('canKick')).toBe(false)
  expect(wrapper.get('[data-testid="details-can-kick"]').text()).toBe('false')
})

it('expires the session before redirecting and disables open detail kicks', async () => {
  const { wrapper } = mountOnlinePlayersView({
    state: 'fresh',
    snapshot: onePlayerSnapshot(),
  })
  wrapper.getComponent(OnlinePlayersTable).vm.$emit('viewDetails', player)
  await nextTick()

  const options = useOnlinePlayersMock.mock.calls[0]?.[0]
  options.onSessionExpired()
  await nextTick()

  expect(routerReplaceMock).toHaveBeenCalledWith({
    path: '/login',
    query: { redirect: '/players' },
  })
  expect(wrapper.get('[data-testid="details-can-kick"]').text()).toBe('false')
})

it('renders a loading skeleton before the first snapshot', () => {
  const { wrapper } = mountOnlinePlayersView()

  expect(wrapper.get('[data-testid="players-loading"]').attributes('aria-label')).toBe('正在加载在线玩家')
})

it.each([
  ['offline', null, '无法获取在线玩家'],
  ['offline', 'game-not-ready', '游戏仍在加载'],
  ['forbidden', null, '无权查看在线玩家'],
] as const)('renders %s state without player rows', (state, errorCode, message) => {
  const { wrapper } = mountOnlinePlayersView({ state, errorCode })

  expect(wrapper.text()).toContain(message)
  expect(wrapper.text()).not.toContain('Test Player')
})
