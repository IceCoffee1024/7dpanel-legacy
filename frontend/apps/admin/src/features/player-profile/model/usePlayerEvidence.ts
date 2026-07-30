import type { DeepReadonly, MaybeRefOrGetter, ShallowRef } from 'vue'
import type {
  FetchPlayerInventoryDiffs,
  FetchPlayerInventorySnapshots,
  FetchPlayerSkills,
  PlayerEvidenceGap,
  PlayerInventoryDiff,
  PlayerInventoryDiffsPage,
  PlayerInventorySnapshot,
  PlayerInventorySnapshotsPage,
  PlayerSkillSnapshot,
  PlayerSkillsPage,
} from '../api/playerEvidence'

import { onScopeDispose, readonly, shallowRef, toValue, watch } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import {
  fetchPlayerInventoryDiffs,
  fetchPlayerInventorySnapshots,
  fetchPlayerSkills,
} from '../api/playerEvidence'

export type PlayerEvidenceViewState = 'loading' | 'available' | 'partial' | 'stale' | 'unavailable' | 'forbidden'

export interface PlayerEvidenceFeedController<T> {
  state: DeepReadonly<ShallowRef<PlayerEvidenceViewState>>
  items: DeepReadonly<ShallowRef<readonly T[]>>
  observedAtUtc: DeepReadonly<ShallowRef<string | null>>
  nextCursor: DeepReadonly<ShallowRef<string | null>>
  gaps: DeepReadonly<ShallowRef<readonly PlayerEvidenceGap[]>>
  isRefreshing: DeepReadonly<ShallowRef<boolean>>
  errorCode: DeepReadonly<ShallowRef<string | null>>
  refresh: () => Promise<void>
  loadMore: () => Promise<void>
}

export interface PlayerEvidenceController {
  inventorySnapshots: PlayerEvidenceFeedController<PlayerInventorySnapshot>
  inventoryDiffs: PlayerEvidenceFeedController<PlayerInventoryDiff>
  skills: PlayerEvidenceFeedController<PlayerSkillSnapshot>
  refresh: () => Promise<void>
  dispose: () => void
}

interface EvidenceAuth {
  authorizationHeader: string | null
  expireSession: () => void
}

export interface UsePlayerEvidenceOptions {
  auth?: EvidenceAuth
  pageSize?: number
  fetchInventorySnapshots?: FetchPlayerInventorySnapshots
  fetchInventoryDiffs?: FetchPlayerInventoryDiffs
  fetchSkills?: FetchPlayerSkills
  onSessionExpired?: () => void
}

interface EvidencePage<T> {
  readonly state?: string
  readonly observedAtUtc?: string | null
  readonly nextCursor?: string | null
  readonly gapMetadata?: readonly PlayerEvidenceGap[]
  readonly values: readonly T[]
}

function mapState(value: string | undefined): PlayerEvidenceViewState {
  if (value === 'Available')
    return 'available'
  if (value === 'Partial')
    return 'partial'
  if (value === 'Forbidden')
    return 'forbidden'
  return 'unavailable'
}

