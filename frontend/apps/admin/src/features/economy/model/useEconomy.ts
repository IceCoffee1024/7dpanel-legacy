import type { DeepReadonly, ShallowRef } from 'vue'
import type { AccountQuery, BalanceAdjustmentInput, EconomyAccount, LedgerTransaction, TransactionQuery } from '../api/economy'

import { onUnmounted, readonly, shallowRef } from 'vue'
import { useAuthStore } from '../../auth/model/authStore'
import { HttpError } from '../../../shared/api/http'
import * as api from '../api/economy'

export type EconomyViewState = 'loading' | 'empty' | 'fresh' | 'stale' | 'failed' | 'forbidden'

interface EconomyAuth {
  readonly authorizationHeader: string | null
  expireSession: () => void
}

export interface EconomyAccountsController {
  readonly state: DeepReadonly<ShallowRef<EconomyViewState>>
  readonly accounts: DeepReadonly<ShallowRef<readonly EconomyAccount[]>>
  readonly nextCursor: DeepReadonly<ShallowRef<string | null>>
  readonly isLoading: DeepReadonly<ShallowRef<boolean>>
  readonly mutationAccountId: DeepReadonly<ShallowRef<string | null>>
  readonly errorCode: DeepReadonly<ShallowRef<string | null>>
  refresh: (query?: AccountQuery) => Promise<void>
  loadNext: () => Promise<void>
  setFrozen: (account: EconomyAccount, isFrozen: boolean) => Promise<boolean>
  adjust: (input: BalanceAdjustmentInput) => Promise<boolean>
  dispose: () => void
}

export interface EconomyTransactionsController {
  readonly state: DeepReadonly<ShallowRef<EconomyViewState>>
  readonly transactions: DeepReadonly<ShallowRef<readonly LedgerTransaction[]>>
  readonly nextCursor: DeepReadonly<ShallowRef<string | null>>
  readonly isLoading: DeepReadonly<ShallowRef<boolean>>
  readonly errorCode: DeepReadonly<ShallowRef<string | null>>
  refresh: (query?: TransactionQuery) => Promise<void>
  loadNext: () => Promise<void>
  dispose: () => void
}

interface AccountOptions {
  auth?: EconomyAuth
  fetch?: typeof api.fetchEconomyAccounts
  freeze?: typeof api.setEconomyAccountFrozen
  adjust?: typeof api.adjustEconomyBalance
}

interface TransactionOptions {
  auth?: EconomyAuth
  fetch?: typeof api.fetchEconomyTransactions
}

function errorState(error: unknown, auth: EconomyAuth, hasData: boolean): { state: EconomyViewState, code: string } {
  if (error instanceof HttpError && error.status === 401) {
    auth.expireSession()
    return { state: 'failed', code: 'session_expired' }
  }
  if (error instanceof HttpError && error.status === 403)
    return { state: 'forbidden', code: 'forbidden' }
  return { state: hasData ? 'stale' : 'failed', code: error instanceof HttpError ? (error.problemCode ?? error.code) : 'invalid_response' }
}

