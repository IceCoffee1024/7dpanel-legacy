import type { DeepReadonly, ShallowRef } from 'vue'

import type {
  City,
  CityInput,
  CommunityGameCommandConfiguration,
  CommunityGameCommandConfigurationInput,
  FriendshipRecord,
  FriendshipStatus,
  PlayerHome,
  TeleportOperation,
  TeleportSettings,
  TeleportSettingsInput,
  VoteConfiguration,
  VoteConfigurationInput,
  VoteRound,
  VoteSettlement,
} from '../api/community'

import type { CommunityAuth, CommunityMutationState, CommunityMutationTarget, CommunityViewState, UseCommunityOptions } from './community.types'
import { readonly } from 'vue'
import { useAuthStore } from '../../auth/model/authStore'
import { createCommunityLoader } from './community-loader'
import { createCommunityMutation } from './community-mutation'
import { useCommunityCity } from './useCommunityCity'
import { useCommunityConfig } from './useCommunityConfig'
import { useCommunityFriendship } from './useCommunityFriendship'
import { useCommunityTeleport } from './useCommunityTeleport'
import { useCommunityVote } from './useCommunityVote'

export type { CommunityAuth, CommunityMutationState, CommunityMutationTarget, CommunityViewState, UseCommunityOptions }

export interface CommunityController {
  readonly gameCommandConfigurationState: DeepReadonly<ShallowRef<CommunityViewState>>
  readonly gameCommandConfiguration: DeepReadonly<ShallowRef<CommunityGameCommandConfiguration | null>>
  readonly teleportSettingsState: DeepReadonly<ShallowRef<CommunityViewState>>
  readonly teleportSettings: DeepReadonly<ShallowRef<readonly TeleportSettings[]>>
  readonly homesState: DeepReadonly<ShallowRef<CommunityViewState>>
  readonly homes: DeepReadonly<ShallowRef<readonly PlayerHome[]>>
  readonly friendshipState: DeepReadonly<ShallowRef<CommunityViewState>>
  readonly friendship: DeepReadonly<ShallowRef<FriendshipStatus | null>>
  readonly friendshipRecordsState: DeepReadonly<ShallowRef<CommunityViewState>>
  readonly friendshipRecords: DeepReadonly<ShallowRef<readonly FriendshipRecord[]>>
  readonly teleportOperationState: DeepReadonly<ShallowRef<CommunityViewState>>
  readonly teleportOperation: DeepReadonly<ShallowRef<TeleportOperation | null>>
  readonly teleportOperationsState: DeepReadonly<ShallowRef<CommunityViewState>>
  readonly teleportOperations: DeepReadonly<ShallowRef<readonly TeleportOperation[]>>
  readonly citiesState: DeepReadonly<ShallowRef<CommunityViewState>>
  readonly cities: DeepReadonly<ShallowRef<readonly City[]>>
  readonly fullCityListState: DeepReadonly<ShallowRef<CommunityViewState>>
  readonly fullCities: DeepReadonly<ShallowRef<readonly City[]>>
  readonly voteConfigurationsState: DeepReadonly<ShallowRef<CommunityViewState>>
  readonly voteConfigurations: DeepReadonly<ShallowRef<readonly VoteConfiguration[]>>
  readonly voteRoundsState: DeepReadonly<ShallowRef<CommunityViewState>>
  readonly voteRounds: DeepReadonly<ShallowRef<readonly VoteRound[]>>
  readonly fullVoteRoundListState: DeepReadonly<ShallowRef<CommunityViewState>>
  readonly fullVoteRounds: DeepReadonly<ShallowRef<readonly VoteRound[]>>
  readonly voteRoundState: DeepReadonly<ShallowRef<CommunityViewState>>
  readonly voteRound: DeepReadonly<ShallowRef<VoteRound | null>>
  readonly settlement: DeepReadonly<ShallowRef<VoteSettlement | null>>
  readonly mutationState: DeepReadonly<ShallowRef<CommunityMutationState>>
  readonly mutationTarget: DeepReadonly<ShallowRef<CommunityMutationTarget | null>>
  loadGameCommandConfiguration: () => Promise<void>
  saveGameCommandConfiguration: (current: CommunityGameCommandConfiguration, input: CommunityGameCommandConfigurationInput) => Promise<boolean>
  loadTeleportSettings: () => Promise<void>
  saveTeleportSetting: (current: TeleportSettings, input: TeleportSettingsInput) => Promise<boolean>
  queryHomes: (crossplatformId: string) => Promise<void>
  queryFriendship: (firstCrossplatformId: string, secondCrossplatformId: string) => Promise<void>
  loadFriendshipRecords: () => Promise<void>
  queryTeleportOperation: (operationId: string) => Promise<void>
  loadTeleportOperations: () => Promise<void>
  loadCities: () => Promise<void>
  loadAllCities: () => Promise<void>
  saveCity: (input: CityInput) => Promise<boolean>
  loadVoteConfigurations: () => Promise<void>
  saveVoteConfiguration: (current: VoteConfiguration, input: VoteConfigurationInput) => Promise<boolean>
  loadVoteRounds: () => Promise<void>
  loadAllVoteRounds: () => Promise<void>
  queryVoteRound: (roundId: string) => Promise<void>
  settleVote: (roundId: string) => Promise<boolean>
  clearMutationState: () => void
  dispose: () => void
}

