import type { PlayerSnapshot } from '../api/playerSnapshot'

import { mount } from '@vue/test-utils'
import { expect, it } from 'vitest'

import PlayerSnapshotDetails from './PlayerSnapshotDetails.vue'

const player: PlayerSnapshot = {
  entityId: 7,
  name: 'Test Player',
  platformIdentity: { combinedId: 'Steam_76561198000000000', platform: 'Steam' },
  crossplatformIdentity: { combinedId: 'EOS_12345678901234567', platform: 'EOS' },
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
  playGroup: 'Survivalists',
  lastLoginUtc: '2026-07-23T07:00:00Z',
  gameStage: 143,
  expToNextLevel: 1200,
  skillPoints: 4,
  bedroll: { x: 100, y: 70, z: 200 },
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

it('renders all five read-only snapshot sections including history-only fields', () => {
  const wrapper = mount(PlayerSnapshotDetails, { props: { player } })

  expect(wrapper.text()).toContain('身份')
  expect(wrapper.text()).toContain('连接')
  expect(wrapper.text()).toContain('当前状态')
  expect(wrapper.text()).toContain('进度')
  expect(wrapper.text()).toContain('累计统计')
  expect(wrapper.text()).toContain('Survivalists')
  expect(wrapper.text()).toContain('143')
  expect(wrapper.text()).toContain('1,200')
  expect(wrapper.text()).toContain('100, 70, 200')
  expect(wrapper.findAll('button')).toHaveLength(0)
})

it('distinguishes unknown snapshot values from an unset bedroll', () => {
  const wrapper = mount(PlayerSnapshotDetails, {
    props: {
      player: {
        ...player,
        playGroup: null,
        lastLoginUtc: null,
        gameStage: null,
        expToNextLevel: null,
        skillPoints: null,
        bedroll: null,
      },
    },
  })

  expect(wrapper.text()).toContain('未知')
  expect(wrapper.text()).toContain('未设置')
})
