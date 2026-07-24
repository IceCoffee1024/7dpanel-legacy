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

import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { nextTick, readonly, shallowRef } from 'vue'

import OnlinePlayersList from './OnlinePlayersList.vue'
import OnlinePlayersTable from './OnlinePlayersTable.vue'
import OnlinePlayersView from './OnlinePlayersView.vue'

const { routerReplaceMock, toastAddMock, useKickPlayerMock, useOnlinePlayersMock } = vi.hoisted(() => ({
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

vi.mock('vue-router', async importOriginal => ({
  ...await importOriginal<typeof import('vue-router')>(),
  useRouter: () => ({ replace: routerReplaceMock }),
}))

const player: OnlinePlayer = {
  entityId: 7,
  name: 'Test Player',
  observedAtUtc: '2026-07-23T07:59:00Z',
  platformIdentity: {
    combinedId: 'Steam_76561198000000000',
    platform: 'Steam',
  },
  crossplatformIdentity: null,
  ping: 42,
  level: 18,
  health: 93,
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
  const dispose = vi.fn()
  const feedback = shallowRef<KickPlayerFeedback | null>(kickOptions.feedback ?? null)
  const snapshotState = shallowRef<OnlinePlayersSnapshot | null>(values.snapshot ?? null)
  const submit = vi.fn().mockImplementation(async () => {
    feedback.value = kickOptions.feedback ?? null
    return kickOptions.response ?? null
  })
  const controller: OnlinePlayersController = {
    state: readonly(shallowRef(values.state ?? 'loading')),
    snapshot: readonly(snapshotState),
    errorCode: readonly(shallowRef(values.errorCode ?? null)),
    isRefreshing: readonly(shallowRef(values.isRefreshing ?? false)),
    refresh,
    dispose,
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
          RouterLink: {
            props: ['to'],
            template: '<a :href="to"><slot /></a>',
          },
          Tooltip: {
            template: '<div><slot /></div>',
          },
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
          UDashboardSidebarCollapse: true,
          UIcon: true,
          UTooltip: {
            template: '<div><slot /></div>',
          },
        },
      },
    }),
    refresh,
    snapshotState,
    submit,
  }
}

function onePlayerSnapshot(overrides: Partial<OnlinePlayer> = {}): OnlinePlayersSnapshot {
  return {
    players: [{ ...player, ...overrides }],
  }
}