export function useCommunity(options: UseCommunityOptions = {}): CommunityController {
  const auth = options.auth ?? useAuthStore()
  let disposed = false
  const loader = createCommunityLoader(auth, () => disposed)
  const mutation = createCommunityMutation(auth, () => disposed)
  const config = useCommunityConfig(options, loader, mutation)
  const teleport = useCommunityTeleport(options, loader, mutation)
  const friendship = useCommunityFriendship(options, loader)
  const city = useCommunityCity(options, loader, mutation)
  const vote = useCommunityVote(options, loader, mutation)

  function dispose() {
    disposed = true
    loader.dispose()
    mutation.dispose()
  }

  return {
    gameCommandConfigurationState: readonly(config.state),
    gameCommandConfiguration: readonly(config.data),
    teleportSettingsState: readonly(teleport.settingsState),
    teleportSettings: readonly(teleport.settings),
    homesState: readonly(teleport.homesState),
    homes: readonly(teleport.homes),
    friendshipState: readonly(friendship.state),
    friendship: readonly(friendship.data),
    friendshipRecordsState: readonly(friendship.recordsState),
    friendshipRecords: readonly(friendship.records),
    teleportOperationState: readonly(teleport.operationState),
    teleportOperation: readonly(teleport.operation),
    teleportOperationsState: readonly(teleport.operationsState),
    teleportOperations: readonly(teleport.operations),
    citiesState: readonly(city.state),
    cities: readonly(city.cities),
    fullCityListState: readonly(city.fullState),
    fullCities: readonly(city.fullCities),
    voteConfigurationsState: readonly(vote.configurationsState),
    voteConfigurations: readonly(vote.configurations),
    voteRoundsState: readonly(vote.roundsState),
    voteRounds: readonly(vote.rounds),
    fullVoteRoundListState: readonly(vote.fullRoundsState),
    fullVoteRounds: readonly(vote.fullRounds),
    voteRoundState: readonly(vote.roundState),
    voteRound: readonly(vote.round),
    settlement: readonly(vote.settlement),
    mutationState: readonly(mutation.state),
    mutationTarget: readonly(mutation.target),
    loadGameCommandConfiguration: config.load,
    saveGameCommandConfiguration: config.save,
    loadTeleportSettings: teleport.loadSettings,
    saveTeleportSetting: teleport.saveSetting,
    queryHomes: teleport.queryHomes,
    queryFriendship: friendship.query,
    loadFriendshipRecords: friendship.loadRecords,
    queryTeleportOperation: teleport.queryOperation,
    loadTeleportOperations: teleport.loadOperations,
    loadCities: city.load,
    loadAllCities: city.loadAll,
    saveCity: city.save,
    loadVoteConfigurations: vote.loadConfigurations,
    saveVoteConfiguration: vote.saveConfiguration,
    loadVoteRounds: vote.loadRounds,
    loadAllVoteRounds: vote.loadAllRounds,
    queryVoteRound: vote.queryRound,
    settleVote: vote.settle,
    clearMutationState: mutation.clear,
    dispose,
  }
}
