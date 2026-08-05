import type {
  Availability,
  GameOverview,
  GameRuntimeMetrics,
  HostAdditionalMemory,
  HostOverview,
  HostPublicNetwork,
  HostStorageVolume,
  ObservedRuntimeMetric,
  OverviewAttention,
  OverviewSnapshot,
  RecentActivityItem,
  RecentActivityOverview,
  RestartPolicyOverview,
  RuntimeMetricWarning,
} from './overview'

export class OverviewError extends Error {
  readonly code = 'invalid-response' as const

  constructor() {
    super('Invalid overview response')
    this.name = 'OverviewError'
  }
}

const overviewKeys = ['availability', 'game', 'host', 'restartPolicy', 'recentActivity', 'attention'] as const
const gameKeys = [
  'availability',
  'sampledAtUtc',
  'gameTitle',
  'saveGameName',
  'worldName',
  'worldSessionUptimeSeconds',
  'version',
  'gameMode',
  'difficulty',
  'region',
  'language',
  'connectionAddress',
  'connectionPort',
  'maximumPlayerCount',
  'runtimeMetrics',
] as const
const runtimeMetricKeys = [
  'gameDayTime',
  'isBloodMoon',
  'framesPerSecond',
  'onlinePlayerCount',
  'historicalPlayerCount',
  'animalCount',
  'hostileEntityCount',
  'activeEntityCount',
  'chunkCount',
  'droppedItemCount',
  'gameMemoryBytes',
] as const
const hostKeys = [
  'availability',
  'identityAvailability',
  'sampledAtUtc',
  'processUptimeSeconds',
  'residentSetBytes',
  'managedHeapBytes',
  'otherMemoryBytes',
  'cpuUsagePercent',
  'operatingSystem',
  'operatingSystemVersion',
  'processorCount',
  'memoryTotalBytes',
  'memoryAvailableBytes',
  'additionalMemory',
  'storageVolumes',
  'publicNetwork',
  'deviceId',
  'currentSystemUser',
  'osFamily',
  'operatingSystemArchitecture',
  'runtimeVersion',
  'cpuModel',
  'logicalCoreCount',
  'cpuFrequencyMhz',
  'deviceName',
  'deviceModel',
  'deviceType',
  'processId',
  'processStartedAtUtc',
] as const