beforeEach(() => {
  routerReplaceMock.mockReset()
  toastAddMock.mockReset()
  useKickPlayerMock.mockReset()
  useOnlinePlayersMock.mockReset()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

it('renders the empty state without a fabricated capture time', () => {
  const { wrapper } = mountOnlinePlayersView({
    state: 'fresh',
    snapshot: { players: [] },
  })

  expect(wrapper.get('[data-testid="players-empty"]').text()).toContain('当前没有在线玩家')
  expect(wrapper.text()).not.toContain('捕获于')
})

it.each([
  ['desktop table', OnlinePlayersTable],
  ['mobile list', OnlinePlayersList],
] as const)('marks only old observations in the %s', (_, component) => {
  const { wrapper } = mountOnlinePlayersView({
    state: 'fresh',
    snapshot: onePlayerSnapshot({ observedAtUtc: '2000-01-01T00:00:00Z' }),
  })

  expect(wrapper.getComponent(component).text()).toContain('数据可能已过期')
  expect(wrapper.getComponent(component).text()).toContain('Test Player')
})

it.each([
  ['desktop table', OnlinePlayersTable],
  ['mobile list', OnlinePlayersList],
] as const)('shows each player observation time in the %s', (_, component) => {
  const { wrapper } = mountOnlinePlayersView({
    state: 'fresh',
    snapshot: onePlayerSnapshot({ observedAtUtc: '2026-07-23T08:00:00Z' }),
  })

  expect(wrapper.getComponent(component).text()).toContain('更新于')
  expect(wrapper.getComponent(component).text()).toContain('2026')
})

it('distinguishes a refresh failure from player observation age', () => {
  const { wrapper } = mountOnlinePlayersView({
    state: 'stale',
    snapshot: onePlayerSnapshot({ observedAtUtc: new Date().toISOString() }),
  })

  expect(wrapper.text()).toContain('刷新失败，显示上次结果')
  expect(wrapper.text()).not.toContain('数据可能已过期')
})

it.each([
  ['desktop table', OnlinePlayersTable],
  ['mobile list', OnlinePlayersList],
] as const)('uses the same fixed kick target from the %s', async (_, component) => {
  const { wrapper, snapshotState, submit } = mountOnlinePlayersView({
    state: 'fresh',
    snapshot: onePlayerSnapshot(),
  })

  wrapper.getComponent(component).vm.$emit('kickPlayer', player)
  await nextTick()
  snapshotState.value = onePlayerSnapshot({
    name: 'Replacement Player',
    platformIdentity: {
      combinedId: 'Steam_replacement',
      platform: 'Steam',
    },
  })
  await nextTick()
  await wrapper.get('[data-testid="kick-dialog-confirm"]').trigger('click')

  expect(wrapper.get('[data-testid="kick-dialog-player"]').text()).toBe('Test Player')
  expect(submit).toHaveBeenCalledWith(player, '违反服务器规则')
})

it('closes, notifies and refreshes after a successful kick', async () => {
  const response: KickPlayerResponse = {
    operationId: '8f742dcfe65a454d8f919e164ace77d7',
    status: 'succeeded',
    target: {
      entityId: player.entityId,
      name: player.name,
      platformIdentity: player.platformIdentity,
    },
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

it.each(['player_not_online', 'player_identity_changed'] as const)('refreshes without retrying after %s', async (code) => {
  const feedback: KickPlayerFeedback = { code }
  const { wrapper, refresh, submit } = mountOnlinePlayersView({
    state: 'fresh',
    snapshot: onePlayerSnapshot(),
  }, { feedback })

  wrapper.getComponent(OnlinePlayersTable).vm.$emit('kickPlayer', player)
  await nextTick()
  await wrapper.get('[data-testid="kick-dialog-confirm"]').trigger('click')
  await flushPromises()

  expect(submit).toHaveBeenCalledOnce()
  expect(refresh).toHaveBeenCalledOnce()
  expect(wrapper.find('[data-testid="kick-dialog"]').exists()).toBe(false)
})

it.each([
  'player_action_busy',
  'game_not_ready',
  'game_thread_timeout',
  'audit_unavailable',
  'unknown',
] as const)('keeps the fixed dialog open for %s feedback', async (code) => {
  const { wrapper, refresh } = mountOnlinePlayersView({
    state: 'fresh',
    snapshot: onePlayerSnapshot(),
  }, { feedback: { code } })

  wrapper.getComponent(OnlinePlayersTable).vm.$emit('kickPlayer', player)
  await nextTick()
  await wrapper.get('[data-testid="kick-dialog-confirm"]').trigger('click')
  await flushPromises()

  expect(wrapper.get('[data-testid="kick-dialog-player"]').text()).toBe('Test Player')
  expect(wrapper.get('[role="status"]').text()).toBe(code)
  expect(refresh).not.toHaveBeenCalled()
  expect(toastAddMock).not.toHaveBeenCalled()
})

it('uses the existing players redirect when the kick session expires', () => {
  mountOnlinePlayersView()
  const options = useKickPlayerMock.mock.calls[0]?.[0]

  options.onSessionExpired()

  expect(routerReplaceMock).toHaveBeenCalledWith({
    path: '/login',
    query: { redirect: '/players' },
  })
})

it('hides kick actions after forbidden feedback', () => {
  const { wrapper } = mountOnlinePlayersView({
    state: 'fresh',
    snapshot: onePlayerSnapshot(),
  }, { feedback: { code: 'forbidden' } })

  expect(wrapper.find('[aria-label="玩家操作：Test Player"]').exists()).toBe(false)
})

it('closes the fixed dialog after a submitted action becomes forbidden', async () => {
  const { wrapper, submit } = mountOnlinePlayersView({
    state: 'fresh',
    snapshot: onePlayerSnapshot(),
  }, { feedback: { code: 'forbidden' } })

  wrapper.getComponent(OnlinePlayersTable).vm.$emit('kickPlayer', player)
  await nextTick()
  await wrapper.get('[data-testid="kick-dialog-confirm"]').trigger('click')
  await flushPromises()

  expect(submit).toHaveBeenCalledOnce()
  expect(wrapper.find('[data-testid="kick-dialog"]').exists()).toBe(false)
  expect(wrapper.find('[aria-label="玩家操作：Test Player"]').exists()).toBe(false)
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

it('refreshes from the fixed icon button', async () => {
  const { wrapper, refresh } = mountOnlinePlayersView({
    state: 'fresh',
    snapshot: onePlayerSnapshot(),
  })

  const button = wrapper.get('[aria-label="刷新在线玩家"]')
  expect(button.classes()).toContain('size-8')
  await button.trigger('click')

  expect(refresh).toHaveBeenCalledOnce()
})

it('does not render unapproved snapshot fields', () => {
  const snapshot = {
    ...onePlayerSnapshot(),
    players: [{
      ...player,
      ip: '192.0.2.1',
      position: '100, 50, 200',
      banned: true,
      kills: 12,
      deaths: 3,
    }],
  } as unknown as OnlinePlayersSnapshot
  const { wrapper } = mountOnlinePlayersView({ state: 'fresh', snapshot })

  expect(wrapper.text()).not.toContain('192.0.2.1')
  expect(wrapper.text()).not.toContain('100, 50, 200')
  expect(wrapper.text()).not.toMatch(/封禁|击杀|死亡/)
})

it.each([
  ['desktop table', OnlinePlayersTable],
  ['mobile list', OnlinePlayersList],
] as const)('renders approved fields in the %s', (_, component) => {
  const detailedPlayer: OnlinePlayer = {
    ...player,
    crossplatformIdentity: {
      combinedId: 'EOS_0002aabb',
      platform: 'EOS',
    },
  }
  const wrapper = mount(component, {
    props: { players: [detailedPlayer] },
    global: {
      stubs: {
        Icon: true,
        UIcon: true,
      },
    },
  })

  expect(wrapper.text()).toContain('Test Player')
  expect(wrapper.text()).toContain('7')
  expect(wrapper.text()).toContain('Steam')
  expect(wrapper.text()).toContain('Steam_76561198000000000')
  expect(wrapper.text()).toContain('EOS')
  expect(wrapper.text()).toContain('EOS_0002aabb')
  expect(wrapper.text()).toContain('18')
  expect(wrapper.text()).toContain('93')
  expect(wrapper.text()).toContain('42')
  expect(wrapper.text()).not.toMatch(/IP|位置|封禁|击杀|死亡/)
})

it.each([
  ['desktop table', OnlinePlayersTable],
  ['mobile list', OnlinePlayersList],
] as const)('switches the %s to English without translating player identities', async (_, component) => {
  const detailedPlayer: OnlinePlayer = {
    ...player,
    crossplatformIdentity: {
      combinedId: 'EOS_0002aabb',
      platform: 'EOS',
    },
  }
  const wrapper = mount(component, {
    props: { players: [detailedPlayer] },
    global: {
      stubs: {
        Icon: true,
        UIcon: true,
      },
    },
  })

  wrapper.vm.$i18n.locale = 'en'
  await nextTick()

  expect(wrapper.text()).toContain('Updated')
  expect(wrapper.text()).toContain('Test Player')
  expect(wrapper.text()).toContain('Steam_76561198000000000')
  expect(wrapper.text()).toContain('EOS_0002aabb')
})

it.each([
  ['desktop table', OnlinePlayersTable],
  ['mobile list', OnlinePlayersList],
] as const)('labels a missing crossplatform identity in the %s', (_, component) => {
  const wrapper = mount(component, {
    props: { players: [player] },
    global: {
      stubs: {
        Icon: true,
        UIcon: true,
      },
    },
  })

  expect(wrapper.text()).toContain('未绑定')
})

it.each([
  ['desktop table', OnlinePlayersTable],
  ['mobile list', OnlinePlayersList],
] as const)('emits the selected player from the %s action menu', async (_, component) => {
  const wrapper = mount(component, {
    props: { players: [player] },
    global: {
      stubs: {
        DropdownMenu: {
          props: ['items'],
          template: `
            <div>
              <slot />
              <button
                data-testid="select-kick-player"
                :data-icon="items[0].icon"
                @click="items[0].onSelect()"
              >
                {{ items[0].label }}
              </button>
            </div>
          `,
        },
        Icon: true,
        UDropdownMenu: {
          props: ['items'],
          template: `
            <div>
              <slot />
              <button
                data-testid="select-kick-player"
                :data-icon="items[0].icon"
                @click="items[0].onSelect()"
              >
                {{ items[0].label }}
              </button>
            </div>
          `,
        },
        UIcon: true,
      },
    },
  })

  const actionButton = wrapper.get('[aria-label="玩家操作：Test Player"]')
  expect(actionButton.classes()).toContain('size-8')

  const kickItem = wrapper.get('[data-testid="select-kick-player"]')
  expect(kickItem.attributes('data-icon')).toBe('i-lucide-log-out')
  expect(kickItem.text()).toBe('踢出玩家')
  await kickItem.trigger('click')

  expect(wrapper.emitted('kickPlayer')).toEqual([[player]])
  expect(wrapper.emitted('consoleCommand')).toBeUndefined()
})

it('copies only the selected platform identity', async () => {
  const writeText = vi.fn().mockResolvedValue(undefined)
  vi.stubGlobal('navigator', {
    ...navigator,
    clipboard: { writeText },
  })
  const { wrapper } = mountOnlinePlayersView({
    state: 'fresh',
    snapshot: onePlayerSnapshot(),
  })

  await wrapper.get('[data-testid="copy-platform-identity-table-7"]').trigger('click')

  expect(writeText).toHaveBeenCalledOnce()
  expect(writeText).toHaveBeenCalledWith('Steam_76561198000000000')
})

it.each([
  ['clipboard API is unavailable', undefined],
  ['clipboard write rejects', { writeText: vi.fn().mockRejectedValue(new Error('NotAllowedError: permission denied')) }],
] as const)('shows a stable failure feedback when %s without an unhandled rejection', async (_, clipboard) => {
  vi.stubGlobal('navigator', clipboard === undefined ? {} : { clipboard })
  const { wrapper } = mountOnlinePlayersView({
    state: 'fresh',
    snapshot: onePlayerSnapshot(),
  })

  await expect(wrapper.get('[data-testid="copy-platform-identity-table-7"]').trigger('click')).resolves.toBeUndefined()

  const feedback = wrapper.get('[data-testid="copy-feedback"]')
  expect(feedback.attributes('role')).toBe('status')
  expect(feedback.text()).toBe('复制失败，请手动选择身份标识')
  expect(feedback.text()).not.toContain('Steam_76561198000000000')
  expect(feedback.text()).not.toContain('NotAllowedError')
  expect(feedback.text()).not.toMatch(/permission|权限|token/i)
})
