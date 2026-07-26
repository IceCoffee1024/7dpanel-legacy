import type { MutationCache, QueryCache } from '@pinia/colada'
import type { Pinia } from 'pinia'
import type { WatchStopHandle } from 'vue'

import { useMutationCache, useQueryCache } from '@pinia/colada'
import { watch } from 'vue'

import { useAuthStore } from '../features/auth'
import { configureGeneratedClient } from '../shared/api/generatedClient'
import { serverEvents, type ServerEventsLifecycle } from './serverEvents'

export function clearServerStateCache(
  queryCache: QueryCache,
  mutationCache: MutationCache,
): void {
  queryCache.cancelQueries()
  for (const entry of queryCache.getEntries())
    queryCache.remove(entry)
  for (const entry of mutationCache.getEntries())
    mutationCache.remove(entry)
}

export function connectServerState(
  pinia: Pinia,
  eventLifecycle: ServerEventsLifecycle = serverEvents,
): WatchStopHandle {
  const auth = useAuthStore(pinia)
  const queryCache = useQueryCache(pinia)
  const mutationCache = useMutationCache(pinia)

  configureGeneratedClient({
    getAuthorizationHeader: () => auth.authorizationHeader,
    onUnauthorized: auth.expireSession,
  })

  const stopWatch = watch(
    () => auth.authorizationHeader,
    (authorizationHeader, previousAuthorizationHeader) => {
      if (authorizationHeader === previousAuthorizationHeader)
        return
      if (previousAuthorizationHeader !== null && previousAuthorizationHeader !== undefined) {
        eventLifecycle.stop({ clearCursor: true })
        clearServerStateCache(queryCache, mutationCache)
      }
      if (authorizationHeader !== null)
        eventLifecycle.start(authorizationHeader)
    },
    { flush: 'sync', immediate: true },
  )

  return () => {
    stopWatch()
    eventLifecycle.stop({ clearCursor: true })
  }
}
