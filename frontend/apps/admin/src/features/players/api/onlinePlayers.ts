import type { PlayerSnapshot } from './playerSnapshot'
import { requestJson } from '../../../shared/api/http'

import { isRecord, parsePlayerSnapshot } from './playerSnapshot'

export type {
  PlayerSnapshot as OnlinePlayer,
  PlayerDeviceType as OnlinePlayerDeviceType,
  PlayerPosition as OnlinePlayerPosition,
  PlayerIdentity,
} from './playerSnapshot'

export interface OnlinePlayersSnapshot {
  players: readonly PlayerSnapshot[]
}

export function parseOnlinePlayers(value: unknown): OnlinePlayersSnapshot {
  if (!isRecord(value) || !Array.isArray(value.players))
    throw new Error('Invalid online players response')

  try {
    return Object.freeze({
      players: Object.freeze(value.players.map(parsePlayerSnapshot)),
    })
  }
  catch {
    throw new Error('Invalid online players response')
  }
}

export async function fetchOnlinePlayers(
  authorizationHeader: string,
  signal?: AbortSignal,
): Promise<OnlinePlayersSnapshot> {
  const response = await requestJson<unknown>('/api/v1/players/online', {
    headers: { Authorization: authorizationHeader },
    signal,
  })
  return parseOnlinePlayers(response)
}