export function useEconomyAccounts(options: AccountOptions = {}): EconomyAccountsController {
  const auth = options.auth ?? useAuthStore()
  const fetch = options.fetch ?? api.fetchEconomyAccounts
  const freeze = options.freeze ?? api.setEconomyAccountFrozen
  const adjust = options.adjust ?? api.adjustEconomyBalance
  const state = shallowRef<EconomyViewState>('loading')
  const accounts = shallowRef<readonly EconomyAccount[]>(Object.freeze([]))
  const nextCursor = shallowRef<string | null>(null)
  const isLoading = shallowRef(false)
  const mutationAccountId = shallowRef<string | null>(null)
  const errorCode = shallowRef<string | null>(null)
  let activeQuery: AccountQuery = {}
  let request: Promise<void> | null = null
  let loadController: AbortController | null = null
  let mutationController: AbortController | null = null
  let disposed = false

  function load(query: AccountQuery, append: boolean): Promise<void> {
    if (request !== null)
      return request
    const token = auth.authorizationHeader
    if (disposed || token === null)
      return Promise.resolve()
    loadController = new AbortController()
    isLoading.value = true
    if (!append && accounts.value.length === 0)
      state.value = 'loading'
    const current = loadController
    const pending = fetch(token, query, current.signal)
      .then((page) => {
        if (disposed || current.signal.aborted)
          return
        accounts.value = Object.freeze(append ? [...accounts.value, ...page.accounts] : [...page.accounts])
        nextCursor.value = page.nextCursor
        activeQuery = { ...query, cursor: undefined }
        state.value = accounts.value.length === 0 ? 'empty' : 'fresh'
        errorCode.value = null
      })
      .catch((error: unknown) => {
        if (disposed || current.signal.aborted)
          return
        const failure = errorState(error, auth, accounts.value.length > 0)
        state.value = failure.state
        errorCode.value = failure.code
      })
      .finally(() => {
        if (request === pending) {
          request = null
          loadController = null
          isLoading.value = false
        }
      })
    request = pending
    return pending
  }

  const refresh = (query: AccountQuery = activeQuery) => load({ ...query, cursor: undefined }, false)
  const loadNext = () => nextCursor.value === null ? Promise.resolve() : load({ ...activeQuery, cursor: nextCursor.value }, true)

  async function mutate(accountId: string, action: (token: string, signal: AbortSignal) => Promise<unknown>): Promise<boolean> {
    const token = auth.authorizationHeader
    if (disposed || token === null || mutationAccountId.value !== null)
      return false
    mutationController = new AbortController()
    mutationAccountId.value = accountId
    errorCode.value = null
    try {
      await action(token, mutationController.signal)
      if (disposed || mutationController.signal.aborted)
        return false
      await refresh()
      return true
    }
    catch (error) {
      const failure = errorState(error, auth, accounts.value.length > 0)
      state.value = failure.state
      errorCode.value = failure.code
      return false
    }
    finally {
      mutationController = null
      mutationAccountId.value = null
    }
  }

  function dispose() {
    disposed = true
    loadController?.abort()
    mutationController?.abort()
  }
  onUnmounted(dispose)

  return {
    state: readonly(state), accounts: readonly(accounts), nextCursor: readonly(nextCursor),
    isLoading: readonly(isLoading), mutationAccountId: readonly(mutationAccountId), errorCode: readonly(errorCode),
    refresh, loadNext,
    setFrozen: (account, isFrozen) => mutate(account.accountId, (token, signal) => freeze(token, account, isFrozen, signal)),
    adjust: input => mutate(input.crossplatformId, (token, signal) => adjust(token, input, signal)),
    dispose,
  }
}

export function useEconomyTransactions(options: TransactionOptions = {}): EconomyTransactionsController {
  const auth = options.auth ?? useAuthStore()
  const fetch = options.fetch ?? api.fetchEconomyTransactions
  const state = shallowRef<EconomyViewState>('loading')
  const transactions = shallowRef<readonly LedgerTransaction[]>(Object.freeze([]))
  const nextCursor = shallowRef<string | null>(null)
  const isLoading = shallowRef(false)
  const errorCode = shallowRef<string | null>(null)
  let activeQuery: TransactionQuery = {}
  let request: Promise<void> | null = null
  let controller: AbortController | null = null
  let disposed = false

  function load(query: TransactionQuery, append: boolean): Promise<void> {
    if (request !== null)
      return request
    const token = auth.authorizationHeader
    if (disposed || token === null)
      return Promise.resolve()
    controller = new AbortController()
    const current = controller
    isLoading.value = true
    if (!append && transactions.value.length === 0)
      state.value = 'loading'
    const pending = fetch(token, query, current.signal)
      .then((page) => {
        if (disposed || current.signal.aborted)
          return
        transactions.value = Object.freeze(append ? [...transactions.value, ...page.transactions] : [...page.transactions])
        nextCursor.value = page.nextCursor
        activeQuery = { ...query, cursor: undefined }
        state.value = transactions.value.length === 0 ? 'empty' : 'fresh'
        errorCode.value = null
      })
      .catch((error: unknown) => {
        if (disposed || current.signal.aborted)
          return
        const failure = errorState(error, auth, transactions.value.length > 0)
        state.value = failure.state
        errorCode.value = failure.code
      })
      .finally(() => {
        if (request === pending) {
          request = null
          controller = null
          isLoading.value = false
        }
      })
    request = pending
    return pending
  }

  const refresh = (query: TransactionQuery = activeQuery) => load({ ...query, cursor: undefined }, false)
  const loadNext = () => nextCursor.value === null ? Promise.resolve() : load({ ...activeQuery, cursor: nextCursor.value }, true)
  function dispose() { disposed = true; controller?.abort() }
  onUnmounted(dispose)
  return {
    state: readonly(state), transactions: readonly(transactions), nextCursor: readonly(nextCursor),
    isLoading: readonly(isLoading), errorCode: readonly(errorCode), refresh, loadNext, dispose,
  }
}
