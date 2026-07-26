export const chatChannels = ['Global', 'Friends', 'Party', 'Whisper', 'Unknown'] as const

export type ChatChannel = typeof chatChannels[number]
export type ChatChannelFilter = 'All' | ChatChannel
export type ChatSourceKind = 'Player' | 'Administrator' | 'System'
export type ChatConnectionStatus = 'connecting' | 'live' | 'reconnecting' | 'stopped'

export interface ChatMessage {
  readonly sequence: number
  readonly occurredAtUtc: string
  readonly entityId: number
  readonly crossplatformId: string | null
  readonly senderName: string
  readonly channel: ChatChannel
  readonly sourceKind: ChatSourceKind
  readonly message: string
}
