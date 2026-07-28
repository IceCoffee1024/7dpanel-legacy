import { requestJson } from '../../../shared/api/http'

export const TELEPORT_KINDS = ['Home', 'City', 'Friend', 'Return', 'Admin'] as const
export const TELEPORT_OPERATION_STATES = ['Reserved', 'Dispatching', 'PendingReconciliation', 'Completed', 'Failed', 'Refunded'] as const
export const VOTE_KINDS = ['Kick', 'Restart'] as const
export const VOTE_ROUND_STATES = ['Open', 'Passed', 'Rejected', 'Expired', 'Cancelled', 'ActionQueued', 'ActionSucceeded', 'ActionFailed', 'ActionResultUnknown'] as const
export const COMMUNITY_GAME_COMMAND_IDS = [
  'Balance', 'Pay', 'MoneyTop', 'Daily', 'Shop', 'Buy', 'Redeem',
  'Homes', 'SetHome', 'DeleteHome', 'Home', 'Cities', 'City',
  'TeleportAsk', 'TeleportAccept', 'TeleportReject', 'Back',
  'VoteKick', 'VoteRestart',
] as const

export type TeleportKind = typeof TELEPORT_KINDS[number]
export type TeleportOperationState = typeof TELEPORT_OPERATION_STATES[number]
export type VoteKind = typeof VOTE_KINDS[number]
export type VoteRoundState = typeof VOTE_ROUND_STATES[number]
export type VoteSettlementStatus = 'NotDue' | 'Settled' | 'AlreadySettled'
export type CommunityGameCommandId = typeof COMMUNITY_GAME_COMMAND_IDS[number]

export interface CommunityGameCommandSetting {
  readonly commandId: CommunityGameCommandId
  readonly name: string
  readonly aliases: readonly string[]
}

export interface CommunityGameCommandConfiguration {
  readonly commands: readonly CommunityGameCommandSetting[]
  readonly updatedAtUtc: string
  readonly rowVersion: bigint
}

export interface CommunityGameCommandConfigurationInput {
  readonly commands: readonly CommunityGameCommandSetting[]
}

export interface WorldPosition {
  readonly worldId: string
  readonly x: number
  readonly y: number
  readonly z: number
  readonly yaw: number
}

export interface TeleportSettings {
  readonly kind: TeleportKind
  readonly enabled: boolean
  readonly maxHomes: number | null
  readonly cooldownMs: bigint
  readonly globalCooldownMs: bigint
  readonly denyDuringBloodMoon: boolean
  readonly feeAmount: bigint
  readonly homeExperience?: HomeTeleportExperience | null
  readonly updatedAtUtc: string
  readonly rowVersion: bigint
}

export interface TeleportSettingsInput {
  readonly enabled: boolean
  readonly maxHomes: number | null
  readonly cooldownMs: bigint
  readonly globalCooldownMs: bigint
  readonly denyDuringBloodMoon: boolean
  readonly feeAmount: bigint
  readonly homeExperience?: HomeTeleportExperienceInput | null
}

export interface HomeTeleportExperience {
  readonly setFeeAmount: bigint
  readonly listCommandName: string
  readonly setCommandName: string
  readonly deleteCommandName: string
  readonly teleportCommandName: string
  readonly noHomesMessage: string
  readonly limitMessage: string
  readonly setSuccessMessage: string
  readonly overwriteMessage: string
  readonly deleteSuccessMessage: string
  readonly notFoundMessage: string
  readonly cooldownMessage: string
  readonly teleportSuccessMessage: string
  readonly setInsufficientFundsMessage: string
  readonly teleportInsufficientFundsMessage: string
  readonly bloodMoonMessage: string
}

export type HomeTeleportExperienceInput = HomeTeleportExperience

export interface PlayerHome {
  readonly homeId: string
  readonly crossplatformId: string
  readonly name: string
  readonly position: WorldPosition
  readonly createdAtUtc: string
  readonly updatedAtUtc: string
  readonly rowVersion: bigint
}

export interface City {
  readonly cityId: string
  readonly name: string
  readonly description: string
  readonly enabled: boolean
  readonly position: WorldPosition
  readonly sortOrder: number
  readonly createdAtUtc: string
  readonly updatedAtUtc: string
  readonly rowVersion: bigint
}

