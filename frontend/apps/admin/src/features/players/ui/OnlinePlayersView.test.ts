import type {
  OnlinePlayer,
  OnlinePlayersController,
  OnlinePlayersErrorCode,
  OnlinePlayersSnapshot,
  OnlinePlayersState,
} from '..'

import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { readonly, shallowRef } from 'vue'

import OnlinePlayersList from './OnlinePlayersList.vue'
import OnlinePlayersTable from './OnlinePlayersTable.vue'
import OnlinePlayersView from './OnlinePlayersView.vue'

const { useOnlinePlayersMock } = vi.hoisted(() => ({
  useOnlinePlayersMock: vi.fn(),
}))

vi.mock('../model/useOnlinePlayers', () => ({
  useOnlinePlayers: useOnlinePlayersMock,
}))

const player: OnlinePlayer = {
  entityId: 7,
  name: 'Test Player',
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

function mountOnlinePlayersView(values: ControllerValues = {}) {
  const refresh = vi.fn().mockResolvedValue(undefined)
  const dispose = vi.fn()
  const controller: OnlinePlayersController = {
    state: readonly(shallowRef(values.state ?? 'loading')),
    snapshot: readonly(shallowRef(values.snapshot ?? null)),
    errorCode: readonly(shallowRef(values.errorCode ?? null)),
    isRefreshing: readonly(shallowRef(values.isRefreshing ?? false)),
    refresh,
    dispose,
  }
  useOnlinePlayersMock.mockReturnValue(controller)

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
          UDashboardSidebarCollapse: true,
          UIcon: true,
          UTooltip: {
            template: '<div><slot /></div>',
          },
        },
      },
    }),
    refresh,
  }
}

function onePlayerSnapshot(overrides: Partial<OnlinePlayer> = {}): OnlinePlayersSnapshot {
  return {
    capturedAtUtc: '2026-07-21T00:00:00Z',
    players: [{ ...player, ...overrides }],
  }
}

beforeEach(() => {
  useOnlinePlayersMock.mockReset()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

it('renders the empty state with capture time', () => {
  const { wrapper } = mountOnlinePlayersView({
    state: 'fresh',
    snapshot: { capturedAtUtc: '2026-07-21T00:00:00Z', players: [] },
  })

  expect(wrapper.get('[data-testid="players-empty"]').text()).toContain('当前没有在线玩家')
  expect(wrapper.text()).toContain('2026')
})

it('keeps player rows visible while stale', () => {
  const { wrapper } = mountOnlinePlayersView({
    state: 'stale',
    snapshot: onePlayerSnapshot(),
  })

  expect(wrapper.text()).toContain('数据已过期')
  expect(wrapper.text()).toContain('Test Player')
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
