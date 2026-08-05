export interface PlayerMapVisibility {
  isVisible: () => boolean
  subscribe: (listener: () => void) => () => void
}

export type PlayerMapRequestLane = 'core' | 'time' | 'history' | 'track'

export interface PlayerMapRequest {
  readonly lane: PlayerMapRequestLane
  readonly controller: AbortController
  readonly sequence: number
}

export interface PlayerMapLifecycleOptions {
  visibility: PlayerMapVisibility
  onVisible: () => void
  onInterval: () => void
}

export interface PlayerMapLifecycle {
  begin: (lane: PlayerMapRequestLane) => PlayerMapRequest
  finish: (request: PlayerMapRequest) => void
  invalidate: (lane: PlayerMapRequestLane) => void
  isCurrent: (request: PlayerMapRequest) => boolean
  isDisposed: () => boolean
  start: () => void
  dispose: () => void
}

const requestLanes: readonly PlayerMapRequestLane[] = ['core', 'time', 'history', 'track']

export function createPlayerMapLifecycle(options: PlayerMapLifecycleOptions): PlayerMapLifecycle {
  const requests = new Map<PlayerMapRequestLane, { sequence: number, current: PlayerMapRequest | null }>(
    requestLanes.map(lane => [lane, { sequence: 0, current: null }]),
  )
  let timer: ReturnType<typeof setInterval> | null = null
  let unsubscribeVisibility: (() => void) | null = null
  let started = false
  let disposed = false

  function clearTimer() {
    if (timer !== null) {
      clearInterval(timer)
      timer = null
    }
  }

  function startTimer() {
    clearTimer()
    if (!disposed && options.visibility.isVisible())
      timer = setInterval(options.onInterval, 30_000)
  }

  function invalidate(lane: PlayerMapRequestLane) {
    const state = requests.get(lane)
    if (state === undefined)
      return
    state.sequence++
    state.current?.controller.abort()
    state.current = null
  }

  function handleVisibility() {
    if (!options.visibility.isVisible()) {
      clearTimer()
      for (const lane of requestLanes)
        invalidate(lane)
      return
    }
    startTimer()
    options.onVisible()
  }

  function begin(lane: PlayerMapRequestLane): PlayerMapRequest {
    const state = requests.get(lane)
    if (state === undefined)
      throw new Error(`Unknown player map request lane: ${lane}`)
    state.current?.controller.abort()
    const request: PlayerMapRequest = Object.freeze({
      lane,
      controller: new AbortController(),
      sequence: ++state.sequence,
    })
    state.current = request
    return request
  }

  function finish(request: PlayerMapRequest) {
    const state = requests.get(request.lane)
    if (state?.current === request)
      state.current = null
  }

  function isCurrent(request: PlayerMapRequest): boolean {
    return !disposed && requests.get(request.lane)?.current === request
  }

  function start() {
    if (started || disposed)
      return
    started = true
    unsubscribeVisibility = options.visibility.subscribe(handleVisibility)
    if (options.visibility.isVisible()) {
      startTimer()
      options.onVisible()
    }
  }

  function dispose() {
    if (disposed)
      return
    disposed = true
    for (const lane of requestLanes)
      invalidate(lane)
    clearTimer()
    unsubscribeVisibility?.()
    unsubscribeVisibility = null
  }

  return { begin, finish, invalidate, isCurrent, isDisposed: () => disposed, start, dispose }
}