export interface CityInput {
  readonly cityId: string
  readonly name: string
  readonly description: string
  readonly enabled: boolean
  readonly position: WorldPosition
  readonly sortOrder: number
}

export interface FriendshipStatus {
  readonly firstCrossplatformId: string
  readonly secondCrossplatformId: string
  readonly areFriends: boolean
}

export interface FriendshipRecord {
  readonly friendshipId: string
  readonly memberACrossplatformId: string
  readonly memberBCrossplatformId: string
  readonly createdByCrossplatformId: string
  readonly acceptedAtUtc: string
}

export interface TeleportOperation {
  readonly operationId: string
  readonly kind: TeleportKind
  readonly crossplatformId: string
  readonly targetCrossplatformId: string | null
  readonly destination: WorldPosition
  readonly origin: WorldPosition | null
  readonly state: TeleportOperationState
  readonly errorCode: string | null
  readonly correlationId: string | null
  readonly createdAtUtc: string
  readonly updatedAtUtc: string
  readonly completedAtUtc: string | null
  readonly rowVersion: bigint
}

export interface VoteConfiguration {
  readonly configurationId: string
  readonly kind: VoteKind
  readonly enabled: boolean
  readonly durationMs: bigint
  readonly thresholdPercent: number
  readonly minimumParticipants: number
  readonly initiatorMinimumOnlineMs: bigint
  readonly participantMinimumOnlineMs: bigint
  readonly initiatorCooldownMs: bigint
  readonly targetCooldownMs: bigint
  readonly globalCooldownMs: bigint
  readonly mutualExclusionScope: string
  readonly allowVoteChange: boolean
  readonly updatedAtUtc: string
  readonly rowVersion: bigint
}

export interface VoteConfigurationInput {
  readonly enabled: boolean
  readonly durationMs: bigint
  readonly thresholdPercent: number
  readonly minimumParticipants: number
  readonly initiatorMinimumOnlineMs: bigint
  readonly participantMinimumOnlineMs: bigint
  readonly initiatorCooldownMs: bigint
  readonly targetCooldownMs: bigint
  readonly globalCooldownMs: bigint
  readonly mutualExclusionScope: string
  readonly allowVoteChange: boolean
}

export interface VoteRound {
  readonly roundId: string
  readonly configurationId: string
  readonly kind: VoteKind
  readonly state: VoteRoundState
  readonly initiatorCrossplatformId: string
  readonly targetCrossplatformId: string | null
  readonly scopeKey: string
  readonly eligibleCount: number
  readonly thresholdPercent: number
  readonly minimumParticipants: number
  readonly allowVoteChange: boolean
  readonly actionJobId: string | null
  readonly actionOperationId: string | null
  readonly correlationId: string | null
  readonly openedAtUtc: string
  readonly expiresAtUtc: string
  readonly settledAtUtc: string | null
  readonly actionCompletedAtUtc: string | null
  readonly rowVersion: bigint
}

export interface VoteSettlement {
  readonly status: VoteSettlementStatus
  readonly round: VoteRound
  readonly participantCount: number
  readonly yesCount: number
  readonly noCount: number
  readonly wasSettled: boolean
}