function invalid(): never {
  throw new OverviewError()
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function record(value: unknown, allowedKeys: readonly string[]): Record<string, unknown> {
  if (!isRecord(value) || Object.keys(value).some(key => !allowedKeys.includes(key)))
    invalid()
  return value
}

function hasOwn(value: Record<string, unknown>, key: string): boolean {
  return Object.getOwnPropertyDescriptor(value, key) !== undefined
}

function availability(value: unknown): Availability {
  if (value === 'available' || value === 'stale' || value === 'unavailable' || value === 'forbidden')
    return value
  return invalid()
}

function nullableString(value: unknown): string | null {
  if (value === null || typeof value === 'string')
    return value
  return invalid()
}

function requiredString(value: unknown): string {
  if (typeof value === 'string' && value.trim() !== '')
    return value
  return invalid()
}

function nullableBoolean(value: unknown): boolean | null {
  if (value === null || typeof value === 'boolean')
    return value
  return invalid()
}

function requiredBoolean(value: unknown): boolean {
  if (typeof value === 'boolean')
    return value
  return invalid()
}

function nullableNumber(value: unknown, integer = false, maximum?: number): number | null {
  if (value === null)
    return null
  if (typeof value !== 'number' || !Number.isFinite(value) || value < 0)
    return invalid()
  if (integer && !Number.isSafeInteger(value))
    return invalid()
  if (maximum !== undefined && value > maximum)
    return invalid()
  return value
}

function requiredInteger(value: unknown): number {
  const parsed = nullableNumber(value, true)
  if (parsed === null)
    return invalid()
  return parsed
}

function isValidUtcTimestamp(value: string): boolean {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?(?:Z|[+-]00:00)$/.exec(value)
  if (!match)
    return false

  const [, yearText, monthText, dayText, hourText, minuteText, secondText, fractionText] = match
  const normalized = value.endsWith('Z') ? value : `${value.slice(0, -6)}Z`
  const timestamp = Date.parse(normalized)
  if (!Number.isFinite(timestamp))
    return false

  const date = new Date(timestamp)
  const millisecond = Number((fractionText ?? '').padEnd(3, '0').slice(0, 3) || 0)
  return date.getUTCFullYear() === Number(yearText)
    && date.getUTCMonth() + 1 === Number(monthText)
    && date.getUTCDate() === Number(dayText)
    && date.getUTCHours() === Number(hourText)
    && date.getUTCMinutes() === Number(minuteText)
    && date.getUTCSeconds() === Number(secondText)
    && date.getUTCMilliseconds() === millisecond
}

function utcTimestamp(value: unknown): string {
  if (typeof value === 'string' && isValidUtcTimestamp(value))
    return value
  return invalid()
}

function nullableUtcTimestamp(value: unknown): string | null {
  if (value === null)
    return null
  return utcTimestamp(value)
}

function metricWarning(value: unknown): RuntimeMetricWarning | null {
  if (value === null || value === 'readFailed' || value === 'unsupported')
    return value
  return invalid()
}

function parseMetric<T>(
  value: unknown,
  parseValue: (candidate: unknown) => T | null,
): ObservedRuntimeMetric<T> {
  const source = record(value, ['value', 'source', 'unit', 'observedAtUtc', 'warning'])
  const parsedValue = parseValue(source.value)
  const warning = metricWarning(source.warning)
  if ((parsedValue === null) !== (warning !== null))
    invalid()

  return Object.freeze({
    value: parsedValue,
    source: requiredString(source.source),
    unit: requiredString(source.unit),
    observedAtUtc: utcTimestamp(source.observedAtUtc),
    warning,
  })
}

function parseRuntimeMetrics(value: unknown): GameRuntimeMetrics {
  const source = record(value, runtimeMetricKeys)
  const metrics = Object.freeze({
    gameDayTime: parseMetric(source.gameDayTime, nullableString),
    isBloodMoon: parseMetric(source.isBloodMoon, nullableBoolean),
    framesPerSecond: parseMetric(source.framesPerSecond, candidate => nullableNumber(candidate)),
    onlinePlayerCount: parseMetric(source.onlinePlayerCount, candidate => nullableNumber(candidate, true)),
    historicalPlayerCount: parseMetric(source.historicalPlayerCount, candidate => nullableNumber(candidate, true)),
    animalCount: parseMetric(source.animalCount, candidate => nullableNumber(candidate, true)),
    hostileEntityCount: parseMetric(source.hostileEntityCount, candidate => nullableNumber(candidate, true)),
    activeEntityCount: parseMetric(source.activeEntityCount, candidate => nullableNumber(candidate, true)),
    chunkCount: parseMetric(source.chunkCount, candidate => nullableNumber(candidate, true)),
    droppedItemCount: parseMetric(source.droppedItemCount, candidate => nullableNumber(candidate, true)),
    gameMemoryBytes: parseMetric(source.gameMemoryBytes, candidate => nullableNumber(candidate, true)),
  })
  if (new Set(Object.values(metrics).map(metric => metric.observedAtUtc)).size !== 1)
    invalid()
  return metrics
}

function parseGame(value: unknown): GameOverview {
  const source = record(value, gameKeys)
  return Object.freeze({
    availability: availability(source.availability),
    sampledAtUtc: nullableUtcTimestamp(source.sampledAtUtc),
    gameTitle: nullableString(source.gameTitle),
    saveGameName: nullableString(source.saveGameName),
    worldName: nullableString(source.worldName),
    worldSessionUptimeSeconds: nullableNumber(source.worldSessionUptimeSeconds, true),
    version: nullableString(source.version),
    gameMode: nullableString(source.gameMode),
    difficulty: nullableString(source.difficulty),
    region: nullableString(source.region),
    language: nullableString(source.language),
    connectionAddress: nullableString(source.connectionAddress),
    connectionPort: nullableNumber(source.connectionPort, true, 65_535),
    maximumPlayerCount: nullableNumber(source.maximumPlayerCount, true),
    runtimeMetrics: source.runtimeMetrics === null ? null : parseRuntimeMetrics(source.runtimeMetrics),
  })
}

function parseAdditionalMemory(value: unknown): HostAdditionalMemory {
  const source = record(value, ['kind', 'totalBytes', 'usedBytes'])
  if (source.kind !== 'virtualAddressSpace' && source.kind !== 'swap')
    invalid()
  return Object.freeze({
    kind: source.kind,
    totalBytes: nullableNumber(source.totalBytes, true),
    usedBytes: nullableNumber(source.usedBytes, true),
  })
}

function parseStorageVolume(value: unknown): HostStorageVolume {
  const source = record(value, ['name', 'rootPath', 'totalBytes', 'availableBytes', 'isPrimaryDataVolume'])
  return Object.freeze({
    name: requiredString(source.name),
    ...(hasOwn(source, 'rootPath') ? { rootPath: nullableString(source.rootPath) } : {}),
    totalBytes: nullableNumber(source.totalBytes, true),
    availableBytes: nullableNumber(source.availableBytes, true),
    isPrimaryDataVolume: nullableBoolean(source.isPrimaryDataVolume),
  })
}

function parsePublicNetwork(value: unknown): HostPublicNetwork {
  const source = record(value, ['availability', 'ipv4', 'ipv6'])
  return Object.freeze({
    availability: availability(source.availability),
    ...(hasOwn(source, 'ipv4') ? { ipv4: nullableString(source.ipv4) } : {}),
    ...(hasOwn(source, 'ipv6') ? { ipv6: nullableString(source.ipv6) } : {}),
  })
}

function parseHost(value: unknown): HostOverview {
  const source = record(value, hostKeys)
  if (!Array.isArray(source.storageVolumes))
    invalid()
  return Object.freeze({
    availability: availability(source.availability),
    identityAvailability: availability(source.identityAvailability),
    sampledAtUtc: nullableUtcTimestamp(source.sampledAtUtc),
    processUptimeSeconds: nullableNumber(source.processUptimeSeconds, true),
    residentSetBytes: nullableNumber(source.residentSetBytes, true),
    managedHeapBytes: nullableNumber(source.managedHeapBytes, true),
    otherMemoryBytes: nullableNumber(source.otherMemoryBytes, true),
    cpuUsagePercent: nullableNumber(source.cpuUsagePercent, false, 100),
    operatingSystem: nullableString(source.operatingSystem),
    operatingSystemVersion: nullableString(source.operatingSystemVersion),
    processorCount: nullableNumber(source.processorCount, true),
    memoryTotalBytes: nullableNumber(source.memoryTotalBytes, true),
    memoryAvailableBytes: nullableNumber(source.memoryAvailableBytes, true),
    ...(hasOwn(source, 'additionalMemory') ? { additionalMemory: parseAdditionalMemory(source.additionalMemory) } : {}),
    storageVolumes: Object.freeze(source.storageVolumes.map(parseStorageVolume)),
    publicNetwork: parsePublicNetwork(source.publicNetwork),
    ...(hasOwn(source, 'deviceId') ? { deviceId: nullableString(source.deviceId) } : {}),
    ...(hasOwn(source, 'currentSystemUser') ? { currentSystemUser: nullableString(source.currentSystemUser) } : {}),
    osFamily: nullableString(source.osFamily),
    operatingSystemArchitecture: nullableString(source.operatingSystemArchitecture),
    runtimeVersion: nullableString(source.runtimeVersion),
    cpuModel: nullableString(source.cpuModel),
    logicalCoreCount: nullableNumber(source.logicalCoreCount, true),
    cpuFrequencyMhz: nullableNumber(source.cpuFrequencyMhz),
    deviceName: nullableString(source.deviceName),
    deviceModel: nullableString(source.deviceModel),
    deviceType: nullableString(source.deviceType),
    processId: nullableNumber(source.processId, true),
    processStartedAtUtc: nullableUtcTimestamp(source.processStartedAtUtc),
  })
}

function parseRestartPolicy(value: unknown): RestartPolicyOverview {
  const source = record(value, ['availability', 'isConfigured', 'scheduleDescription', 'nextRestartAtUtc'])
  return Object.freeze({
    availability: availability(source.availability),
    isConfigured: requiredBoolean(source.isConfigured),
    scheduleDescription: nullableString(source.scheduleDescription),
    nextRestartAtUtc: nullableUtcTimestamp(source.nextRestartAtUtc),
  })
}

function parseMessageArguments(value: unknown): Readonly<Record<string, string>> {
  const source = record(value, Object.keys(isRecord(value) ? value : {}))
  const parsed: Record<string, string> = {}
  for (const [key, argument] of Object.entries(source)) {
    if (typeof argument !== 'string')
      invalid()
    parsed[key] = argument
  }
  return Object.freeze(parsed)
}

function parseRecentActivityItem(value: unknown): RecentActivityItem {
  const source = record(value, ['occurredAtUtc', 'messageKey', 'messageArguments'])
  return Object.freeze({
    occurredAtUtc: utcTimestamp(source.occurredAtUtc),
    messageKey: requiredString(source.messageKey),
    messageArguments: parseMessageArguments(source.messageArguments),
  })
}

function parseRecentActivity(value: unknown): RecentActivityOverview {
  const source = record(value, ['availability', 'sampledAtUtc', 'totalCount', 'latestOccurredAtUtc', 'items'])
  if (!Array.isArray(source.items))
    invalid()
  return Object.freeze({
    availability: availability(source.availability),
    sampledAtUtc: nullableUtcTimestamp(source.sampledAtUtc),
    totalCount: requiredInteger(source.totalCount),
    latestOccurredAtUtc: nullableUtcTimestamp(source.latestOccurredAtUtc),
    items: Object.freeze(source.items.map(parseRecentActivityItem)),
  })
}

function parseAttention(value: unknown): OverviewAttention {
  const source = record(value, ['code'])
  return Object.freeze({ code: requiredString(source.code) })
}

export function parseOverview(value: unknown): OverviewSnapshot {
  const source = record(value, overviewKeys)
  if (!Array.isArray(source.attention))
    invalid()
  return Object.freeze({
    availability: availability(source.availability),
    game: parseGame(source.game),
    host: parseHost(source.host),
    restartPolicy: parseRestartPolicy(source.restartPolicy),
    recentActivity: parseRecentActivity(source.recentActivity),
    attention: Object.freeze(source.attention.map(parseAttention)),
  })
}
