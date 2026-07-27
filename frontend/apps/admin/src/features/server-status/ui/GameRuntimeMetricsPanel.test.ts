import type { GameRuntimeMetrics } from '../model/overview'

import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import GameRuntimeMetricsPanel from './GameRuntimeMetricsPanel.vue'

const observedAtUtc = '2026-07-25T01:02:03Z'

function metric<T>(
  value: T | null,
  source: string,
  unit: string,
  warning: 'readFailed' | 'unsupported' | null = null,
) {
  return { value, source, unit, observedAtUtc, warning }
}

function runtimeMetrics(): GameRuntimeMetrics {
  return {
    gameDayTime: metric('Day 3, 12:30', 'World.worldTime', 'game-clock'),
    isBloodMoon: metric(false, 'World.aiDirector.BloodMoonComponent.BloodMoonActive', 'boolean'),
    framesPerSecond: metric(60.5, 'GameManager.frameTime', 'frames/second'),
    onlinePlayerCount: metric(0, 'World.Players.Count', 'count'),
    historicalPlayerCount: metric(10, 'GameManager.persistentPlayerCount', 'count'),
    animalCount: metric(4, 'World.Entities', 'count'),
    hostileEntityCount: metric(9, 'World.Entities', 'count'),
    activeEntityCount: metric(25, 'World.Entities', 'count'),
    chunkCount: metric(144, 'Chunk.InstanceCount', 'count'),
    droppedItemCount: metric(null, 'World.Entities', 'count', 'readFailed'),
    gameMemoryBytes: metric(null, 'GC.GetTotalMemory(false)', 'bytes', 'unsupported'),
  }
}

const stubs = {
  UAlert: {
    props: ['title', 'description'],
    template: '<div role="alert">{{ title }} {{ description }}<slot /></div>',
  },
  UBadge: { template: '<span><slot /></span>' },
  UCard: { template: '<section><slot name="header" /><slot /></section>' },
}

function render(
  metrics: GameRuntimeMetrics | null = runtimeMetrics(),
  availability: 'available' | 'stale' | 'unavailable' | 'forbidden' = 'available',
  stale = false,
) {
  return mount(GameRuntimeMetricsPanel, {
    props: { metrics, availability, stale },
    global: { stubs },
  })
}

describe('GameRuntimeMetricsPanel', () => {
  it('renders all eleven typed metrics with value, unit, source, time and warning', () => {
    const wrapper = render()

    expect(wrapper.findAll('[data-testid^="runtime-metric-"]')).toHaveLength(11)
    expect(wrapper.get('[data-testid="runtime-metric-onlinePlayerCount"]').text()).toContain('0')
    expect(wrapper.get('[data-testid="runtime-metric-onlinePlayerCount"]').text()).not.toContain('读取失败')
    expect(wrapper.get('[data-testid="runtime-metric-droppedItemCount"]').text()).toContain('读取失败')
    expect(wrapper.get('[data-testid="runtime-metric-gameMemoryBytes"]').text()).toContain('当前版本不支持')
    expect(wrapper.get('[data-testid="runtime-metric-framesPerSecond"]').text()).toContain('帧/秒')
    expect(wrapper.get('[data-testid="runtime-metric-framesPerSecond"]').text()).toContain('GameManager.frameTime')
    expect(wrapper.get('[data-testid="runtime-metric-framesPerSecond"]').text()).toContain('2026')
  })

  it('distinguishes an unavailable metric partition from a stale retained snapshot', () => {
    const unavailable = render(null, 'unavailable')
    expect(unavailable.get('[data-testid="runtime-metrics-unavailable"]').text()).toContain('运行指标不可用')
    expect(unavailable.find('[data-testid^="runtime-metric-"]').exists()).toBe(false)

    const stale = render(runtimeMetrics(), 'stale', true)
    expect(stale.get('[data-testid="runtime-metrics-stale"]').text()).toContain('上次成功采样')
    expect(stale.findAll('[data-testid^="runtime-metric-"]')).toHaveLength(11)
  })

  it('uses a wrapping narrow-screen layout without a horizontal-scroll table', () => {
    const wrapper = render()

    expect(wrapper.get('[data-testid="runtime-metrics-grid"]').classes()).toContain('grid-cols-1')
    expect(wrapper.html()).not.toContain('overflow-x-auto')
  })
})
