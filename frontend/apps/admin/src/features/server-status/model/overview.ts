export type Availability = 'available' | 'stale' | 'unavailable' | 'forbidden'
export type AdditionalMemoryKind = 'virtualAddressSpace' | 'swap'
export type RuntimeMetricWarning = 'readFailed' | 'unsupported'

export interface ObservedRuntimeMetric<T> {
  value: T | null
  source: string
  unit: string
  observedAtUtc: string
  warning: RuntimeMetricWarning | null
}

export interface GameRuntimeMetrics {
  gameDayTime: ObservedRuntimeMetric<string>
  isBloodMoon: ObservedRuntimeMetric<boolean>
  framesPerSecond: ObservedRuntimeMetric<number>
  onlinePlayerCount: ObservedRuntimeMetric<number>
  historicalPlayerCount: ObservedRuntimeMetric<number>
  animalCount: ObservedRuntimeMetric<number>
  hostileEntityCount: ObservedRuntimeMetric<number>
  activeEntityCount: ObservedRuntimeMetric<number>
  chunkCount: ObservedRuntimeMetric<number>
  droppedItemCount: ObservedRuntimeMetric<number>
  gameMemoryBytes: ObservedRuntimeMetric<number>
}

export interface GameOverview {
  availability: Availability
  sampledAtUtc: string | null
  gameTitle: string | null
  saveGameName: string | null
  worldName: string | null
  worldSessionUptimeSeconds: number | null
  version: string | null
  gameMode: string | null
  difficulty: string | null
  region: string | null
  language: string | null
  connectionAddress: string | null
  connectionPort: number | null
  maximumPlayerCount: number | null
  /** Runtime responses always include this field; optionality keeps legacy typed fixtures buildable. */
  runtimeMetrics?: GameRuntimeMetrics | null
  /** Compatibility-only fixture fields; the response parser rejects these wire aliases. */
  onlinePlayerCount?: number | null
  historicalPlayerCount?: number | null
  framesPerSecond?: number | null
  gameTime?: string | null
}

export interface HostAdditionalMemory {
  kind: AdditionalMemoryKind
  totalBytes: number | null
  usedBytes: number | null
}

export interface HostStorageVolume {
  name: string
  rootPath?: string | null
  totalBytes: number | null
  availableBytes: number | null
  isPrimaryDataVolume: boolean | null
}

export interface HostPublicNetwork {
  availability: Availability
  ipv4?: string | null
  ipv6?: string | null
}

export interface HostOverview {
  availability: Availability
  identityAvailability: Availability
  sampledAtUtc: string | null
  processUptimeSeconds: number | null
  residentSetBytes: number | null
  managedHeapBytes: number | null
  otherMemoryBytes: number | null
  cpuUsagePercent: number | null
  operatingSystem: string | null
  operatingSystemVersion: string | null
  processorCount: number | null
  memoryTotalBytes: number | null
  memoryAvailableBytes: number | null
  additionalMemory?: HostAdditionalMemory
  storageVolumes: readonly HostStorageVolume[]
  publicNetwork: HostPublicNetwork
  deviceId?: string | null
  currentSystemUser?: string | null
  osFamily: string | null
  operatingSystemArchitecture: string | null
  runtimeVersion: string | null
  cpuModel: string | null
  logicalCoreCount: number | null
  cpuFrequencyMhz: number | null
  deviceName: string | null
  deviceModel: string | null
  deviceType: string | null
  processId: number | null
  processStartedAtUtc: string | null
}

export interface RestartPolicyOverview {
  availability: Availability
  isConfigured: boolean
  scheduleDescription: string | null
  nextRestartAtUtc: string | null
}

export interface RecentActivityItem {
  occurredAtUtc: string
  messageKey: string
  messageArguments: Readonly<Record<string, string>>
}

export interface RecentActivityOverview {
  availability: Availability
  sampledAtUtc: string | null
  totalCount: number
  latestOccurredAtUtc: string | null
  items: readonly RecentActivityItem[]
}

export interface OverviewAttention {
  code: string
}

export interface OverviewSnapshot {
  availability: Availability
  game: GameOverview
  host: HostOverview
  restartPolicy: RestartPolicyOverview
  recentActivity: RecentActivityOverview
  attention: readonly OverviewAttention[]
}

export { OverviewError, parseOverview } from './overviewParser'
