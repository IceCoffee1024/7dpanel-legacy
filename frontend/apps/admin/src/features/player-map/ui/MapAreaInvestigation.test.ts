import { mount } from '@vue/test-utils'
import { expect, it, vi } from 'vitest'

import { useAreaInvestigation } from '../model/useAreaInvestigation'
import MapAreaInvestigation from './MapAreaInvestigation.vue'

const response = {
  players: [{
    combinedId: 'EOS_ada',
    displayName: 'Ada',
    firstMatchingObservation: { observedAtUtc: '2026-07-25T08:00:00Z' },
    lastMatchingObservation: {
      observedAtUtc: '2026-07-25T08:15:00Z',
      position: { x: 10, y: 20, z: 30 },
    },
    matchingObservationCount: 3,
  }],
  candidateObservationCount: 12,
  matchingObservationCount: 3,
  truncated: true,
  truncation: { candidateObservations: true, playerResults: false },
} as const

function createController(request = async () => response) {
  return useAreaInvestigation({
    authorizationHeader: () => 'Bearer owner',
    limit: 50,
    request,
  })
}

it('delegates UTC search controls and controlled geometry events', async () => {
  const investigation = createController(() => new Promise<never>(() => {}))
  investigation.setRectangle(-10, -20, 30, 40)
  const setTimeRange = vi.spyOn(investigation, 'setTimeRange')
  const search = vi.spyOn(investigation, 'search')
  const cancel = vi.spyOn(investigation, 'cancel')

  const wrapper = mount(MapAreaInvestigation, {
    props: { investigation, mode: 'rectangle', limit: 50 },
  })

  await wrapper.get('[data-testid="area-mode-circle"]').trigger('click')
  expect(wrapper.emitted('update:mode')?.[0]).toEqual(['circle'])
  expect(wrapper.emitted('drawGeometry')?.[0]).toEqual(['circle'])

  await wrapper.get('[data-testid="area-modify"]').trigger('click')
  expect(wrapper.emitted('modifyGeometry')?.[0]).toEqual([investigation.geometry.value])

  await wrapper.get('[data-testid="area-from-utc"]').setValue('2026-07-25T08:00:00')
  await wrapper.get('[data-testid="area-to-utc"]').setValue('2026-07-25T09:00:00')
  await wrapper.get('[data-testid="area-limit"]').setValue('75')
  const updateLimitEvents = wrapper.emitted('update:limit')
  expect(updateLimitEvents?.[updateLimitEvents.length - 1]).toEqual([75])

  await wrapper.get('[data-testid="area-search"]').trigger('click')
  expect(setTimeRange).toHaveBeenCalledWith('2026-07-25T08:00:00.000Z', '2026-07-25T09:00:00.000Z')
  expect(search).toHaveBeenCalledOnce()

  await wrapper.get('[data-testid="area-cancel"]').trigger('click')
  expect(cancel).toHaveBeenCalledOnce()
})

it('renders bounded results and emits only readonly result navigation', async () => {
  const investigation = createController()
  investigation.setCircle(0, 0, 25)
  investigation.setTimeRange('2026-07-25T08:00:00Z', '2026-07-25T09:00:00Z')
  await investigation.search()

  const wrapper = mount(MapAreaInvestigation, {
    props: { investigation, mode: 'circle', limit: 50 },
  })

  expect(wrapper.text()).toContain('Ada')
  expect(wrapper.text()).toContain('EOS_ada')
  expect(wrapper.text()).toContain('X 10 · Y 20 · Z 30')
  expect(wrapper.text()).toContain('3')
  expect(wrapper.text()).toContain('12')
  expect(wrapper.text()).toContain('不证明持续停留')
  expect(wrapper.text()).toContain('候选观察已截断')

  await wrapper.get('[data-result-id="EOS_ada"]').trigger('click')
  expect(investigation.selectedCombinedId.value).toBe('EOS_ada')
  expect(wrapper.emitted('selectResult')?.[0]).toEqual(['EOS_ada'])

  await wrapper.get('[data-testid="area-profile-EOS_ada"]').trigger('click')
  await wrapper.get('[data-testid="area-track-EOS_ada"]').trigger('click')
  expect(wrapper.emitted('openHistoryProfile')?.[0]).toEqual(['EOS_ada'])
  expect(wrapper.emitted('loadHistoryTrack')?.[0]).toEqual(['EOS_ada'])

  await wrapper.get('[data-testid="area-clear"]').trigger('click')
  expect(investigation.geometry.value).toBeNull()
  expect(wrapper.emitted('clearGeometry')).toHaveLength(1)
})
