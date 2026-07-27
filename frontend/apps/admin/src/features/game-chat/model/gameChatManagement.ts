export type GameChatManagementState = 'loading' | 'empty' | 'ready' | 'stale' | 'failed' | 'forbidden'
export type ChatType = 'Global' | 'Friends' | 'Party' | 'Whisper' | 'Unknown'
export type ChatSourceKind = 'Player' | 'Administrator' | 'System'
export type PlayerColorTagPermission = 'None' | 'AdminOnly' | 'All'

export interface ChatHistoryMessage {
  sequence: number
  occurredAtUtc: string
  entityId: number
  crossplatformId: string | null
  senderName: string | null
  chatType: ChatType
  sourceKind: ChatSourceKind
  message: string
}

export interface ChatHistoryFilters {
  crossplatformId: string
  senderName: string
  chatType: ChatType | ''
  sourceKind: ChatSourceKind | ''
  startUtc: string
  endUtc: string
}

export interface ChatSettings {
  isEnabled: boolean
  globalServerName: string | null
  whisperServerName: string | null
  commandPrefixes: string[]
  excludeCommandsFromHistory: boolean
  historyRetentionDays: number
}

export interface ColoredChatSettings {
  isEnabled: boolean
  globalDefaultColor: string | null
  whisperDefaultColor: string | null
  friendsDefaultColor: string | null
  partyDefaultColor: string | null
  adminDefaultColor: string | null
  systemDefaultColor: string | null
  playerColorTagPermission: PlayerColorTagPermission
}

export interface ColoredChatProfileDraft {
  crossplatformId: string
  customName: string | null
  nameColor: string | null
  textColor: string | null
  description: string | null
}

export interface ColoredChatProfile extends ColoredChatProfileDraft {
  createdAtUtc: string
  updatedAtUtc: string
}

export interface ColoredChatPreviewContext {
  playerName: string
  playerId: string
  entityId: string | number
  chatType: ChatType
}

export const chatTypeOptions: readonly ChatType[] = ['Global', 'Friends', 'Party', 'Whisper', 'Unknown']

export const chatSourceOptions: readonly ChatSourceKind[] = ['Player', 'Administrator', 'System']

export const playerColorTagPermissionOptions: readonly PlayerColorTagPermission[] = ['None', 'AdminOnly', 'All']

export const coloredChatTemplateVariables = ['playerName', 'playerId', 'entityId', 'chatType'] as const

export function createEmptyHistoryFilters(): ChatHistoryFilters {
  return {
    crossplatformId: '',
    senderName: '',
    chatType: '',
    sourceKind: '',
    startUtc: '',
    endUtc: '',
  }
}

export function normalizeChatColor(value: string | null | undefined): string | null | undefined {
  const normalized = value?.trim().replace(/^#/, '') ?? ''
  if (normalized === '')
    return null
  if (!/^[0-9A-F]{6}$/i.test(normalized))
    return undefined
  return normalized.toUpperCase()
}

export function toChatColorStyle(value: string | null | undefined): string | undefined {
  const normalized = normalizeChatColor(value)
  return typeof normalized === 'string' ? `#${normalized}` : undefined
}

export function toChatColorPickerValue(value: string | null | undefined): string {
  return toChatColorStyle(value) ?? '#FFFFFF'
}

export function normalizeCommandPrefixes(prefixes: readonly string[]): string[] | undefined {
  const normalized = prefixes.map(prefix => prefix.trim()).filter(Boolean)
  if (normalized.some(prefix => Array.from(prefix).length !== 1 || /\s/.test(prefix)))
    return undefined
  return [...new Set(normalized)]
}

export function renderColoredChatName(template: string | null | undefined, context: ColoredChatPreviewContext): string {
  const source = template?.trim() || context.playerName
  const values: Record<(typeof coloredChatTemplateVariables)[number], string> = {
    playerName: context.playerName,
    playerId: context.playerId,
    entityId: String(context.entityId),
    chatType: context.chatType,
  }

  return source.replace(/\{(playerName|playerId|entityId|chatType)\}/gi, (_match, variable: string) => {
    const key = coloredChatTemplateVariables.find(candidate => candidate.toLowerCase() === variable.toLowerCase())
    return key === undefined ? _match : values[key]
  })
}
