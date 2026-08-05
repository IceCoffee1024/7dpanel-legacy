import type { ShallowRef } from 'vue'

import type { FriendshipRecord, FriendshipStatus } from '../api/community'

import type { CommunityLoader } from './community-loader'
import type { CommunityViewState, UseCommunityOptions } from './community.types'
import { shallowRef } from 'vue'
import * as api from '../api/community'

export interface CommunityFriendshipController {
  readonly state: ShallowRef<CommunityViewState>
  readonly data: ShallowRef<FriendshipStatus | null>
  readonly recordsState: ShallowRef<CommunityViewState>
  readonly records: ShallowRef<readonly FriendshipRecord[]>
  query: (firstCrossplatformId: string, secondCrossplatformId: string) => Promise<void>
  loadRecords: () => Promise<void>
}

export function useCommunityFriendship(options: UseCommunityOptions, loader: CommunityLoader): CommunityFriendshipController {
  const state = shallowRef<CommunityViewState>('idle')
  const data = shallowRef<FriendshipStatus | null>(null)
  const recordsState = shallowRef<CommunityViewState>('idle')
  const records = shallowRef<readonly FriendshipRecord[]>(Object.freeze([]))

  function query(firstCrossplatformId: string, secondCrossplatformId: string): Promise<void> {
    const first = firstCrossplatformId.trim()
    const second = secondCrossplatformId.trim()
    if (first === '' || second === '')
      return Promise.resolve()
    return loader.loadQuery(
      'friendship',
      JSON.stringify([first, second]),
      state,
      () => data.value !== null,
      (token, signal) => (options.fetchFriendship ?? api.fetchFriendship)(token, first, second, signal),
      (value) => {
        data.value = value
        return 1
      },
    )
  }

  function loadRecords(): Promise<void> {
    return loader.load(
      'friendship-records',
      recordsState,
      () => records.value.length > 0,
      options.fetchFriendshipRecords ?? api.fetchFriendshipRecords,
      (value) => {
        records.value = value
        return value.length
      },
    )
  }

  return { state, data, recordsState, records, query, loadRecords }
}
