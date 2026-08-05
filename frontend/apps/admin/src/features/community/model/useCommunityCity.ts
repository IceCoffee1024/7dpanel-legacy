import type { ShallowRef } from 'vue'

import type { City, CityInput } from '../api/community'

import type { CommunityLoader } from './community-loader'
import type { CommunityMutationController } from './community-mutation'
import type { CommunityViewState, UseCommunityOptions } from './community.types'
import { shallowRef } from 'vue'
import * as api from '../api/community'

export interface CommunityCityController {
  readonly state: ShallowRef<CommunityViewState>
  readonly cities: ShallowRef<readonly City[]>
  readonly fullState: ShallowRef<CommunityViewState>
  readonly fullCities: ShallowRef<readonly City[]>
  load: () => Promise<void>
  loadAll: () => Promise<void>
  save: (input: CityInput) => Promise<boolean>
}

export function useCommunityCity(
  options: UseCommunityOptions,
  loader: CommunityLoader,
  mutation: CommunityMutationController,
): CommunityCityController {
  const state = shallowRef<CommunityViewState>('idle')
  const cities = shallowRef<readonly City[]>(Object.freeze([]))
  const fullState = shallowRef<CommunityViewState>('unavailable')
  const fullCities = shallowRef<readonly City[]>(Object.freeze([]))

  function load(): Promise<void> {
    return loader.load(
      'cities',
      state,
      () => cities.value.length > 0,
      options.fetchCities ?? api.fetchCities,
      (value) => {
        cities.value = value
        return value.length
      },
    )
  }

  function loadAll(): Promise<void> {
    return loader.load(
      'all-cities',
      fullState,
      () => fullCities.value.length > 0,
      options.fetchAllCities ?? api.fetchAllCities,
      (value) => {
        fullCities.value = value
        return value.length
      },
    )
  }

  function save(input: CityInput): Promise<boolean> {
    return mutation.mutate(
      { kind: 'city', id: input.cityId },
      (token, signal) => (options.upsertCity ?? api.upsertCity)(token, input, signal),
      (authoritative) => {
        const updateProjection = (values: readonly City[]) => {
          const remaining = values.filter(value => value.cityId !== authoritative.cityId)
          return Object.freeze(authoritative.enabled ? [...remaining, authoritative] : remaining)
        }
        cities.value = updateProjection(cities.value)
        state.value = cities.value.length === 0 ? 'empty' : 'ready'
        if (fullState.value === 'ready' || fullState.value === 'empty') {
          fullCities.value = updateProjection(fullCities.value)
          fullState.value = fullCities.value.length === 0 ? 'empty' : 'ready'
        }
        else {
          loader.invalidate('all-cities', fullState)
          fullCities.value = Object.freeze([])
        }
      },
    )
  }

  return { state, cities, fullState, fullCities, load, loadAll, save }
}
