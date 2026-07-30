import type { DeepReadonly, Ref, ShallowRef } from 'vue'
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

import { readonly, shallowRef, unref } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth/model/authStore'
import * as api from '../api/community'

export type CommunityViewState = 'idle' | 'loading' | 'empty' | 'ready' | 'stale' | 'unavailable' | 'forbidden'
export type CommunityMutationState = 'idle' | 'saving' | 'confirmed' | 'failed' | 'unavailable' | 'forbidden'
export type CommunityMutationTarget
  = | { readonly kind: 'game-command-configuration', readonly id: string }
    | { readonly kind: 'teleport-setting', readonly id: string }
    | { readonly kind: 'city', readonly id: string }
    | { readonly kind: 'vote-configuration', readonly id: string }
    | { readonly kind: 'vote-settlement', readonly id: string }

type MaybeRef<T> = T | Ref<T>

interface CommunityAuth {
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

type QueryResource = 'homes' | 'friendship' | 'teleport-operation' | 'vote-round'
type ResourceKey = 'game-command-configuration' | 'teleport-settings' | 'friendship-records' | 'teleport-operations' | 'cities' | 'all-cities' | 'vote-configurations' | 'vote-rounds' | 'all-vote-rounds' | `${QueryResource}:${string}`

function stateAfterFailure(error: unknown, auth: CommunityAuth, hasData: boolean): CommunityViewState {
  if (error instanceof HttpError && error.status === 401) {
    auth.expireSession()
    return hasData ? 'stale' : 'unavailable'
  }
  if (error instanceof HttpError && error.status === 403)
    return 'forbidden'
  return hasData ? 'stale' : 'unavailable'
}

function mutationStateAfterFailure(error: unknown, auth: CommunityAuth): CommunityMutationState {
  if (error instanceof HttpError && error.status === 401) {
    auth.expireSession()
    return 'unavailable'
  }
  if (error instanceof HttpError && error.status === 403)
    return 'forbidden'
  if (error instanceof HttpError && (error.status === 503 || error.code === 'network' || error.code === 'timeout'))
    return 'unavailable'
  return 'failed'
}

export function useCommunity(options: UseCommunityOptions = {}): CommunityController {
  const auth = options.auth ?? useAuthStore()
  const gameCommandConfigurationState = shallowRef<CommunityViewState>('idle')
  const gameCommandConfiguration = shallowRef<CommunityGameCommandConfiguration | null>(null)
  const teleportSettingsState = shallowRef<CommunityViewState>('idle')
  const teleportSettings = shallowRef<readonly TeleportSettings[]>(Object.freeze([]))
  const homesState = shallowRef<CommunityViewState>('idle')
  const homes = shallowRef<readonly PlayerHome[]>(Object.freeze([]))
  const friendshipState = shallowRef<CommunityViewState>('idle')
  const friendship = shallowRef<FriendshipStatus | null>(null)
  const friendshipRecordsState = shallowRef<CommunityViewState>('idle')
  const friendshipRecords = shallowRef<readonly FriendshipRecord[]>(Object.freeze([]))
  const teleportOperationState = shallowRef<CommunityViewState>('idle')
  const teleportOperation = shallowRef<TeleportOperation | null>(null)
  const teleportOperationsState = shallowRef<CommunityViewState>('idle')
  const teleportOperations = shallowRef<readonly TeleportOperation[]>(Object.freeze([]))
  const citiesState = shallowRef<CommunityViewState>('idle')
  const cities = shallowRef<readonly City[]>(Object.freeze([]))
  const fullCityListState = shallowRef<CommunityViewState>('unavailable')
  const fullCities = shallowRef<readonly City[]>(Object.freeze([]))
  const voteConfigurationsState = shallowRef<CommunityViewState>('idle')
  const voteConfigurations = shallowRef<readonly VoteConfiguration[]>(Object.freeze([]))
  const voteRoundsState = shallowRef<CommunityViewState>('idle')
  const voteRounds = shallowRef<readonly VoteRound[]>(Object.freeze([]))
  const fullVoteRoundListState = shallowRef<CommunityViewState>('unavailable')
  const fullVoteRounds = shallowRef<readonly VoteRound[]>(Object.freeze([]))
  const voteRoundState = shallowRef<CommunityViewState>('idle')
  const voteRound = shallowRef<VoteRound | null>(null)
  const settlement = shallowRef<VoteSettlement | null>(null)
  const mutationState = shallowRef<CommunityMutationState>('idle')
  const mutationTarget = shallowRef<CommunityMutationTarget | null>(null)
  const requests: Partial<Record<ResourceKey, Promise<void>>> = {}
  const controllers: Partial<Record<ResourceKey, AbortController>> = {}
  const currentQueryKeys: Partial<Record<QueryResource, ResourceKey>> = {}
  let mutationController: AbortController | null = null
  let disposed = false

  function authorization(): string | null {
    return unref(auth.authorizationHeader)
  }

  function load<T>(
    key: ResourceKey,
    state: ShallowRef<CommunityViewState>,
    hasData: () => boolean,
    request: (authorization: string, signal: AbortSignal) => Promise<T>,
    apply: (value: T) => number,
  ): Promise<void> {
    const active = requests[key]
    if (active !== undefined)
      return active
    const token = authorization()
    if (disposed || token === null) {
      state.value = hasData() ? 'stale' : 'unavailable'
      return Promise.resolve()
    }
    const controller = new AbortController()
    controllers[key] = controller
    state.value = 'loading'
    const pending = request(token, controller.signal)
      .then((value) => {
        if (disposed || controller.signal.aborted)
          return
        state.value = apply(value) === 0 ? 'empty' : 'ready'
      })
      .catch((error: unknown) => {
        if (disposed || controller.signal.aborted)
          return
        state.value = stateAfterFailure(error, auth, hasData())
      })
      .finally(() => {
        if (requests[key] === pending) {
          delete requests[key]
          delete controllers[key]
        }
      })
    requests[key] = pending
    return pending
  }

  function loadQuery<T>(
    resource: QueryResource,
    parameter: string,
    state: ShallowRef<CommunityViewState>,
    hasData: () => boolean,
    request: (authorization: string, signal: AbortSignal) => Promise<T>,
    apply: (value: T) => number,
  ): Promise<void> {
    const key = `${resource}:${parameter}` as ResourceKey
    const previousKey = currentQueryKeys[resource]
    if (previousKey !== undefined && previousKey !== key) {
      controllers[previousKey]?.abort()
      delete controllers[previousKey]
      delete requests[previousKey]
    }
    currentQueryKeys[resource] = key
    return load(key, state, hasData, request, apply)
  }

  function loadGameCommandConfiguration(): Promise<void> {
    return load(
      'game-command-configuration',
      gameCommandConfigurationState,
      () => gameCommandConfiguration.value !== null,
      options.fetchGameCommandConfiguration ?? api.fetchGameCommandConfiguration,
      (value) => {
        gameCommandConfiguration.value = value
        return 1
      },
    )
  }

  function loadTeleportSettings(): Promise<void> {
    return load(
      'teleport-settings',
      teleportSettingsState,
      () => teleportSettings.value.length > 0,
      options.fetchTeleportSettings ?? api.fetchTeleportSettings,
      (value) => {
        teleportSettings.value = value
        return value.length
      },
    )
  }

  function queryHomes(crossplatformId: string): Promise<void> {
    const playerId = crossplatformId.trim()
    if (playerId === '')
      return Promise.resolve()
    return loadQuery(
      'homes',
      playerId,
      homesState,
      () => homes.value.length > 0,
      (token, signal) => (options.fetchHomes ?? api.fetchHomes)(token, playerId, signal),
      (value) => {
        homes.value = value
        return value.length
      },
    )
  }

  function queryFriendship(firstCrossplatformId: string, secondCrossplatformId: string): Promise<void> {
    const first = firstCrossplatformId.trim()
    const second = secondCrossplatformId.trim()
    if (first === '' || second === '')
      return Promise.resolve()
    return loadQuery(
      'friendship',
      JSON.stringify([first, second]),
      friendshipState,
      () => friendship.value !== null,
      (token, signal) => (options.fetchFriendship ?? api.fetchFriendship)(token, first, second, signal),
      (value) => {
        friendship.value = value
        return 1
      },
    )
  }

  function loadFriendshipRecords(): Promise<void> {
    return load(
      'friendship-records',
      friendshipRecordsState,
      () => friendshipRecords.value.length > 0,
      options.fetchFriendshipRecords ?? api.fetchFriendshipRecords,
      (value) => {
        friendshipRecords.value = value
        return value.length
      },
    )
  }

  function queryTeleportOperation(operationId: string): Promise<void> {
    const id = operationId.trim()
    if (id === '')
      return Promise.resolve()
    return loadQuery(
      'teleport-operation',
      id,
      teleportOperationState,
      () => teleportOperation.value !== null,
      (token, signal) => (options.fetchTeleportOperation ?? api.fetchTeleportOperation)(token, id, signal),
      (value) => {
        teleportOperation.value = value
        return 1
      },
    )
  }

  function loadTeleportOperations(): Promise<void> {
    return load(
      'teleport-operations',
      teleportOperationsState,
      () => teleportOperations.value.length > 0,
      options.fetchTeleportOperations ?? api.fetchTeleportOperations,
      (value) => {
        teleportOperations.value = value
        return value.length
      },
    )
  }

  function loadCities(): Promise<void> {
    return load(
      'cities',
      citiesState,
      () => cities.value.length > 0,
      options.fetchCities ?? api.fetchCities,
      (value) => {
        cities.value = value
        return value.length
      },
    )
  }

  function loadAllCities(): Promise<void> {
    return load(
      'all-cities',
      fullCityListState,
      () => fullCities.value.length > 0,
      options.fetchAllCities ?? api.fetchAllCities,
      (value) => {
        fullCities.value = value
        return value.length
      },
    )
  }

  function loadVoteConfigurations(): Promise<void> {
    return load(
      'vote-configurations',
      voteConfigurationsState,
      () => voteConfigurations.value.length > 0,
      options.fetchVoteConfigurations ?? api.fetchVoteConfigurations,
      (value) => {
        voteConfigurations.value = value
        return value.length
      },
    )
  }

  function loadVoteRounds(): Promise<void> {
    return load(
      'vote-rounds',
      voteRoundsState,
      () => voteRounds.value.length > 0,
      options.fetchActionQueuedVoteRounds ?? api.fetchActionQueuedVoteRounds,
      (value) => {
        voteRounds.value = value
        return value.length
      },
    )
  }

  function loadAllVoteRounds(): Promise<void> {
    return load(
      'all-vote-rounds',
      fullVoteRoundListState,
      () => fullVoteRounds.value.length > 0,
      options.fetchVoteRounds ?? api.fetchVoteRounds,
      (value) => {
        fullVoteRounds.value = value
        return value.length
      },
    )
  }

  function queryVoteRound(roundId: string): Promise<void> {
    const id = roundId.trim()
    if (id === '')
      return Promise.resolve()
    settlement.value = null
    return loadQuery(
      'vote-round',
      id,
      voteRoundState,
      () => voteRound.value !== null,
      (token, signal) => (options.fetchVoteRound ?? api.fetchVoteRound)(token, id, signal),
      (value) => {
        voteRound.value = value
        return 1
      },
    )
  }

  async function mutate<T>(
    target: CommunityMutationTarget,
    request: (authorization: string, signal: AbortSignal) => Promise<T>,
    apply: (value: T) => void,
  ): Promise<boolean> {
    const token = authorization()
    if (disposed || token === null || mutationTarget.value !== null)
      return false
    const controller = new AbortController()
    mutationController = controller
    mutationTarget.value = target
    mutationState.value = 'saving'
    try {
      const value = await request(token, controller.signal)
      if (disposed || controller.signal.aborted)
        return false
      apply(value)
      mutationState.value = 'confirmed'
      return true
    }
    catch (error) {
      if (!controller.signal.aborted)
        mutationState.value = mutationStateAfterFailure(error, auth)
      return false
    }
    finally {
      if (mutationController === controller) {
        mutationController = null
        mutationTarget.value = null
      }
    }
  }

  function saveGameCommandConfiguration(
    current: CommunityGameCommandConfiguration,
    input: CommunityGameCommandConfigurationInput,
  ): Promise<boolean> {
    return mutate(
      { kind: 'game-command-configuration', id: 'community' },
      (token, signal) => (options.updateGameCommandConfiguration ?? api.updateGameCommandConfiguration)(token, current, input, signal),
      (authoritative) => {
        gameCommandConfiguration.value = authoritative
        gameCommandConfigurationState.value = 'ready'
      },
    )
  }

  function saveTeleportSetting(current: TeleportSettings, input: TeleportSettingsInput): Promise<boolean> {
    return mutate(
      { kind: 'teleport-setting', id: current.kind },
      (token, signal) => (options.updateTeleportSetting ?? api.updateTeleportSetting)(token, current, input, signal),
      (authoritative) => {
        teleportSettings.value = Object.freeze(teleportSettings.value.map(value => value.kind === authoritative.kind ? authoritative : value))
        teleportSettingsState.value = 'ready'
      },
    )
  }

  function saveCity(input: CityInput): Promise<boolean> {
    return mutate(
      { kind: 'city', id: input.cityId },
      (token, signal) => (options.upsertCity ?? api.upsertCity)(token, input, signal),
      (authoritative) => {
        const remaining = cities.value.filter(value => value.cityId !== authoritative.cityId)
        cities.value = Object.freeze(authoritative.enabled ? [...remaining, authoritative] : remaining)
        citiesState.value = cities.value.length === 0 ? 'empty' : 'ready'
      },
    )
  }

  function saveVoteConfiguration(current: VoteConfiguration, input: VoteConfigurationInput): Promise<boolean> {
    return mutate(
      { kind: 'vote-configuration', id: current.kind },
      (token, signal) => (options.updateVoteConfiguration ?? api.updateVoteConfiguration)(token, current, input, signal),
      (authoritative) => {
        voteConfigurations.value = Object.freeze(voteConfigurations.value.map(value => value.kind === authoritative.kind ? authoritative : value))
        voteConfigurationsState.value = 'ready'
      },
    )
  }

  function settleVote(roundId: string): Promise<boolean> {
    const id = roundId.trim()
    if (id === '')
      return Promise.resolve(false)
    return mutate(
      { kind: 'vote-settlement', id },
      (token, signal) => (options.settleVoteRound ?? api.settleVoteRound)(token, id, signal),
      (authoritative) => {
        settlement.value = authoritative
        voteRound.value = authoritative.round
        voteRoundState.value = 'ready'
        const remaining = voteRounds.value.filter(value => value.roundId !== authoritative.round.roundId)
        voteRounds.value = Object.freeze(authoritative.round.state === 'ActionQueued'
          ? [...remaining, authoritative.round]
          : remaining)
        voteRoundsState.value = voteRounds.value.length === 0 ? 'empty' : 'ready'
      },
    )
  }

  function clearMutationState() {
    if (mutationTarget.value === null)
      mutationState.value = 'idle'
  }

  function dispose() {
    disposed = true
    for (const controller of Object.values(controllers))
      controller?.abort()
    mutationController?.abort()
    mutationController = null
    mutationTarget.value = null
    mutationState.value = 'idle'
  }

  return {
    gameCommandConfigurationState: readonly(gameCommandConfigurationState),
    gameCommandConfiguration: readonly(gameCommandConfiguration),
    teleportSettingsState: readonly(teleportSettingsState),
    teleportSettings: readonly(teleportSettings),
    homesState: readonly(homesState),
    homes: readonly(homes),
    friendshipState: readonly(friendshipState),
    friendship: readonly(friendship),
    friendshipRecordsState: readonly(friendshipRecordsState),
    friendshipRecords: readonly(friendshipRecords),
    teleportOperationState: readonly(teleportOperationState),
    teleportOperation: readonly(teleportOperation),
    teleportOperationsState: readonly(teleportOperationsState),
    teleportOperations: readonly(teleportOperations),
    citiesState: readonly(citiesState),
    cities: readonly(cities),
    fullCityListState: readonly(fullCityListState),
    fullCities: readonly(fullCities),
    voteConfigurationsState: readonly(voteConfigurationsState),
    voteConfigurations: readonly(voteConfigurations),
    voteRoundsState: readonly(voteRoundsState),
    voteRounds: readonly(voteRounds),
    fullVoteRoundListState: readonly(fullVoteRoundListState),
    fullVoteRounds: readonly(fullVoteRounds),
    voteRoundState: readonly(voteRoundState),
    voteRound: readonly(voteRound),
    settlement: readonly(settlement),
    mutationState: readonly(mutationState),
    mutationTarget: readonly(mutationTarget),
    loadGameCommandConfiguration,
    saveGameCommandConfiguration,
    loadTeleportSettings,
    saveTeleportSetting,
    queryHomes,
    queryFriendship,
    loadFriendshipRecords,
    queryTeleportOperation,
    loadTeleportOperations,
    loadCities,
    loadAllCities,
    saveCity,
    loadVoteConfigurations,
    saveVoteConfiguration,
    loadVoteRounds,
    loadAllVoteRounds,
    queryVoteRound,
    settleVote,
    clearMutationState,
    dispose,
  }
}