const positionKeys = ['worldId', 'x', 'y', 'z', 'yaw'] as const
const teleportSettingsKeys = ['kind', 'enabled', 'maxHomes', 'cooldownMs', 'globalCooldownMs', 'denyDuringBloodMoon', 'feeAmount', 'homeExperience', 'updatedAtUtc', 'rowVersion'] as const
const homeExperienceKeys = ['setFeeAmount', 'listCommandName', 'setCommandName', 'deleteCommandName', 'teleportCommandName', 'noHomesMessage', 'limitMessage', 'setSuccessMessage', 'overwriteMessage', 'deleteSuccessMessage', 'notFoundMessage', 'cooldownMessage', 'teleportSuccessMessage', 'setInsufficientFundsMessage', 'teleportInsufficientFundsMessage', 'bloodMoonMessage'] as const
const homeKeys = ['homeId', 'crossplatformId', 'name', 'position', 'createdAtUtc', 'updatedAtUtc', 'rowVersion'] as const
const cityKeys = ['cityId', 'name', 'description', 'enabled', 'position', 'sortOrder', 'createdAtUtc', 'updatedAtUtc', 'rowVersion'] as const
const friendshipKeys = ['firstCrossplatformId', 'secondCrossplatformId', 'areFriends'] as const
const friendshipRecordKeys = ['friendshipId', 'memberACrossplatformId', 'memberBCrossplatformId', 'createdByCrossplatformId', 'acceptedAtUtc'] as const
const operationKeys = ['operationId', 'kind', 'crossplatformId', 'targetCrossplatformId', 'destination', 'origin', 'state', 'errorCode', 'correlationId', 'createdAtUtc', 'updatedAtUtc', 'completedAtUtc', 'rowVersion'] as const
const voteConfigurationKeys = ['configurationId', 'kind', 'enabled', 'durationMs', 'thresholdPercent', 'minimumParticipants', 'initiatorMinimumOnlineMs', 'participantMinimumOnlineMs', 'initiatorCooldownMs', 'targetCooldownMs', 'globalCooldownMs', 'mutualExclusionScope', 'allowVoteChange', 'updatedAtUtc', 'rowVersion'] as const
const voteRoundKeys = ['roundId', 'configurationId', 'kind', 'state', 'initiatorCrossplatformId', 'targetCrossplatformId', 'scopeKey', 'eligibleCount', 'thresholdPercent', 'minimumParticipants', 'allowVoteChange', 'actionJobId', 'actionOperationId', 'correlationId', 'openedAtUtc', 'expiresAtUtc', 'settledAtUtc', 'actionCompletedAtUtc', 'rowVersion'] as const
const voteSettlementKeys = ['status', 'round', 'participantCount', 'yesCount', 'noCount', 'wasSettled'] as const
const gameCommandConfigurationKeys = ['commands', 'updatedAtUtc', 'rowVersion'] as const
const gameCommandSettingKeys = ['commandId', 'name', 'aliases'] as const

function invalid(): never {
  throw new Error('Invalid community response')
}

function record(value: unknown, keys: readonly string[]): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value))
    return invalid()
  const source = value as Record<string, unknown>
  const actual = Object.keys(source).sort()
  const expected = [...keys].sort()
  if (actual.length !== expected.length || actual.some((key, index) => key !== expected[index]))
    return invalid()
  return source
}

function text(value: unknown, allowEmpty = false): string {
  if (typeof value !== 'string' || (!allowEmpty && value.trim() === ''))
    return invalid()
  return value
}

function nullableText(value: unknown): string | null {
  return value === null ? null : text(value)
}

function nullableCode(value: unknown): string | null {
  if (value === null)
    return null
  const candidate = text(value)
  if (!/^[a-z][a-z0-9_]*$/.test(candidate))
    return invalid()
  return candidate
}

function bool(value: unknown): boolean {
  if (typeof value !== 'boolean')
    return invalid()
  return value
}

function finite(value: unknown): number {
  if (typeof value !== 'number' || !Number.isFinite(value))
    return invalid()
  return value
}

function integer(value: unknown, minimum = Number.MIN_SAFE_INTEGER): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < minimum)
    return invalid()
  return value
}

function long(value: unknown, minimum = 0n): bigint {
  let parsed: bigint
  if (typeof value === 'number' && Number.isSafeInteger(value))
    parsed = BigInt(value)
  else if (typeof value === 'string' && /^-?\d+$/.test(value))
    parsed = BigInt(value)
  else
    return invalid()
  if (parsed < minimum || parsed > 9_223_372_036_854_775_807n)
    return invalid()
  return parsed
}

function enumValue<T extends string>(value: unknown, values: readonly T[]): T {
  if (typeof value !== 'string' || !values.includes(value as T))
    return invalid()
  return value as T
}

function utc(value: unknown): string {
  if (typeof value !== 'string')
    return invalid()
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?(?:Z|[+]00:00)$/.exec(value)
  if (match === null)
    return invalid()
  const [year, month, day, hour, minute, second] = match.slice(1, 7).map(Number)
  const milliseconds = Number((match[7] ?? '').padEnd(3, '0').slice(0, 3))
  const timestamp = Date.parse(value)
  const parsed = new Date(timestamp)
  if (!Number.isFinite(timestamp)
    || parsed.getUTCFullYear() !== year
    || parsed.getUTCMonth() + 1 !== month
    || parsed.getUTCDate() !== day
    || parsed.getUTCHours() !== hour
    || parsed.getUTCMinutes() !== minute
    || parsed.getUTCSeconds() !== second
    || parsed.getUTCMilliseconds() !== milliseconds)
    return invalid()
  return value
}

