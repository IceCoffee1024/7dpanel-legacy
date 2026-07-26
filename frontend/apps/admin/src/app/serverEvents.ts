import type { StreamEvent } from '../shared/api/generated/core/serverSentEvents.gen'

import { serverEventsGet } from '../shared/api/generated/sdk.gen'

export type ServerEventType =
  | 'welcome'
  | 'console-log'
  | 'chat-message'
  | 'game-ready'
  | 'server-stopping'
  | 'gap'
  | 'heartbeat'

export interface ServerEventNotification {
  type: ServerEventType
  data: unknown
  id?: string
}

export interface ServerEventsLifecycle {
  start: (authorizationHeader: string) => void
  stop: (options?: { clearCursor?: boolean }) => void
}

export type ServerEventsConnectionStatus =
  | 'connecting'
  | 'live'
  | 'reconnecting'
  | 'stopped'

interface ServerEventStreamOptions {
  headers: HeadersInit
  onSseError: (error: unknown) => void
  onSseEvent: (event: StreamEvent<unknown>) => void
  signal: AbortSignal
  sseMaxRetryDelay: number
}

interface ServerEventStreamResult {
  stream: AsyncIterable<unknown>
}

export interface ServerEventsDependencies {
  openStream?: (options: ServerEventStreamOptions) => Promise<ServerEventStreamResult>
  reconnectDelayMs?: number
}

export interface ServerEventsController extends ServerEventsLifecycle {
  subscribe: (listener: (event: ServerEventNotification) => void) => () => void
  subscribeStatus: (listener: (status: ServerEventsConnectionStatus) => void) => () => void
}

const supportedEventTypes = new Set<ServerEventType>([
  'welcome',
  'console-log',
  'chat-message',
  'game-ready',
  'server-stopping',
  'gap',
])

async function openGeneratedStream(
  options: ServerEventStreamOptions,
): Promise<ServerEventStreamResult> {
  // OpenAPI describes the streaming response as text; named payloads are parsed by the generated SSE runtime.
  return serverEventsGet(
    options as unknown as NonNullable<Parameters<typeof serverEventsGet>[0]>,
  ) as Promise<ServerEventStreamResult>
}

function waitForReconnect(delayMs: number, signal: AbortSignal): Promise<void> {
  if (signal.aborted || delayMs <= 0)
    return Promise.resolve()

  return new Promise((resolve) => {
    const finish = () => {
      clearTimeout(timeout)
      signal.removeEventListener('abort', finish)
      resolve()
    }
    const timeout = setTimeout(finish, delayMs)
    signal.addEventListener('abort', finish, { once: true })
  })
}

export function createServerEvents(
  dependencies: ServerEventsDependencies = {},
): ServerEventsController {
  const openStream = dependencies.openStream ?? openGeneratedStream
  const reconnectDelayMs = dependencies.reconnectDelayMs ?? 3_000
  const listeners = new Set<(event: ServerEventNotification) => void>()
  const statusListeners = new Set<(status: ServerEventsConnectionStatus) => void>()
  let authorizationHeader: string | null = null
  let controller: AbortController | null = null
  let generation = 0
  let lastEventId: string | undefined
  let status: ServerEventsConnectionStatus = 'stopped'

  function setStatus(nextStatus: ServerEventsConnectionStatus): void {
    if (status === nextStatus)
      return
    status = nextStatus
    for (const listener of statusListeners)
      listener(status)
  }

  function publish(rawEvent: StreamEvent<unknown>, currentGeneration: number): void {
    if (currentGeneration !== generation)
      return

    let type: ServerEventType | null = null
    if (rawEvent.event === undefined && rawEvent.data === undefined)
      type = 'heartbeat'
    else if (supportedEventTypes.has(rawEvent.event as ServerEventType))
      type = rawEvent.event as ServerEventType
    if (type === null)
      return

    if (type === 'welcome')
      setStatus('live')

    if (rawEvent.id !== undefined)
      lastEventId = rawEvent.id === '' ? undefined : rawEvent.id

    const event: ServerEventNotification = {
      data: rawEvent.data,
      type,
      ...(rawEvent.id === undefined ? {} : { id: rawEvent.id }),
    }
    for (const listener of listeners)
      listener(event)
  }

  async function consume(
    currentGeneration: number,
    currentAuthorizationHeader: string,
    signal: AbortSignal,
  ): Promise<void> {
    while (!signal.aborted && currentGeneration === generation) {
      const headers: Record<string, string> = {
        Accept: 'text/event-stream',
        Authorization: currentAuthorizationHeader,
      }
      if (lastEventId !== undefined)
        headers['Last-Event-ID'] = lastEventId

      try {
        const result = await openStream({
          headers,
          onSseError: () => {
            if (!signal.aborted && currentGeneration === generation)
              setStatus('reconnecting')
          },
          onSseEvent: event => publish(event, currentGeneration),
          signal,
          sseMaxRetryDelay: 30_000,
        })
        for await (const _event of result.stream) {
          if (signal.aborted || currentGeneration !== generation)
            return
        }
      }
      catch {
        if (signal.aborted || currentGeneration !== generation)
          return
      }

      setStatus('reconnecting')
      await waitForReconnect(reconnectDelayMs, signal)
    }
  }

  function stop(options: { clearCursor?: boolean } = {}): void {
    generation++
    authorizationHeader = null
    controller?.abort()
    controller = null
    if (options.clearCursor ?? true)
      lastEventId = undefined
    setStatus('stopped')
  }

  function start(nextAuthorizationHeader: string): void {
    if (authorizationHeader === nextAuthorizationHeader && controller !== null)
      return

    if (authorizationHeader !== null || controller !== null)
      stop({ clearCursor: true })

    authorizationHeader = nextAuthorizationHeader
    setStatus('connecting')
    const currentController = new AbortController()
    controller = currentController
    const currentGeneration = ++generation
    void consume(currentGeneration, nextAuthorizationHeader, currentController.signal)
  }

  function subscribe(listener: (event: ServerEventNotification) => void): () => void {
    listeners.add(listener)
    return () => listeners.delete(listener)
  }

  function subscribeStatus(
    listener: (status: ServerEventsConnectionStatus) => void,
  ): () => void {
    statusListeners.add(listener)
    listener(status)
    return () => statusListeners.delete(listener)
  }

  return { start, stop, subscribe, subscribeStatus }
}

export const serverEvents = createServerEvents()

export function subscribeServerEvents(
  listener: (event: ServerEventNotification) => void,
): () => void {
  return serverEvents.subscribe(listener)
}

export function subscribeServerEventsStatus(
  listener: (status: ServerEventsConnectionStatus) => void,
): () => void {
  return serverEvents.subscribeStatus(listener)
}
