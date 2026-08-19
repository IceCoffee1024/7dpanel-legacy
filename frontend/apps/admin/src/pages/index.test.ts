import type { GameRuntimeMetrics, OverviewSnapshot } from '../features/server-status/model/overview'

import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick, shallowRef } from 'vue'

import OverviewPage from './index.vue'

const status = shallowRef<'loading' | 'fresh' | 'partial' | 'stale' | 'offline'>('fresh')
const snapshot = shallowRef<OverviewSnapshot | null>(null)
const overviewError = shallowRef<{ code: 'network' } | null>(null)
const role = shallowRef<'Owner' | 'Admin' | 'Viewer' | null>('Owner')
const restartState = shallowRef<'idle' | 'confirming' | 'submitting' | 'accepted' | 'failed'>('idle')
const restartError = shallowRef<{ code: 'unknown' } | null>(null)
const shutdownState = shallowRef<'idle' | 'confirming' | 'submitting' | 'accepted' | 'failed'>('idle')
const shutdownError = shallowRef<{ code: 'unknown' } | null>(null)

const refresh = vi.fn()
const restartConfirm = vi.fn(() => Promise.resolve(null))
const shutdownConfirm = vi.fn(() => Promise.resolve(null))

vi.mock('../features/server-status/model/useOverview', () => ({
  useOverview: () => ({ error: overviewError, refresh, snapshot, status }),
}))
vi.mock('../features/server-operations/model/useRestartServer', () => ({
  useRestartServer: () => ({
    error: restartError,
    state: restartState,
    startConfirmation: () => { restartState.value = 'confirming' },
    cancelConfirmation: () => { restartState.value = 'idle' },
    confirm: restartConfirm,
  }),
}))
vi.mock('../features/server-operations/model/useShutdownServer', () => ({
  useShutdownServer: () => ({
    error: shutdownError,
    state: shutdownState,
    startConfirmation: () => { shutdownState.value = 'confirming' },
    cancelConfirmation: () => { shutdownState.value = 'idle' },
    confirm: shutdownConfirm,
  }),
}))
vi.mock('../features/auth', () => ({
  useAuthStore: () => ({ get role() { return role.value } }),
}))

function runtimeMetric<T>(
  value: T | null,
  unit: string,
  warning: 'readFailed' | 'unsupported' | null = null,
) {
  return {
    value,
    source: 'test fixture',
    unit,
    observedAtUtc: '2026-07-25T01:02:03Z',
    warning,
  }
}

function runtimeMetrics(): GameRuntimeMetrics {
  return {
    gameDayTime: runtimeMetric('Day 12, 18:30', 'game-clock'),
    isBloodMoon: runtimeMetric(false, 'boolean'),
    framesPerSecond: runtimeMetric(59.8, 'frames/second'),
    onlinePlayerCount: runtimeMetric(3, 'count'),
    historicalPlayerCount: runtimeMetric(27, 'count'),
    animalCount: runtimeMetric(null, 'count', 'unsupported'),
    hostileEntityCount: runtimeMetric(null, 'count', 'unsupported'),
    activeEntityCount: runtimeMetric(null, 'count', 'unsupported'),
    chunkCount: runtimeMetric(null, 'count', 'unsupported'),
    droppedItemCount: runtimeMetric(null, 'count', 'unsupported'),
    gameMemoryBytes: runtimeMetric(null, 'bytes', 'unsupported'),
  }
}