function nullableUtc(value: unknown): string | null {
  return value === null ? null : utc(value)
}

function ensureChronology(first: string, second: string): void {
  if (Date.parse(second) < Date.parse(first))
    invalid()
}

function collection<T>(value: unknown, parser: (item: unknown) => T): readonly T[] {
  if (!Array.isArray(value))
    return invalid()
  return Object.freeze(value.map(parser))
}

function wireInteger(value: bigint): number | string {
  const candidate = Number(value)
  return Number.isSafeInteger(candidate) ? candidate : value.toString()
}

function headers(authorization: string, json = false): Record<string, string> {
  return json
    ? { Authorization: authorization, 'Content-Type': 'application/json' }
    : { Authorization: authorization }
}

function queryPath(path: string, query: Record<string, string | boolean>): string {
  const parameters = new URLSearchParams()
  for (const [key, value] of Object.entries(query))
    parameters.set(key, String(value))
  return `${path}?${parameters.toString()}`
}

export function parseWorldPosition(value: unknown): WorldPosition {
  const source = record(value, positionKeys)
  return Object.freeze({
    worldId: text(source.worldId),
    x: finite(source.x),
    y: finite(source.y),
    z: finite(source.z),
    yaw: finite(source.yaw),
  })
}

export function parseTeleportSettings(value: unknown): TeleportSettings {
  const source = record(value, teleportSettingsKeys)
  const kind = enumValue(source.kind, TELEPORT_KINDS)
  const maxHomes = source.maxHomes === null ? null : integer(source.maxHomes, 0)
  if (kind !== 'Home' && maxHomes !== null)
    return invalid()
  const homeExperience = source.homeExperience === null ? null : parseHomeExperience(source.homeExperience)
  if ((kind === 'Home') !== (homeExperience !== null))
    return invalid()
  return Object.freeze({
    kind,
    enabled: bool(source.enabled),
    maxHomes,
    cooldownMs: long(source.cooldownMs),
    globalCooldownMs: long(source.globalCooldownMs),
    denyDuringBloodMoon: bool(source.denyDuringBloodMoon),
    feeAmount: long(source.feeAmount),
    homeExperience,
    updatedAtUtc: utc(source.updatedAtUtc),
    rowVersion: long(source.rowVersion),
  })
}

function parseGameCommandSetting(value: unknown): CommunityGameCommandSetting {
  const source = record(value, gameCommandSettingKeys)
  return Object.freeze({
    commandId: enumValue(source.commandId, COMMUNITY_GAME_COMMAND_IDS),
    name: text(source.name),
    aliases: collection(source.aliases, item => text(item)),
  })
}

export function parseGameCommandConfiguration(value: unknown): CommunityGameCommandConfiguration {
  const source = record(value, gameCommandConfigurationKeys)
  const commands = collection(source.commands, parseGameCommandSetting)
  if (commands.length !== COMMUNITY_GAME_COMMAND_IDS.length
    || new Set(commands.map(command => command.commandId)).size !== COMMUNITY_GAME_COMMAND_IDS.length)
    return invalid()
  return Object.freeze({
    commands,
    updatedAtUtc: utc(source.updatedAtUtc),
    rowVersion: long(source.rowVersion),
  })
}

function parseHomeExperience(value: unknown): HomeTeleportExperience {
  const source = record(value, homeExperienceKeys)
  return Object.freeze({
    setFeeAmount: long(source.setFeeAmount),
    listCommandName: text(source.listCommandName),
    setCommandName: text(source.setCommandName),
    deleteCommandName: text(source.deleteCommandName),
    teleportCommandName: text(source.teleportCommandName),
    noHomesMessage: text(source.noHomesMessage),
    limitMessage: text(source.limitMessage),
    setSuccessMessage: text(source.setSuccessMessage),
    overwriteMessage: text(source.overwriteMessage),
    deleteSuccessMessage: text(source.deleteSuccessMessage),
    notFoundMessage: text(source.notFoundMessage),
    cooldownMessage: text(source.cooldownMessage),
    teleportSuccessMessage: text(source.teleportSuccessMessage),
    setInsufficientFundsMessage: text(source.setInsufficientFundsMessage),
    teleportInsufficientFundsMessage: text(source.teleportInsufficientFundsMessage),
    bloodMoonMessage: text(source.bloodMoonMessage),
  })
}

