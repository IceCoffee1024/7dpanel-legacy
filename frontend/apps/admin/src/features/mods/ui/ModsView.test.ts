import type { ModsController } from '../model/useMods'

import { mount } from '@vue/test-utils'
import { readonly, shallowRef } from 'vue'
import { beforeEach, expect, it, vi } from 'vitest'

import ModsView from './ModsView.vue'

const { useModsMock } = vi.hoisted(() => ({ useModsMock: vi.fn() }))
vi.mock('../model/useMods', () => ({ useMods: useModsMock }))
vi.mock('vue-router', async importOriginal => ({
  ...await importOriginal<typeof import('vue-router')>(),
  useRouter: () => ({ replace: vi.fn() }),
}))
vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key: string) => ({
      'mods.title': '模组管理',
      'mods.search': '搜索模组',
      'mods.current.loaded': '当前已加载',
      'mods.current.unloaded': '当前未加载',
      'mods.current.unknown': '当前状态未知',
      'mods.next.enabled': '下次启动启用',
      'mods.next.disabled': '下次启动禁用',
      'mods.restartHint': '重启后生效',
      'mods.protected': '受保护模组',
      'mods.action.enable': '启用',
      'mods.action.disable': '禁用',
    } as Record<string, string>)[key] ?? key,
  }),
}))

const mod = {
  directoryId: 'Example', name: 'Example', displayName: 'Example', author: 'Author', version: '1',
  website: null, description: 'Description', isLoadedNow: true, isEnabledNextStart: false, isProtected: false,
}

function mountView(overrides: Partial<ModsController> = {}) {
  const controller = {
    state: readonly(shallowRef('fresh' as const)),
    mods: readonly(shallowRef([mod])),
    feedback: readonly(shallowRef(null)),
    canMutate: readonly(shallowRef(true)),
    changingDirectoryId: readonly(shallowRef(null)),
    refresh: vi.fn(), changeNextStart: vi.fn(), clearFeedback: vi.fn(), dispose: vi.fn(),
    ...overrides,
  } as ModsController
  useModsMock.mockReturnValue(controller)
  return mount(ModsView, {
    global: {
      stubs: {
        UDashboardPanel: { template: '<section><slot name="header"/><slot name="body"/></section>' },
        UDashboardNavbar: { template: '<header><slot name="right"/></header>' },
        UDashboardSidebarCollapse: true,
        UInput: { props: ['modelValue'], template: '<input data-testid="search" :value="modelValue" />' },
        UButton: { props: ['label'], template: '<button><slot/>{{ label }}</button>' },
        UBadge: { template: '<span><slot/></span>' },
        ModStateDialog: true,
      },
    },
  })
}

beforeEach(() => useModsMock.mockReset())

it('does not claim a disabled-next-start mod is unloaded now', () => {
  const wrapper = mountView()
  expect(wrapper.text()).toContain('当前已加载')
  expect(wrapper.text()).toContain('下次启动禁用')
  expect(wrapper.text()).toContain('重启后生效')
})

it('hides mutation for a protected mod', () => {
  const wrapper = mountView({ mods: readonly(shallowRef([{ ...mod, isProtected: true }])) })
  expect(wrapper.text()).toContain('受保护模组')
  expect(wrapper.text()).not.toContain('启用')
})
