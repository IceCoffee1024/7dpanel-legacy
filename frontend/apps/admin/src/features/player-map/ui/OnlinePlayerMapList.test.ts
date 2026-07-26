import { mount } from '@vue/test-utils'
import { expect, it } from 'vitest'

import OnlinePlayerMapList from './OnlinePlayerMapList.vue'

const players = [
  { combinedId: 'EOS_ada', name: 'Ada', position: { x: 10, y: 20, z: 30 }, observedAtUtc: '2026-07-26T08:00:00Z' },
  { combinedId: 'EOS_grace', name: 'Grace', position: { x: 40, y: 50, z: 60 }, observedAtUtc: '2026-07-26T08:01:00Z' },
]

it('shows synchronized online details and emits a keyboard selection', async () => {
  const wrapper = mount(OnlinePlayerMapList, {
    props: { players, selectedCombinedId: 'EOS_ada' },
  })

  expect(wrapper.text()).toContain('Ada')
  expect(wrapper.text()).toContain('X 10 · Y 20 · Z 30')
  expect(wrapper.text()).toContain('2026')
  expect(wrapper.get('[data-player-id="EOS_ada"]').attributes('aria-pressed')).toBe('true')

  await wrapper.get('[data-player-id="EOS_grace"]').trigger('click')
  expect(wrapper.emitted('select')?.[0]).toEqual(['EOS_grace'])
})