export function parsePlayerHome(value: unknown): PlayerHome {
  const source = record(value, homeKeys)
  const createdAtUtc = utc(source.createdAtUtc)
  const updatedAtUtc = utc(source.updatedAtUtc)
  ensureChronology(createdAtUtc, updatedAtUtc)
  return Object.freeze({
    homeId: text(source.homeId),
    crossplatformId: text(source.crossplatformId),
    name: text(source.name),
    position: parseWorldPosition(source.position),
    createdAtUtc,
    updatedAtUtc,
    rowVersion: long(source.rowVersion),
  })
}

export function parseCity(value: unknown): City {
  const source = record(value, cityKeys)
  const createdAtUtc = utc(source.createdAtUtc)
  const updatedAtUtc = utc(source.updatedAtUtc)
  ensureChronology(createdAtUtc, updatedAtUtc)
  return Object.freeze({
    cityId: text(source.cityId),
    name: text(source.name),
    description: text(source.description, true),
    enabled: bool(source.enabled),
    position: parseWorldPosition(source.position),
    sortOrder: integer(source.sortOrder),
    createdAtUtc,
    updatedAtUtc,
    rowVersion: long(source.rowVersion),
  })
}

export function parseFriendshipStatus(value: unknown): FriendshipStatus {
  const source = record(value, friendshipKeys)
  return Object.freeze({
    firstCrossplatformId: text(source.firstCrossplatformId),
    secondCrossplatformId: text(source.secondCrossplatformId),
    areFriends: bool(source.areFriends),
  })
}

export function parseFriendshipRecord(value: unknown): FriendshipRecord {
  const source = record(value, friendshipRecordKeys)
  const memberACrossplatformId = text(source.memberACrossplatformId)
  const memberBCrossplatformId = text(source.memberBCrossplatformId)
  if (memberACrossplatformId >= memberBCrossplatformId)
    return invalid()
  return Object.freeze({
    friendshipId: text(source.friendshipId),
    memberACrossplatformId,
    memberBCrossplatformId,
    createdByCrossplatformId: text(source.createdByCrossplatformId),
    acceptedAtUtc: utc(source.acceptedAtUtc),
  })
}

export function parseTeleportOperation(value: unknown): TeleportOperation {
  const source = record(value, operationKeys)
  const createdAtUtc = utc(source.createdAtUtc)
  const updatedAtUtc = utc(source.updatedAtUtc)
  const completedAtUtc = nullableUtc(source.completedAtUtc)
  ensureChronology(createdAtUtc, updatedAtUtc)
  if (completedAtUtc !== null)
    ensureChronology(createdAtUtc, completedAtUtc)
  return Object.freeze({
    operationId: text(source.operationId),
    kind: enumValue(source.kind, TELEPORT_KINDS),
    crossplatformId: text(source.crossplatformId),
    targetCrossplatformId: nullableText(source.targetCrossplatformId),
    destination: parseWorldPosition(source.destination),
    origin: source.origin === null ? null : parseWorldPosition(source.origin),
    state: enumValue(source.state, TELEPORT_OPERATION_STATES),
    errorCode: nullableCode(source.errorCode),
    correlationId: nullableText(source.correlationId),
    createdAtUtc,
    updatedAtUtc,
    completedAtUtc,
    rowVersion: long(source.rowVersion),
  })
}

export function parseVoteConfiguration(value: unknown): VoteConfiguration {
  const source = record(value, voteConfigurationKeys)
  const thresholdPercent = integer(source.thresholdPercent, 1)
  if (thresholdPercent > 100)
    return invalid()
  return Object.freeze({
    configurationId: text(source.configurationId),
    kind: enumValue(source.kind, VOTE_KINDS),
    enabled: bool(source.enabled),
    durationMs: long(source.durationMs, 1n),
    thresholdPercent,
    minimumParticipants: integer(source.minimumParticipants, 1),
    initiatorMinimumOnlineMs: long(source.initiatorMinimumOnlineMs),
    participantMinimumOnlineMs: long(source.participantMinimumOnlineMs),
    initiatorCooldownMs: long(source.initiatorCooldownMs),
    targetCooldownMs: long(source.targetCooldownMs),
    globalCooldownMs: long(source.globalCooldownMs),
    mutualExclusionScope: text(source.mutualExclusionScope),
    allowVoteChange: bool(source.allowVoteChange),
    updatedAtUtc: utc(source.updatedAtUtc),
    rowVersion: long(source.rowVersion),
  })
}

