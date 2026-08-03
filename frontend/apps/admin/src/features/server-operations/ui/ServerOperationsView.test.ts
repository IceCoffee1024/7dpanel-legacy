import type { OverviewSnapshot } from '../../server-status/model/overview'

import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick, shallowRef } from 'vue'

import ServerOperationsView from './ServerOperationsView.vue'

const status = shallowRef<'loading' | 'fresh' | 'partial' | 'stale' | 'offline'>('fresh')
const snapshot = shallowRef<OverviewSnapshot | null>(null)
const role = shallowRef<'Owner' | 'Admin' | 'Viewer' | null>('Owner')
const restartState = shallowRef<'idle' | 'confirming' | 'submitting' | 'accepted' | 'failed'>('idle')
const restartError = shallowRef<{ code: 'unknown' } | null>(null)
const shutdownState = shallowRef<'idle' | 'confirming' | 'submitting' | 'accepted' | 'failed'>('idle')
const shutdownError = shallowRef<{ code: 'unknown' } | null>(null)

const refresh = vi.fn()
const restartConfirm = vi.fn(() => Promise.resolve(null))
const shutdownConfirm = vi.fn(() => Promise.resolve(null))

vi.mock('../../server-status/model/useOverview', () => ({
  useOverview: () => ({ snapshot, status, refresh }),
}))
vi.mock('../model/useRestartServer', () => ({
  useRestartServer: () => ({
    error: restartError,
    state: restartState,
    startConfirmation: () => { restartState.value = 'confirming' },
    cancelConfirmation: () => { restartState.value = 'idle' },
    confirm: restartConfirm,
  }),
}))
vi.mock('../model/useShutdownServer', () => ({
  useShutdownServer: () => ({
    error: shutdownError,
    state: shutdownState,
    startConfirmation: () => { shutdownState.value = 'confirming' },
    cancelConfirmation: () => { shutdownState.value = 'idle' },
    confirm: shutdownConfirm,
  }),
}))
vi.mock('../../auth', () => ({
  useAuthStore: () => ({ get role() { return role.value } }),
}))

function overview(): OverviewSnapshot {
  return {
    availability: 'available',
    game: {
      availability: 'available',
      sampledAtUtc: '2026-08-03T01:02:03Z',
      gameTitle: null,
      saveGameName: null,
      worldName: null,
      worldSessionUptimeSeconds: null,
      version: null,
      gameMode: null,
      difficulty: null,
      region: null,
      language: null,
      connectionAddress: null,
      connectionPort: null,
      maximumPlayerCount: null,
      runtimeMetrics: null,
    },
    host: {
      availability: 'available',
      identityAvailability: 'available',
      sampledAtUtc: '2026-08-03T01:02:03Z',
      processUptimeSeconds: null,
      residentSetBytes: null,
      managedHeapBytes: null,
      otherMemoryBytes: null,
      cpuUsagePercent: null,
      operatingSystem: null,
      operatingSystemVersion: null,
      processorCount: null,
      memoryTotalBytes: null,
      memoryAvailableBytes: null,
      storageVolumes: [],
      publicNetwork: { availability: 'available' },
      osFamily: null,
      operatingSystemArchitecture: null,
      runtimeVersion: null,
      cpuModel: null,
      logicalCoreCount: null,
      cpuFrequencyMhz: null,
      deviceName: null,
      deviceModel: null,
      deviceType: null,
      processId: null,
      processStartedAtUtc: null,
    },
    restartPolicy: { availability: 'available', isConfigured: true, scheduleDescription: '0 4 * * *', nextRestartAtUtc: null },
    recentActivity: { availability: 'available', sampledAtUtc: null, totalCount: 0, latestOccurredAtUtc: null, items: [] },
    attention: [],
  }
}

const stubs = {
  UAlert: { props: ['title', 'description'], template: '<div>{{ title }} {{ description }}<slot /></div>' },
  UBadge: { template: '<span><slot /></span>' },
  UButton: { props: ['label', 'disabled', 'loading'], emits: ['click'], template: '<button :disabled="disabled || loading" @click="$emit(\'click\')">{{ label }}<slot /></button>' },
  UCard: { template: '<section><slot name="header" /><slot /></section>' },
  UIcon: true,
  UModal: { props: ['open', 'title', 'description'], emits: ['update:open'], template: '<section v-if="open" role="dialog"><h2>{{ title }}</h2><p>{{ description }}</p><slot name="body" /><slot name="footer" /></section>' },
  USkeleton: true,
}

function render() {
  return mount(ServerOperationsView, { global: { stubs } })
}

beforeEach(() => {
  status.value = 'fresh'
  snapshot.value = overview()
  role.value = 'Owner'
  restartState.value = 'idle'
  restartError.value = null
  shutdownState.value = 'idle'
  shutdownError.value = null
})

describe('serverOperationsView', () => {
  it('composes the existing status summary and restart policy without duplicating server state', () => {
    const wrapper = render()

    expect(wrapper.find('[data-testid="overview-refresh"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('重启策略')
  })

  it('preserves independent restart and shutdown confirmations for an Owner', async () => {
    const wrapper = render()
    await wrapper.get('[data-testid="restart-action"]').trigger('click')
    await wrapper.get('[data-testid="shutdown-action"]').trigger('click')

    expect(wrapper.find('[data-testid="restart-dialog"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="shutdown-dialog"]').exists()).toBe(true)

    restartState.value = 'submitting'
    await nextTick()
    expect(wrapper.get('[data-testid="restart-confirm"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-testid="shutdown-confirm"]').attributes('disabled')).toBeUndefined()
  })

  it('does not render destructive controls or dialogs for non-owners', async () => {
    const wrapper = render()
    role.value = 'Admin'
    await nextTick()

    expect(wrapper.find('[data-testid="restart-action"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="shutdown-action"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="restart-dialog"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="shutdown-dialog"]').exists()).toBe(false)
  })
})