function overview(): OverviewSnapshot {
  return {
    availability: 'available',
    game: {
      availability: 'available',
      sampledAtUtc: '2026-07-25T01:02:03Z',
      gameTitle: 'Quiet Server',
      saveGameName: 'main-save',
      worldName: 'Navezgane',
      worldSessionUptimeSeconds: 7_380,
      version: 'V 2.4',
      gameMode: 'Survival',
      difficulty: 'Nomad',
      region: 'NorthAmericaEast',
      language: 'English',
      connectionAddress: '10.0.0.8',
      connectionPort: 26900,
      maximumPlayerCount: 8,
      runtimeMetrics: runtimeMetrics(),
    },
    host: {
      availability: 'available',
      identityAvailability: 'available',
      sampledAtUtc: '2026-07-25T01:02:03Z',
      processUptimeSeconds: 86_400,
      residentSetBytes: 3 * 1024 ** 3,
      managedHeapBytes: 512 * 1024 ** 2,
      otherMemoryBytes: 256 * 1024 ** 2,
      cpuUsagePercent: 18.2,
      operatingSystem: 'Windows Server 2022',
      operatingSystemVersion: '10.0.20348',
      processorCount: 16,
      memoryTotalBytes: 32 * 1024 ** 3,
      memoryAvailableBytes: 20 * 1024 ** 3,
      additionalMemory: { kind: 'virtualAddressSpace', totalBytes: 128 * 1024 ** 3, usedBytes: 6 * 1024 ** 3 },
      storageVolumes: [
        { name: 'system', rootPath: 'C:\\', totalBytes: 500 * 1024 ** 3, availableBytes: 200 * 1024 ** 3, isPrimaryDataVolume: false },
        { name: 'data', rootPath: 'D:\\', totalBytes: null, availableBytes: 40 * 1024 ** 3, isPrimaryDataVolume: true },
      ],
      publicNetwork: { availability: 'available', ipv4: '203.0.113.8', ipv6: '2001:db8::8' },
      deviceId: 'device-secret',
      currentSystemUser: 'svc-7dtd',
      osFamily: 'Windows',
      operatingSystemArchitecture: 'x64',
      runtimeVersion: '.NET Framework 4.8',
      cpuModel: 'Example CPU',
      logicalCoreCount: 16,
      cpuFrequencyMhz: 3600,
      deviceName: 'host-01',
      deviceModel: 'PowerEdge',
      deviceType: 'Server',
      processId: 4242,
      processStartedAtUtc: '2026-07-24T01:02:03Z',
    },
    restartPolicy: {
      availability: 'available',
      isConfigured: true,
      scheduleDescription: '0 4 * * * · Asia/Shanghai · warning 300s · save world · graceful · custom command configured · blood moon delay · retain 30d',
      nextRestartAtUtc: '2026-07-26T20:00:00Z',
    },
    recentActivity: {
      availability: 'available',
      sampledAtUtc: '2026-07-25T01:02:03Z',
      totalCount: 9,
      latestOccurredAtUtc: '2026-07-25T01:01:00Z',
      items: Array.from({ length: 9 }, (_, index) => ({
        occurredAtUtc: `2026-07-25T00:${String(50 + index).padStart(2, '0')}:00Z`,
        messageKey: index === 0 ? 'player_joined' : 'unknown_backend_code',
        messageArguments: index === 0
          ? { player: '<Ada>' }
          : {} as Readonly<Record<string, string>>,
      })),
    },
    attention: [{ code: 'low_disk_space' }],
  }
}

const stubs = {
  UDashboardPanel: { template: '<main><slot name="header"/><slot name="body"/></main>' },
  UDashboardNavbar: { props: ['title'], template: '<header>{{ title }}<slot name="leading"/></header>' },
  UDashboardSidebarCollapse: true,
  UCard: { template: '<section><slot name="header"/><slot/><slot name="body"/><slot name="footer"/></section>' },
  UBadge: { template: '<span><slot/></span>' },
  UAlert: { props: ['title', 'description'], template: '<div>{{ title }} {{ description }}<slot/></div>' },
  UProgress: { props: ['modelValue', 'max'], template: '<progress :value="modelValue" :max="max" />' },
  USkeleton: { template: '<span data-testid="skeleton" />' },
  UIcon: true,
  UButton: {
    props: ['label', 'disabled', 'loading'],
    emits: ['click'],
    template: '<button :disabled="disabled || loading" @click="$emit(\'click\')">{{ label }}<slot/></button>',
  },
  UModal: {
    props: ['open', 'title', 'description'],
    emits: ['update:open'],
    template: '<section v-if="open" role="dialog"><h2>{{ title }}</h2><p>{{ description }}</p><slot name="body"/><slot name="footer"/></section>',
  },
}

function render() {
  return mount(OverviewPage, { global: { stubs } })
}

beforeEach(() => {
  status.value = 'fresh'
  snapshot.value = overview()
  overviewError.value = null
  role.value = 'Owner'
  restartState.value = 'idle'
  restartError.value = null
  shutdownState.value = 'idle'
  shutdownError.value = null
})