export function parseVoteRound(value: unknown): VoteRound {
  const source = record(value, voteRoundKeys)
  const openedAtUtc = utc(source.openedAtUtc)
  const expiresAtUtc = utc(source.expiresAtUtc)
  const settledAtUtc = nullableUtc(source.settledAtUtc)
  const actionCompletedAtUtc = nullableUtc(source.actionCompletedAtUtc)
  if (Date.parse(expiresAtUtc) <= Date.parse(openedAtUtc))
    return invalid()
  if (settledAtUtc !== null)
    ensureChronology(openedAtUtc, settledAtUtc)
  if (actionCompletedAtUtc !== null)
    ensureChronology(openedAtUtc, actionCompletedAtUtc)
  const thresholdPercent = integer(source.thresholdPercent, 1)
  if (thresholdPercent > 100)
    return invalid()
  return Object.freeze({
    roundId: text(source.roundId),
    configurationId: text(source.configurationId),
    kind: enumValue(source.kind, VOTE_KINDS),
    state: enumValue(source.state, VOTE_ROUND_STATES),
    initiatorCrossplatformId: text(source.initiatorCrossplatformId),
    targetCrossplatformId: nullableText(source.targetCrossplatformId),
    scopeKey: text(source.scopeKey),
    eligibleCount: integer(source.eligibleCount, 0),
    thresholdPercent,
    minimumParticipants: integer(source.minimumParticipants, 1),
    allowVoteChange: bool(source.allowVoteChange),
    actionJobId: nullableText(source.actionJobId),
    actionOperationId: nullableText(source.actionOperationId),
    correlationId: nullableText(source.correlationId),
    openedAtUtc,
    expiresAtUtc,
    settledAtUtc,
    actionCompletedAtUtc,
    rowVersion: long(source.rowVersion),
  })
}

export function parseVoteSettlement(value: unknown): VoteSettlement {
  const source = record(value, voteSettlementKeys)
  const participantCount = integer(source.participantCount, 0)
  const yesCount = integer(source.yesCount, 0)
  const noCount = integer(source.noCount, 0)
  if (yesCount + noCount !== participantCount)
    return invalid()
  return Object.freeze({
    status: enumValue(source.status, ['NotDue', 'Settled', 'AlreadySettled']),
    round: parseVoteRound(source.round),
    participantCount,
    yesCount,
    noCount,
    wasSettled: bool(source.wasSettled),
  })
}

export async function fetchTeleportSettings(authorization: string, signal?: AbortSignal): Promise<readonly TeleportSettings[]> {
  const settings = collection(await requestJson<unknown>('/api/v1/community/teleport-settings', {
    headers: headers(authorization),
    expectedStatus: 200,
    signal,
  }), parseTeleportSettings)
  if (settings.length !== TELEPORT_KINDS.length
    || new Set(settings.map(value => value.kind)).size !== TELEPORT_KINDS.length)
    return invalid()
  return settings
}

export async function fetchGameCommandConfiguration(
  authorization: string,
  signal?: AbortSignal,
): Promise<CommunityGameCommandConfiguration> {
  return parseGameCommandConfiguration(await requestJson<unknown>('/api/v1/community/game-command-configuration', {
    headers: headers(authorization),
    expectedStatus: 200,
    signal,
  }))
}

export async function updateGameCommandConfiguration(
  authorization: string,
  current: CommunityGameCommandConfiguration,
  input: CommunityGameCommandConfigurationInput,
  signal?: AbortSignal,
): Promise<CommunityGameCommandConfiguration> {
  const response = await requestJson<unknown>('/api/v1/community/game-command-configuration', {
    method: 'PUT',
    headers: headers(authorization, true),
    body: JSON.stringify({
      commands: input.commands,
      expectedRowVersion: wireInteger(current.rowVersion),
    }),
    expectedStatus: 200,
    signal,
  })
  const authoritative = parseGameCommandConfiguration(response)
  if (authoritative.rowVersion <= current.rowVersion)
    return invalid()
  return authoritative
}

