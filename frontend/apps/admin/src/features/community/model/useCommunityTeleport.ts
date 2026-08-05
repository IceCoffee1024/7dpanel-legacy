import type { ShallowRef } from 'vue'

import type { PlayerHome, TeleportOperation, TeleportSettings, TeleportSettingsInput } from '../api/community'

import type { CommunityLoader } from './community-loader'
import type { CommunityMutationController } from './community-mutation'
import type { CommunityViewState, UseCommunityOptions } from './community.types'
import { shallowRef } from 'vue'
import * as api from '../api/community'

export interface CommunityTeleportController {
  readonly settingsState: ShallowRef<CommunityViewState>
  readonly settings: ShallowRef<readonly TeleportSettings[]>
  readonly homesState: ShallowRef<CommunityViewState>
  readonly homes: ShallowRef<readonly PlayerHome[]>
  readonly operationState: ShallowRef<CommunityViewState>
  readonly operation: ShallowRef<TeleportOperation | null>
  readonly operationsState: ShallowRef<CommunityViewState>
  readonly operations: ShallowRef<readonly TeleportOperation[]>
  loadSettings: () => Promise<void>
  saveSetting: (current: TeleportSettings, input: TeleportSettingsInput) => Promise<boolean>
  queryHomes: (crossplatformId: string) => Promise<void>
  queryOperation: (operationId: string) => Promise<void>
  loadOperations: () => Promise<void>
}

export function useCommunityTeleport(
  options: UseCommunityOptions,
  loader: CommunityLoader,
  mutation: CommunityMutationController,
): CommunityTeleportController {
  const settingsState = shallowRef<CommunityViewState>('idle')
  const settings = shallowRef<readonly TeleportSettings[]>(Object.freeze([]))
  const homesState = shallowRef<CommunityViewState>('idle')
  const homes = shallowRef<readonly PlayerHome[]>(Object.freeze([]))
  const operationState = shallowRef<CommunityViewState>('idle')
  const operation = shallowRef<TeleportOperation | null>(null)
  const operationsState = shallowRef<CommunityViewState>('idle')
  const operations = shallowRef<readonly TeleportOperation[]>(Object.freeze([]))

  function loadSettings(): Promise<void> {
    return loader.load(
      'teleport-settings',
      settingsState,
      () => settings.value.length > 0,
      options.fetchTeleportSettings ?? api.fetchTeleportSettings,
      (value) => {
        settings.value = value
        return value.length
      },
    )
  }

  function saveSetting(current: TeleportSettings, input: TeleportSettingsInput): Promise<boolean> {
    return mutation.mutate(
      { kind: 'teleport-setting', id: current.kind },
      (token, signal) => (options.updateTeleportSetting ?? api.updateTeleportSetting)(token, current, input, signal),
      (authoritative) => {
        settings.value = Object.freeze(settings.value.map(value => value.kind === authoritative.kind ? authoritative : value))
        settingsState.value = 'ready'
      },
    )
  }

  function queryHomes(crossplatformId: string): Promise<void> {
    const playerId = crossplatformId.trim()
    if (playerId === '')
      return Promise.resolve()
    return loader.loadQuery(
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

  function queryOperation(operationId: string): Promise<void> {
    const id = operationId.trim()
    if (id === '')
      return Promise.resolve()
    return loader.loadQuery(
      'teleport-operation',
      id,
      operationState,
      () => operation.value !== null,
      (token, signal) => (options.fetchTeleportOperation ?? api.fetchTeleportOperation)(token, id, signal),
      (value) => {
        operation.value = value
        return 1
      },
    )
  }

  function loadOperations(): Promise<void> {
    return loader.load(
      'teleport-operations',
      operationsState,
      () => operations.value.length > 0,
      options.fetchTeleportOperations ?? api.fetchTeleportOperations,
      (value) => {
        operations.value = value
        return value.length
      },
    )
  }

  return {
    settingsState,
    settings,
    homesState,
    homes,
    operationState,
    operation,
    operationsState,
    operations,
    loadSettings,
    saveSetting,
    queryHomes,
    queryOperation,
    loadOperations,
  }
}