export function usePlayerEvidence(
  crossplatformId: MaybeRefOrGetter<string>,
  options: UsePlayerEvidenceOptions = {},
): PlayerEvidenceController {
  const auth = options.auth ?? useAuthStore()
  const pageSize = options.pageSize ?? 50
  let disposed = false
  let sessionExpiryNotified = false

  function expireSession() {
    auth.expireSession()
    if (!sessionExpiryNotified) {
      sessionExpiryNotified = true
      options.onSessionExpired?.()
    }
  }

  function createFeed<T, TPage>(
    request: (authorizationHeader: string, id: string, cursor: string | null, signal: AbortSignal) => Promise<TPage>,
    normalize: (page: TPage) => EvidencePage<T>,
  ) {
    const state = shallowRef<PlayerEvidenceViewState>('loading')
    const items = shallowRef<readonly T[]>(Object.freeze([]))
    const observedAtUtc = shallowRef<string | null>(null)
    const nextCursor = shallowRef<string | null>(null)
    const gaps = shallowRef<readonly PlayerEvidenceGap[]>(Object.freeze([]))
    const isRefreshing = shallowRef(false)
    const errorCode = shallowRef<string | null>(null)
    let controller: AbortController | null = null
    let version = 0

    async function run(append: boolean): Promise<void> {
      if (disposed)
        return
      const id = toValue(crossplatformId).trim()
      if (id === '') {
        state.value = 'unavailable'
        return
      }
      const cursor = append ? nextCursor.value : null
      if (append && cursor === null)
        return
      controller?.abort()
      const currentVersion = ++version
      const nextController = new AbortController()
      controller = nextController
      const authorizationHeader = auth.authorizationHeader
      if (authorizationHeader === null) {
        expireSession()
        state.value = items.value.length === 0 ? 'unavailable' : 'stale'
        return
      }
      if (items.value.length === 0)
        state.value = 'loading'
      isRefreshing.value = true
      try {
        const page = normalize(await request(authorizationHeader, id, cursor, nextController.signal))
        if (disposed || currentVersion !== version)
          return
        items.value = Object.freeze(append ? [...items.value, ...page.values] : [...page.values])
        observedAtUtc.value = page.observedAtUtc ?? null
        nextCursor.value = page.nextCursor ?? null
        gaps.value = Object.freeze([...(page.gapMetadata ?? [])])
        state.value = mapState(page.state)
        errorCode.value = null
        sessionExpiryNotified = false
      }
      catch (error) {
        if (disposed || currentVersion !== version || (error instanceof HttpError && error.code === 'aborted'))
          return
        if (error instanceof HttpError && error.status === 401)
          expireSession()
        if (error instanceof HttpError && error.status === 403) {
          items.value = Object.freeze([])
          nextCursor.value = null
          state.value = 'forbidden'
        }
        else {
          state.value = items.value.length === 0 ? 'unavailable' : 'stale'
        }
        errorCode.value = error instanceof HttpError ? (error.problemCode ?? error.code) : 'protocol_error'
      }
      finally {
        if (currentVersion === version) {
          controller = null
          isRefreshing.value = false
        }
      }
    }

    function reset() {
      version++
      controller?.abort()
      controller = null
      items.value = Object.freeze([])
      observedAtUtc.value = null
      nextCursor.value = null
      gaps.value = Object.freeze([])
      errorCode.value = null
      isRefreshing.value = false
      state.value = 'loading'
    }

    function disposeFeed() {
      version++
      controller?.abort()
      controller = null
      isRefreshing.value = false
    }

    return {
      public: {
        state: readonly(state),
        items: readonly(items),
        observedAtUtc: readonly(observedAtUtc),
        nextCursor: readonly(nextCursor),
        gaps: readonly(gaps),
        isRefreshing: readonly(isRefreshing),
        errorCode: readonly(errorCode),
        refresh: () => run(false),
        loadMore: () => run(true),
      } satisfies PlayerEvidenceFeedController<T>,
      reset,
      dispose: disposeFeed,
    }
  }

  const snapshots = createFeed<PlayerInventorySnapshot, PlayerInventorySnapshotsPage>(
    (header, id, cursor, signal) => (options.fetchInventorySnapshots ?? fetchPlayerInventorySnapshots)(
      header,
      id,
      { pageSize, cursor },
      signal,
    ),
    page => ({
      state: page.state,
      observedAtUtc: page.observedAtUtc,
      nextCursor: page.nextCursor,
      gapMetadata: page.gapMetadata,
      values: page.snapshots ?? [],
    }),
  )
  const diffs = createFeed<PlayerInventoryDiff, PlayerInventoryDiffsPage>(
    (header, id, cursor, signal) => (options.fetchInventoryDiffs ?? fetchPlayerInventoryDiffs)(
      header,
      id,
      { pageSize, cursor },
      signal,
    ),
    page => ({
      state: page.state,
      observedAtUtc: page.observedAtUtc,
      nextCursor: page.nextCursor,
      gapMetadata: page.gapMetadata,
      values: page.diffs ?? [],
    }),
  )
  const skills = createFeed<PlayerSkillSnapshot, PlayerSkillsPage>(
    (header, id, cursor, signal) => (options.fetchSkills ?? fetchPlayerSkills)(
      header,
      id,
      { pageSize, cursor },
      signal,
    ),
    page => ({
      state: page.state,
      observedAtUtc: page.observedAtUtc,
      nextCursor: page.nextCursor,
      gapMetadata: page.gapMetadata,
      values: page.snapshots ?? [],
    }),
  )

  function refresh() {
    return Promise.all([
      snapshots.public.refresh(),
      diffs.public.refresh(),
      skills.public.refresh(),
    ]).then(() => undefined)
  }

  const stop = watch(
    () => toValue(crossplatformId),
    () => {
      snapshots.reset()
      diffs.reset()
      skills.reset()
      void refresh()
    },
    { immediate: true },
  )

  function dispose() {
    if (disposed)
      return
    disposed = true
    stop()
    snapshots.dispose()
    diffs.dispose()
    skills.dispose()
  }

  onScopeDispose(dispose, true)

  return {
    inventorySnapshots: snapshots.public,
    inventoryDiffs: diffs.public,
    skills: skills.public,
    refresh,
    dispose,
  }
}