export async function updateTeleportSetting(
  authorization: string,
  current: TeleportSettings,
  input: TeleportSettingsInput,
  signal?: AbortSignal,
): Promise<TeleportSettings> {
  const response = await requestJson<unknown>(`/api/v1/community/teleport-settings/${current.kind}`, {
    method: 'PUT',
    headers: headers(authorization, true),
    body: JSON.stringify({
      enabled: input.enabled,
      maxHomes: input.maxHomes,
      cooldownMs: wireInteger(input.cooldownMs),
      globalCooldownMs: wireInteger(input.globalCooldownMs),
      denyDuringBloodMoon: input.denyDuringBloodMoon,
      feeAmount: wireInteger(input.feeAmount),
      homeExperience: input.homeExperience == null
        ? null
        : { ...input.homeExperience, setFeeAmount: wireInteger(input.homeExperience.setFeeAmount) },
      expectedRowVersion: wireInteger(current.rowVersion),
    }),
    expectedStatus: 200,
    signal,
  })
  const authoritative = parseTeleportSettings(response)
  if (authoritative.kind !== current.kind || authoritative.rowVersion <= current.rowVersion)
    return invalid()
  return authoritative
}

export async function fetchHomes(authorization: string, crossplatformId: string, signal?: AbortSignal): Promise<readonly PlayerHome[]> {
  const result = collection(await requestJson<unknown>(queryPath('/api/v1/community/homes', { crossplatformId }), {
    headers: headers(authorization),
    expectedStatus: 200,
    signal,
  }), parsePlayerHome)
  if (result.some(value => value.crossplatformId !== crossplatformId))
    return invalid()
  return result
}

export async function fetchCities(authorization: string, signal?: AbortSignal): Promise<readonly City[]> {
  const result = collection(await requestJson<unknown>(queryPath('/api/v1/community/cities', { enabledOnly: true }), {
    headers: headers(authorization),
    expectedStatus: 200,
    signal,
  }), parseCity)
  if (result.some(value => !value.enabled))
    return invalid()
  return result
}

export async function fetchAllCities(authorization: string, signal?: AbortSignal): Promise<readonly City[]> {
  return collection(await requestJson<unknown>(queryPath('/api/v1/community/cities', { enabledOnly: false }), {
    headers: headers(authorization),
    expectedStatus: 200,
    signal,
  }), parseCity)
}

export async function upsertCity(authorization: string, input: CityInput, signal?: AbortSignal): Promise<City> {
  const response = await requestJson<unknown>(`/api/v1/community/cities/${encodeURIComponent(input.cityId)}`, {
    method: 'PUT',
    headers: headers(authorization, true),
    body: JSON.stringify({
      name: input.name,
      description: input.description,
      enabled: input.enabled,
      position: input.position,
      sortOrder: input.sortOrder,
    }),
    expectedStatus: 200,
    signal,
  })
  const authoritative = parseCity(response)
  if (authoritative.cityId !== input.cityId)
    return invalid()
  return authoritative
}

export async function fetchFriendship(
  authorization: string,
  firstCrossplatformId: string,
  secondCrossplatformId: string,
  signal?: AbortSignal,
): Promise<FriendshipStatus> {
  const response = await requestJson<unknown>(queryPath('/api/v1/community/friendships', {
    firstCrossplatformId,
    secondCrossplatformId,
  }), {
    headers: headers(authorization),
    expectedStatus: 200,
    signal,
  })
  const authoritative = parseFriendshipStatus(response)
  if (authoritative.firstCrossplatformId !== firstCrossplatformId
    || authoritative.secondCrossplatformId !== secondCrossplatformId)
    return invalid()
  return authoritative
}

export async function fetchFriendshipRecords(authorization: string, signal?: AbortSignal): Promise<readonly FriendshipRecord[]> {
  return collection(await requestJson<unknown>('/api/v1/community/friendships/records', {
    headers: headers(authorization),
    expectedStatus: 200,
    signal,
  }), parseFriendshipRecord)
}

