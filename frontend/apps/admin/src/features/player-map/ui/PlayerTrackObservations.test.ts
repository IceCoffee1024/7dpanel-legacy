import { mount } from '@vue/test-utils'
import { expect, it } from 'vitest'

import PlayerTrackObservations from './PlayerTrackObservations.vue'

const segments = [{ points: [
  { snapshotId: 1, name: 'Ada', x: 1, y: 2, z: 3, observedAtUtc: '2026-07-26T08:00:00Z' },
  { snapshotId: 2, name: 'Ada', x: 4, y: 5, z: 6, observedAtUtc: '2026-07-26T08:10:00Z' },
] }]

it('provides a keyboard-selectable synchronized observation list', async () => {
  const wrapper = mount(PlayerTrackObservations, {
    props: { segments, selectedSnapshotId: 1 },
  })

  expect(wrapper.findAll('button')).toHaveLength(2)
  await wrapper.findAll('button')[1]?.trigger('click')
  expect(wrapper.emitted('select')?.[0]).toEqual([2])
})
