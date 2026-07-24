import type { OnlinePlayer } from '..'

import { mount } from '@vue/test-utils'
import { expect, it } from 'vitest'

import OnlinePlayerDetailsSlideover from './OnlinePlayerDetailsSlideover.vue'

const player: OnlinePlayer = {
  entityId: 7,
  name: 'Test Player',
  platformIdentity: {
    combinedId: 'Steam_76561198000000000',
    platform: 'Steam',
  },
  crossplatformIdentity: {
    combinedId: 'EOS_12345678901234567',
    platform: 'EOS',
  },
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

const slideoverStub = {
  props: ['open', 'title', 'description'],
  emits: ['update:open'],
  template: `
    <section v-if="open" role="dialog" :aria-label="title">
      <slot name="body" />
      <slot name="footer" />
    </section>
  `,
}

const buttonStub = {
  inheritAttrs: false,
  props: ['label'],
  emits: ['click'],
  template: '<button v-bind="$attrs" @click="$emit(\'click\')"><slot />{{ label }}</button>',
}

function mountSlideover(options: {
  player?: OnlinePlayer | null
  unavailable?: boolean
  canKick?: boolean
} = {}) {
  return mount(OnlinePlayerDetailsSlideover, {
    props: {
      'open': true,
      'player': options.player === undefined ? player : options.player,
      'unavailable': options.unavailable ?? false,
      'canKick': options.canKick ?? true,
      'onUpdate:open': () => {},
    },
    global: {
      stubs: {
        Alert: { props: ['title', 'description'], template: '<div role="alert"><strong>{{ title }}</strong><span>{{ description }}</span></div>' },
        Button: buttonStub,
        Slideover: slideoverStub,
        UAlert: { props: ['title', 'description'], template: '<div role="alert"><strong>{{ title }}</strong><span>{{ description }}</span></div>' },
        UButton: buttonStub,
        USlideover: slideoverStub,
      },
    },
  })
}

it('renders all observation sections and full field values', () => {
  const wrapper = mountSlideover()

  expect(wrapper.get('[role="dialog"]').attributes('aria-label')).toContain('Test Player')
  expect(wrapper.get('[role="dialog"]').attributes('aria-label')).toContain('存活')
  expect(wrapper.text()).toContain('身份')
  expect(wrapper.text()).toContain('连接')
  expect(wrapper.text()).toContain('当前状态')
  expect(wrapper.text()).toContain('累计统计')
  expect(wrapper.text()).toContain('Steam · Steam_76561198000000000')
  expect(wrapper.text()).toContain('EOS · EOS_12345678901234567')
  expect(wrapper.text()).toContain('18446744073709551615')
  expect(wrapper.text()).toContain('192.0.2.10')
  expect(wrapper.text()).toContain('101, 51, 200')
  expect(wrapper.text()).toContain('127,541')
  expect(wrapper.text()).toContain('3 天 8 小时 24 分钟')
  expect(wrapper.text()).toContain('15 小时 20 分钟')
  expect(wrapper.text()).toContain('2 小时 15 分钟')
})

it('shows unknown and hides optional copy actions for null transport values', () => {
  const wrapper = mountSlideover({
    player: {
      ...player,
      crossplatformIdentity: null,
      ip: null,
      compatibilityVersion: null,
      discordUserId: null,
    },
  })

  expect(wrapper.text()).toContain('未知')
  expect(wrapper.find('[aria-label="复制跨平台身份"]').exists()).toBe(false)
  expect(wrapper.find('[aria-label="复制 Discord 用户 ID"]').exists()).toBe(false)
  expect(wrapper.find('[aria-label="复制 IP 地址"]').exists()).toBe(false)
})

it('copies only the explicitly selected identity or connection value', async () => {
  const wrapper = mountSlideover()

  await wrapper.get('[aria-label="复制平台身份"]').trigger('click')
  await wrapper.get('[aria-label="复制 Discord 用户 ID"]').trigger('click')
  await wrapper.get('[aria-label="复制 IP 地址"]').trigger('click')

  expect(wrapper.emitted('copyValue')).toEqual([
    ['Steam_76561198000000000'],
    ['18446744073709551615'],
    ['192.0.2.10'],
  ])
})

it('locks dangerous actions and explains the unavailable observation', () => {
  const wrapper = mountSlideover({ unavailable: true })

  expect(wrapper.get('[role="alert"]').text()).toContain('该玩家观察已不可用')
  expect(wrapper.findAll('button').some(button => button.text() === '取消')).toBe(true)
  expect(wrapper.findAll('button').some(button => button.text() === '踢出玩家')).toBe(false)
})

it('does not render a dangerous action when kicking is not allowed', () => {
  const wrapper = mountSlideover({ canKick: false })

  expect(wrapper.findAll('button').some(button => button.text() === '踢出玩家')).toBe(false)
})

it('emits the fixed observation only when kicking is allowed', async () => {
  const wrapper = mountSlideover()
  const kickButton = wrapper.findAll('button').find(button => button.text() === '踢出玩家')

  expect(kickButton).toBeDefined()
  await kickButton!.trigger('click')

  expect(wrapper.emitted('kickPlayer')).toEqual([[player]])
})

it('closes through the controlled open model', async () => {
  const wrapper = mountSlideover()
  const closeButton = wrapper.findAll('button').find(button => button.text() === '取消')

  expect(closeButton).toBeDefined()
  await closeButton!.trigger('click')

  expect(wrapper.emitted('update:open')).toEqual([[false]])
})