export async function fetchTeleportOperation(authorization: string, operationId: string, signal?: AbortSignal): Promise<TeleportOperation> {
  const response = await requestJson<unknown>(`/api/v1/community/teleport-operations/${encodeURIComponent(operationId)}`, {
    headers: headers(authorization),
    expectedStatus: 200,
    signal,
  })
  const authoritative = parseTeleportOperation(response)
  if (authoritative.operationId !== operationId)
    return invalid()
  return authoritative
}

export async function fetchTeleportOperations(authorization: string, signal?: AbortSignal): Promise<readonly TeleportOperation[]> {
  return collection(await requestJson<unknown>('/api/v1/community/teleport-operations', {
    headers: headers(authorization),
    expectedStatus: 200,
    signal,
  }), parseTeleportOperation)
}

export async function fetchVoteConfigurations(authorization: string, signal?: AbortSignal): Promise<readonly VoteConfiguration[]> {
  return collection(await requestJson<unknown>('/api/v1/community/vote-configurations', {
    headers: headers(authorization),
    expectedStatus: 200,
    signal,
  }), parseVoteConfiguration)
}

export async function updateVoteConfiguration(
  authorization: string,
  current: VoteConfiguration,
  input: VoteConfigurationInput,
  signal?: AbortSignal,
): Promise<VoteConfiguration> {
  const response = await requestJson<unknown>(`/api/v1/community/vote-configurations/${current.kind}`, {
    method: 'PUT',
    headers: headers(authorization, true),
    body: JSON.stringify({
      enabled: input.enabled,
      durationMs: wireInteger(input.durationMs),
      thresholdPercent: input.thresholdPercent,
      minimumParticipants: input.minimumParticipants,
      initiatorMinimumOnlineMs: wireInteger(input.initiatorMinimumOnlineMs),
      participantMinimumOnlineMs: wireInteger(input.participantMinimumOnlineMs),
      initiatorCooldownMs: wireInteger(input.initiatorCooldownMs),
      targetCooldownMs: wireInteger(input.targetCooldownMs),
      globalCooldownMs: wireInteger(input.globalCooldownMs),
      mutualExclusionScope: input.mutualExclusionScope,
      allowVoteChange: input.allowVoteChange,
      expectedRowVersion: wireInteger(current.rowVersion),
    }),
    expectedStatus: 200,
    signal,
  })
  const authoritative = parseVoteConfiguration(response)
  if (authoritative.kind !== current.kind || authoritative.rowVersion <= current.rowVersion)
    return invalid()
  return authoritative
}

export async function fetchActionQueuedVoteRounds(authorization: string, signal?: AbortSignal): Promise<readonly VoteRound[]> {
  const result = collection(await requestJson<unknown>(queryPath('/api/v1/community/vote-rounds', { actionQueuedOnly: true }), {
    headers: headers(authorization),
    expectedStatus: 200,
    signal,
  }), parseVoteRound)
  if (result.some(value => value.state !== 'ActionQueued'))
    return invalid()
  return result
}

export async function fetchVoteRounds(authorization: string, signal?: AbortSignal): Promise<readonly VoteRound[]> {
  return collection(await requestJson<unknown>('/api/v1/community/vote-rounds', {
    headers: headers(authorization),
    expectedStatus: 200,
    signal,
  }), parseVoteRound)
}

export async function fetchVoteRound(authorization: string, roundId: string, signal?: AbortSignal): Promise<VoteRound> {
  const response = await requestJson<unknown>(`/api/v1/community/vote-rounds/${encodeURIComponent(roundId)}`, {
    headers: headers(authorization),
    expectedStatus: 200,
    signal,
  })
  const authoritative = parseVoteRound(response)
  if (authoritative.roundId !== roundId)
    return invalid()
  return authoritative
}

export async function settleVoteRound(authorization: string, roundId: string, signal?: AbortSignal): Promise<VoteSettlement> {
  const response = await requestJson<unknown>(`/api/v1/community/vote-rounds/${encodeURIComponent(roundId)}/settle`, {
    method: 'POST',
    headers: headers(authorization),
    expectedStatus: 200,
    signal,
  })
  const authoritative = parseVoteSettlement(response)
  if (authoritative.round.roundId !== roundId)
    return invalid()
  return authoritative
}
