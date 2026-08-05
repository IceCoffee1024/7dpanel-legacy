import type { ShallowRef } from 'vue'

import type { VoteConfiguration, VoteConfigurationInput, VoteRound, VoteSettlement } from '../api/community'

import type { CommunityLoader } from './community-loader'
import type { CommunityMutationController } from './community-mutation'
import type { CommunityViewState, UseCommunityOptions } from './community.types'
import { shallowRef } from 'vue'
import * as api from '../api/community'

export interface CommunityVoteController {
  readonly configurationsState: ShallowRef<CommunityViewState>
  readonly configurations: ShallowRef<readonly VoteConfiguration[]>
  readonly roundsState: ShallowRef<CommunityViewState>
  readonly rounds: ShallowRef<readonly VoteRound[]>
  readonly fullRoundsState: ShallowRef<CommunityViewState>
  readonly fullRounds: ShallowRef<readonly VoteRound[]>
  readonly roundState: ShallowRef<CommunityViewState>
  readonly round: ShallowRef<VoteRound | null>
  readonly settlement: ShallowRef<VoteSettlement | null>
  loadConfigurations: () => Promise<void>
  saveConfiguration: (current: VoteConfiguration, input: VoteConfigurationInput) => Promise<boolean>
  loadRounds: () => Promise<void>
  loadAllRounds: () => Promise<void>
  queryRound: (roundId: string) => Promise<void>
  settle: (roundId: string) => Promise<boolean>
}

export function useCommunityVote(
  options: UseCommunityOptions,
  loader: CommunityLoader,
  mutation: CommunityMutationController,
): CommunityVoteController {
  const configurationsState = shallowRef<CommunityViewState>('idle')
  const configurations = shallowRef<readonly VoteConfiguration[]>(Object.freeze([]))
  const roundsState = shallowRef<CommunityViewState>('idle')
  const rounds = shallowRef<readonly VoteRound[]>(Object.freeze([]))
  const fullRoundsState = shallowRef<CommunityViewState>('unavailable')
  const fullRounds = shallowRef<readonly VoteRound[]>(Object.freeze([]))
  const roundState = shallowRef<CommunityViewState>('idle')
  const round = shallowRef<VoteRound | null>(null)
  const settlement = shallowRef<VoteSettlement | null>(null)

  function loadConfigurations(): Promise<void> {
    return loader.load(
      'vote-configurations',
      configurationsState,
      () => configurations.value.length > 0,
      options.fetchVoteConfigurations ?? api.fetchVoteConfigurations,
      (value) => {
        configurations.value = value
        return value.length
      },
    )
  }

  function saveConfiguration(current: VoteConfiguration, input: VoteConfigurationInput): Promise<boolean> {
    return mutation.mutate(
      { kind: 'vote-configuration', id: current.kind },
      (token, signal) => (options.updateVoteConfiguration ?? api.updateVoteConfiguration)(token, current, input, signal),
      (authoritative) => {
        configurations.value = Object.freeze(configurations.value.map(value => value.kind === authoritative.kind ? authoritative : value))
        configurationsState.value = 'ready'
      },
    )
  }

  function loadRounds(): Promise<void> {
    return loader.load(
      'vote-rounds',
      roundsState,
      () => rounds.value.length > 0,
      options.fetchActionQueuedVoteRounds ?? api.fetchActionQueuedVoteRounds,
      (value) => {
        rounds.value = value
        return value.length
      },
    )
  }

  function loadAllRounds(): Promise<void> {
    return loader.load(
      'all-vote-rounds',
      fullRoundsState,
      () => fullRounds.value.length > 0,
      options.fetchVoteRounds ?? api.fetchVoteRounds,
      (value) => {
        fullRounds.value = value
        return value.length
      },
    )
  }

  function queryRound(roundId: string): Promise<void> {
    const id = roundId.trim()
    if (id === '')
      return Promise.resolve()
    settlement.value = null
    return loader.loadQuery(
      'vote-round',
      id,
      roundState,
      () => round.value !== null,
      (token, signal) => (options.fetchVoteRound ?? api.fetchVoteRound)(token, id, signal),
      (value) => {
        round.value = value
        return 1
      },
    )
  }

  function settle(roundId: string): Promise<boolean> {
    const id = roundId.trim()
    if (id === '')
      return Promise.resolve(false)
    return mutation.mutate(
      { kind: 'vote-settlement', id },
      (token, signal) => (options.settleVoteRound ?? api.settleVoteRound)(token, id, signal),
      (authoritative) => {
        settlement.value = authoritative
        round.value = authoritative.round
        roundState.value = 'ready'
        const remaining = rounds.value.filter(value => value.roundId !== authoritative.round.roundId)
        rounds.value = Object.freeze(authoritative.round.state === 'ActionQueued'
          ? [...remaining, authoritative.round]
          : remaining)
        roundsState.value = rounds.value.length === 0 ? 'empty' : 'ready'
        if (fullRoundsState.value === 'ready' || fullRoundsState.value === 'empty') {
          const fullRemaining = fullRounds.value.filter(value => value.roundId !== authoritative.round.roundId)
          fullRounds.value = Object.freeze([...fullRemaining, authoritative.round])
          fullRoundsState.value = 'ready'
        }
        else {
          loader.invalidate('all-vote-rounds', fullRoundsState)
          fullRounds.value = Object.freeze([])
        }
      },
    )
  }

  return {
    configurationsState,
    configurations,
    roundsState,
    rounds,
    fullRoundsState,
    fullRounds,
    roundState,
    round,
    settlement,
    loadConfigurations,
    saveConfiguration,
    loadRounds,
    loadAllRounds,
    queryRound,
    settle,
  }
}
