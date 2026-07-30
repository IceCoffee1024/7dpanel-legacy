import type { AccessListsController } from '../model/useAccessLists'

import { mount } from '@vue/test-utils'
import { beforeEach, expect, it, vi } from 'vitest'
import { readonly, shallowRef } from 'vue'

import AccessListsView from './AccessListsView.vue'

const { useAccessListsMock } = vi.hoisted(() => ({ useAccessListsMock: vi.fn() }))
vi.mock('../model/useAccessLists', () => ({ useAccessLists: useAccessListsMock }))
vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {} }),
  useRouter: () => ({ replace: vi.fn() }),
}))

function mountView(role: 'Owner' | 'Viewer') {
  const controller = {
    banState: readonly(shallowRef('fresh')),
    whitelistState: readonly(shallowRef('fresh')),
    bans: readonly(shallowRef([{ playerId: 'EOS_1', displayName: 'Player', bannedUntilUtc: null, reason: 'reason' }])),
    whitelist: readonly(shallowRef([])),
    canMutate: readonly(shallowRef(role !== 'Viewer')),
    mutationTarget: readonly(shallowRef(null)),
    refreshBans: vi.fn(),
    refreshWhitelist: vi.fn(),
    saveBan: vi.fn(),
    removeBan: vi.fn(),
    saveWhitelist: vi.fn(),
    removeWhitelist: vi.fn(),
    dispose: vi.fn(),
  } as unknown as AccessListsController
  useAccessListsMock.mockReturnValue(controller)
  return mount(AccessListsView, {
    global: { stubs: { UDashboardPanel: { template: '<section><slot name="header"/><slot name="body"/></section>' }, BanDialog: true, WhitelistDialog: true } },
  })
}

beforeEach(() => useAccessListsMock.mockReset())

it('keeps viewer read-only and never renders bulk actions', () => {
  const wrapper = mountView('Viewer')
  expect(wrapper.text()).toContain('Player')
  expect(wrapper.find('[data-testid="add-ban"]').exists()).toBe(false)
  expect(wrapper.find('[data-testid="bulk-delete"]').exists()).toBe(false)
})

it('labels permanent bans explicitly and gives editors single-item actions', () => {
  const wrapper = mountView('Owner')
  expect(wrapper.text()).toContain('永久')
  wrapper.get('[data-testid="add-ban"]')
  wrapper.get('[data-testid="edit-ban-EOS_1"]')
  expect(wrapper.find('[data-testid="bulk-delete"]').exists()).toBe(false)
})
