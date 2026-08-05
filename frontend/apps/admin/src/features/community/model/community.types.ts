import type { Ref } from 'vue'

import type * as api from '../api/community'

export type CommunityViewState = 'idle' | 'loading' | 'empty' | 'ready' | 'stale' | 'unavailable' | 'forbidden'
export type CommunityMutationState = 'idle' | 'saving' | 'confirmed' | 'failed' | 'unavailable' | 'forbidden'
export type CommunityMutationTarget
  = | { readonly kind: 'game-command-configuration', readonly id: string }
    | { readonly kind: 'teleport-setting', readonly id: string }
    | { readonly kind: 'city', readonly id: string }
    | { readonly kind: 'vote-configuration', readonly id: string }
    | { readonly kind: 'vote-settlement', readonly id: string }

export type MaybeRef<T> = T | Ref<T>

export interface CommunityAuth {
  readonly authorizationHeader: MaybeRef<string | null>
  expireSession: () => void
}

export interface UseCommunityOptions {
  readonly auth?: CommunityAuth
  readonly fetchTeleportSettings?: typeof api.fetchTeleportSettings
  readonly updateTeleportSetting?: typeof api.updateTeleportSetting
  readonly fetchGameCommandConfiguration?: typeof api.fetchGameCommandConfiguration
  readonly updateGameCommandConfiguration?: typeof api.updateGameCommandConfiguration
  readonly fetchHomes?: typeof api.fetchHomes
  readonly fetchCities?: typeof api.fetchCities
  readonly fetchAllCities?: typeof api.fetchAllCities
  readonly upsertCity?: typeof api.upsertCity
  readonly fetchFriendship?: typeof api.fetchFriendship
  readonly fetchFriendshipRecords?: typeof api.fetchFriendshipRecords
  readonly fetchTeleportOperation?: typeof api.fetchTeleportOperation
  readonly fetchTeleportOperations?: typeof api.fetchTeleportOperations
  readonly fetchVoteConfigurations?: typeof api.fetchVoteConfigurations
  readonly updateVoteConfiguration?: typeof api.updateVoteConfiguration
  readonly fetchActionQueuedVoteRounds?: typeof api.fetchActionQueuedVoteRounds
  readonly fetchVoteRounds?: typeof api.fetchVoteRounds
  readonly fetchVoteRound?: typeof api.fetchVoteRound
  readonly settleVoteRound?: typeof api.settleVoteRound
}
