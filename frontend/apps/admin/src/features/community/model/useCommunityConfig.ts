import type { ShallowRef } from 'vue'

import type { CommunityGameCommandConfiguration, CommunityGameCommandConfigurationInput } from '../api/community'

import type { CommunityLoader } from './community-loader'
import type { CommunityMutationController } from './community-mutation'
import type { CommunityViewState, UseCommunityOptions } from './community.types'
import { shallowRef } from 'vue'
import * as api from '../api/community'

export interface CommunityConfigController {
  readonly state: ShallowRef<CommunityViewState>
  readonly data: ShallowRef<CommunityGameCommandConfiguration | null>
  load: () => Promise<void>
  save: (current: CommunityGameCommandConfiguration, input: CommunityGameCommandConfigurationInput) => Promise<boolean>
}

export function useCommunityConfig(
  options: UseCommunityOptions,
  loader: CommunityLoader,
  mutation: CommunityMutationController,
): CommunityConfigController {
  const state = shallowRef<CommunityViewState>('idle')
  const data = shallowRef<CommunityGameCommandConfiguration | null>(null)

  function load(): Promise<void> {
    return loader.load(
      'game-command-configuration',
      state,
      () => data.value !== null,
      options.fetchGameCommandConfiguration ?? api.fetchGameCommandConfiguration,
      (value) => {
        data.value = value
        return 1
      },
    )
  }

  function save(current: CommunityGameCommandConfiguration, input: CommunityGameCommandConfigurationInput): Promise<boolean> {
    return mutation.mutate(
      { kind: 'game-command-configuration', id: 'community' },
      (token, signal) => (options.updateGameCommandConfiguration ?? api.updateGameCommandConfiguration)(token, current, input, signal),
      (authoritative) => {
        data.value = authoritative
        state.value = 'ready'
      },
    )
  }

  return { state, data, load, save }
}
