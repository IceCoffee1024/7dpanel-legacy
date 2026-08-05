export const TELEPORT_KINDS = ['Home', 'City', 'Friend', 'Return', 'Admin'] as const
export const TELEPORT_OPERATION_STATES = ['Reserved', 'Dispatching', 'PendingReconciliation', 'Completed', 'Failed', 'Refunded'] as const
export const VOTE_KINDS = ['Kick', 'Restart'] as const
export const VOTE_ROUND_STATES = ['Open', 'Passed', 'Rejected', 'Expired', 'Cancelled', 'ActionQueued', 'ActionSucceeded', 'ActionFailed', 'ActionResultUnknown'] as const
export const COMMUNITY_GAME_COMMAND_IDS = [
  'Balance',
  'Pay',
  'MoneyTop',
  'Daily',
  'Shop',
  'Buy',
  'Redeem',
  'Homes',
  'SetHome',
  'DeleteHome',
  'Home',
  'Cities',
  'City',
  'TeleportAsk',
  'TeleportAccept',
  'TeleportReject',
  'Back',
  'VoteKick',
  'VoteRestart',
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
