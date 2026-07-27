import type { DeepReadonly, ShallowRef } from 'vue'
import type { AchievementDefinition, AchievementDefinitionDraft, AchievementRecord, CreateRedeemCodeInput, GeneratedRedeemCode, ManualOnlineRewardInput, OnlineRewardRecord, OnlineRewardRule, OnlineRewardRuleDraft, PurchaseProductInput, RedeemCodeDefinition, ShopProduct, ShopProductDraft, ShopPurchase } from '../api/commerce'

import { onUnmounted, readonly, shallowRef } from 'vue'
import { useAuthStore } from '../../auth/model/authStore'
import { HttpError } from '../../../shared/api/http'
import * as api from '../api/commerce'

export type CommerceState = 'idle' | 'loading' | 'empty' | 'fresh' | 'stale' | 'failed' | 'forbidden'
interface Auth { readonly authorizationHeader: string | null, expireSession: () => void }
interface BaseController { readonly state: DeepReadonly<ShallowRef<CommerceState>>, readonly isMutating: DeepReadonly<ShallowRef<boolean>>, readonly errorCode: DeepReadonly<ShallowRef<string | null>>, dispose: () => void }
export interface ShopController extends BaseController { readonly product: DeepReadonly<ShallowRef<ShopProduct | null>>, readonly purchase: DeepReadonly<ShallowRef<ShopPurchase | null>>, load: (id: string) => Promise<boolean>, save: (draft: ShopProductDraft) => Promise<boolean>, purchaseProduct: (input: PurchaseProductInput) => Promise<boolean> }
export interface RedeemCodesController extends BaseController { readonly definition: DeepReadonly<ShallowRef<RedeemCodeDefinition | null>>, readonly generated: DeepReadonly<ShallowRef<GeneratedRedeemCode | null>>, load: (id: string) => Promise<boolean>, create: (input: CreateRedeemCodeInput) => Promise<boolean>, clearGenerated: () => void }
export interface AchievementOnlineController extends BaseController { readonly achievement: DeepReadonly<ShallowRef<AchievementDefinition | null>>, readonly achievementRecord: DeepReadonly<ShallowRef<AchievementRecord | null>>, readonly rule: DeepReadonly<ShallowRef<OnlineRewardRule | null>>, readonly records: DeepReadonly<ShallowRef<readonly OnlineRewardRecord[]>>, saveAchievement: (draft: AchievementDefinitionDraft) => Promise<boolean>, loadAchievementRecord: (achievementId: string, playerId: string) => Promise<boolean>, saveRule: (draft: OnlineRewardRuleDraft) => Promise<boolean>, loadRecords: (ruleId: string, playerId: string) => Promise<boolean>, manualGrant: (input: ManualOnlineRewardInput) => Promise<boolean> }

function failure(error: unknown, auth: Auth, stale: boolean): { state: CommerceState, code: string } { if (error instanceof HttpError && error.status === 401) { auth.expireSession(); return { state: 'failed', code: 'session_expired' } } if (error instanceof HttpError && error.status === 403) return { state: 'forbidden', code: 'forbidden' }; return { state: stale ? 'stale' : 'failed', code: error instanceof HttpError ? (error.problemCode ?? error.code) : 'invalid_response' } }

function useRunner(auth: Auth) {
  const state = shallowRef<CommerceState>('idle'); const isMutating = shallowRef(false); const errorCode = shallowRef<string | null>(null)
  let request: Promise<boolean> | null = null; let controller: AbortController | null = null; let disposed = false
  function run<T>(action: (token: string, signal: AbortSignal) => Promise<T>, apply: (value: T) => void, mutating: boolean, hasData: () => boolean): Promise<boolean> {
    if (request !== null) return request
    const token = auth.authorizationHeader; if (disposed || token === null) return Promise.resolve(false)
    controller = new AbortController(); const current = controller; isMutating.value = mutating; if (!mutating) state.value = 'loading'; errorCode.value = null
    const pending = action(token, current.signal).then((value) => { if (disposed || current.signal.aborted) return false; apply(value); state.value = 'fresh'; return true }).catch((error: unknown) => { if (disposed || current.signal.aborted) return false; const result = failure(error, auth, hasData()); state.value = result.state; errorCode.value = result.code; return false }).finally(() => { if (request === pending) { request = null; controller = null; isMutating.value = false } })
    request = pending; return pending
  }
  function dispose() { disposed = true; controller?.abort() }
  onUnmounted(dispose)
  return { state, isMutating, errorCode, run, dispose }
}