describe('overview page', () => {
  it('shows loading skeletons and clear fresh, partial, stale, and offline states', async () => {
    status.value = 'loading'
    snapshot.value = null
    const wrapper = render()
    expect(wrapper.findAll('[data-testid="skeleton"]').length).toBeGreaterThan(2)

    status.value = 'fresh'
    snapshot.value = overview()
    await nextTick()
    expect(wrapper.text()).toContain('3 / 8')
    expect(wrapper.text()).toContain('59.8 FPS')
    expect(wrapper.text()).toContain('Navezgane')
    expect(wrapper.text()).toContain('Day 12, 18:30')
    expect(wrapper.text()).toContain('2小时 3分钟')
    expect(wrapper.text()).toContain('服务器标题')

    status.value = 'partial'
    await nextTick()
    expect(wrapper.text()).toContain('部分数据不可用')

    status.value = 'stale'
    await nextTick()
    expect(wrapper.text()).toContain('2026')

    status.value = 'offline'
    await nextTick()
    expect(wrapper.text()).toContain('无法获取服务器状态')
    await wrapper.get('[data-testid="overview-refresh"]').trigger('click')
    expect(refresh).toHaveBeenCalledOnce()
  })

  it('shows Owner-only sensitive host fields and operations without placeholders for non-owners', async () => {
    const wrapper = render()
    expect(wrapper.text()).toContain('203.0.113.8')
    expect(wrapper.text()).toContain('device-secret')
    expect(wrapper.find('[data-testid="restart-action"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="shutdown-action"]').exists()).toBe(true)

    role.value = 'Admin'
    await nextTick()
    expect(wrapper.text()).not.toContain('203.0.113.8')
    expect(wrapper.text()).not.toContain('device-secret')
    expect(wrapper.find('[data-testid="restart-action"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="shutdown-action"]').exists()).toBe(false)
  })

  it('labels Windows and Linux additional memory, renders every volume, and never invents an unknown limit', async () => {
    const wrapper = render()
    expect(wrapper.text()).toContain('虚拟地址空间')
    expect(wrapper.text()).toContain('system')
    expect(wrapper.text()).toContain('data')
    expect(wrapper.findAll('[role="progressbar"]')).toHaveLength(3)

    snapshot.value = {
      ...overview(),
      host: {
        ...overview().host,
        osFamily: 'Linux',
        additionalMemory: { kind: 'swap', totalBytes: 8 * 1024 ** 3, usedBytes: 1024 ** 3 },
      },
    }
    await nextTick()
    expect(wrapper.text()).toContain('交换空间')
  })

  it('shows a stable activity empty state, caps activity at eight, and does not expose policy command text', async () => {
    const wrapper = render()
    expect(wrapper.findAll('[data-testid="activity-item"]')).toHaveLength(8)
    expect(wrapper.text()).toContain('<Ada>')
    expect(wrapper.html()).not.toContain('&lt;Ada&gt;</span><script')
    expect(wrapper.text()).toContain('下次重启')
    expect(wrapper.text()).not.toContain('custom command configured')

    snapshot.value = {
      ...overview(),
      recentActivity: { ...overview().recentActivity, items: [], totalCount: 0 },
    }
    await nextTick()
    expect(wrapper.text()).toContain('暂无最近活动')
  })

  it('keeps restart and shutdown confirmations independent and disables only the submitting action', async () => {
    const wrapper = render()
    await wrapper.get('[data-testid="restart-action"]').trigger('click')
    expect(wrapper.find('[data-testid="restart-dialog"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('不保证服务器能够成功重启')

    await wrapper.get('[data-testid="shutdown-action"]').trigger('click')
    expect(wrapper.find('[data-testid="restart-dialog"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="shutdown-dialog"]').exists()).toBe(true)

    restartState.value = 'submitting'
    await nextTick()
    expect(wrapper.get('[data-testid="restart-confirm"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-testid="shutdown-confirm"]').attributes('disabled')).toBeUndefined()

    await wrapper.get('[data-testid="shutdown-cancel"]').trigger('click')
    expect(wrapper.find('[data-testid="shutdown-dialog"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="restart-dialog"]').exists()).toBe(true)
  })

  it('uses distinct accepted and stable failed copy for restart and shutdown', async () => {
    const wrapper = render()
    restartState.value = 'accepted'
    shutdownState.value = 'accepted'
    await nextTick()
    expect(wrapper.text()).toContain('重启脚本已启动')
    expect(wrapper.text()).toContain('关服请求已接受')
    expect(wrapper.text()).not.toContain('服务器重启成功')

    restartState.value = 'failed'
    shutdownState.value = 'failed'
    restartError.value = { code: 'unknown' }
    shutdownError.value = { code: 'unknown' }
    await nextTick()
    expect(wrapper.text()).toContain('无法启动重启脚本，请稍后重试')
    expect(wrapper.text()).toContain('无法提交关服请求，请稍后重试')
  })
})