export function useShopProducts(options: { auth?: Auth } = {}): ShopController {
  const auth = options.auth ?? useAuthStore(); const runner = useRunner(auth); const product = shallowRef<ShopProduct | null>(null); const purchase = shallowRef<ShopPurchase | null>(null)
  return { state: readonly(runner.state), isMutating: readonly(runner.isMutating), errorCode: readonly(runner.errorCode), product: readonly(product), purchase: readonly(purchase), load: id => runner.run((token, signal) => api.fetchShopProduct(token, id, signal), value => { product.value = value }, false, () => product.value !== null), save: draft => runner.run((token, signal) => api.saveShopProduct(token, draft, signal), value => { product.value = value }, true, () => product.value !== null), purchaseProduct: input => runner.run((token, signal) => api.purchaseShopProduct(token, input, signal), value => { purchase.value = value.purchase }, true, () => purchase.value !== null), dispose: runner.dispose }
}

export function useRedeemCodes(options: { auth?: Auth } = {}): RedeemCodesController {
  const auth = options.auth ?? useAuthStore(); const runner = useRunner(auth); const definition = shallowRef<RedeemCodeDefinition | null>(null); const generated = shallowRef<GeneratedRedeemCode | null>(null)
  return { state: readonly(runner.state), isMutating: readonly(runner.isMutating), errorCode: readonly(runner.errorCode), definition: readonly(definition), generated: readonly(generated), load: id => runner.run((token, signal) => api.fetchRedeemCode(token, id, signal), value => { definition.value = value }, false, () => definition.value !== null), create: input => runner.run((token, signal) => api.createRedeemCode(token, input, signal), value => { generated.value = value; definition.value = value.definition }, true, () => definition.value !== null), clearGenerated: () => { generated.value = null }, dispose: runner.dispose }
}

export function useAchievementOnlineRewards(options: { auth?: Auth } = {}): AchievementOnlineController {
  const auth = options.auth ?? useAuthStore(); const runner = useRunner(auth); const achievement = shallowRef<AchievementDefinition | null>(null); const achievementRecord = shallowRef<AchievementRecord | null>(null); const rule = shallowRef<OnlineRewardRule | null>(null); const records = shallowRef<readonly OnlineRewardRecord[]>(Object.freeze([]))
  const hasData = () => achievement.value !== null || rule.value !== null || records.value.length > 0
  return { state: readonly(runner.state), isMutating: readonly(runner.isMutating), errorCode: readonly(runner.errorCode), achievement: readonly(achievement), achievementRecord: readonly(achievementRecord), rule: readonly(rule), records: readonly(records), saveAchievement: draft => runner.run((token, signal) => api.saveAchievementDefinition(token, draft, signal), value => { achievement.value = value }, true, hasData), loadAchievementRecord: (id, player) => runner.run((token, signal) => api.fetchAchievementRecord(token, id, player, signal), value => { achievementRecord.value = value }, false, hasData), saveRule: draft => runner.run((token, signal) => api.saveOnlineRewardRule(token, draft, signal), value => { rule.value = value }, true, hasData), loadRecords: (id, player) => runner.run((token, signal) => api.fetchOnlineRewardRecords(token, id, player, signal), value => { records.value = Object.freeze([...value]); runner.state.value = value.length === 0 ? 'empty' : 'fresh' }, false, hasData), manualGrant: input => runner.run((token, signal) => api.grantManualOnlineReward(token, input, signal), value => { records.value = Object.freeze([value, ...records.value.filter(item => item.eligibilityId !== value.eligibilityId)]) }, true, hasData), dispose: runner.dispose }
}
