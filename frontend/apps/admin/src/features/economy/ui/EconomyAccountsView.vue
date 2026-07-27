<script setup lang="ts">
import type { BalanceAdjustmentInput, EconomyAccount } from '../api/economy'
import type { EconomyAccountsController } from '../model/useEconomy'

import { computed, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { formatEconomyAmount } from '../api/economy'

const props = defineProps<{ controller: EconomyAccountsController }>()
const emit = defineEmits<{
  refresh: [query: { search?: string, includeSystem: boolean }]
  loadNext: []
  setFrozen: [account: EconomyAccount, isFrozen: boolean]
  adjust: [input: BalanceAdjustmentInput]
}>()
const { t } = useI18n()

const search = shallowRef('')
const includeSystem = shallowRef(false)
const crossplatformId = shallowRef('')
const side = shallowRef<'Debit' | 'Credit'>('Credit')
const amount = shallowRef('')
const reason = shallowRef('')
const adjustmentValid = computed(() => crossplatformId.value.trim() !== '' && /^\d+$/.test(amount.value) && BigInt(amount.value || 0) > 0n && reason.value.trim() !== '')
const sideItems = computed(() => [
  { label: t('economy.accounts.adjustment.credit'), value: 'Credit' },
  { label: t('economy.accounts.adjustment.debit'), value: 'Debit' },
])

function submitSearch() {
  emit('refresh', { search: search.value.trim() || undefined, includeSystem: includeSystem.value })
}

function submitAdjustment() {
  if (!adjustmentValid.value)
    return
  emit('adjust', {
    crossplatformId: crossplatformId.value.trim(),
    playerSide: side.value,
    amount: BigInt(amount.value),
    reason: reason.value.trim(),
    clientRequestKey: crypto.randomUUID(),
  })
}

function badgeColor(account: EconomyAccount) {
  if (!account.enabled)
    return 'neutral' as const
  return account.isFrozen ? 'warning' as const : 'success' as const
}
</script>

<template>
  <UDashboardPanel id="economy-accounts">
    <template #header>
      <UDashboardNavbar :title="t('economy.accounts.title')">
        <template #leading><UDashboardSidebarCollapse /></template>
        <template #right>
          <UButton color="neutral" icon="i-lucide-refresh-cw" :label="t('economy.common.refresh')" variant="outline" :loading="props.controller.isLoading.value" @click="submitSearch" />
        </template>
      </UDashboardNavbar>
    </template>
    <template #body>
      <UContainer class="space-y-5 py-5">
        <UAlert v-if="props.controller.state.value === 'stale'" color="warning" :title="t('economy.accounts.state.stale')" :description="props.controller.errorCode.value ?? undefined" />
        <UAlert v-else-if="props.controller.state.value === 'failed' || props.controller.state.value === 'forbidden'" color="error" :title="t(props.controller.state.value === 'forbidden' ? 'economy.accounts.state.forbidden' : 'economy.accounts.state.unavailable')" :description="props.controller.errorCode.value ?? undefined" />
        <UAlert v-if="props.controller.errorCode.value && props.controller.state.value === 'fresh'" color="error" :title="t('economy.common.operationIncomplete')" :description="props.controller.errorCode.value" />

        <UCard>
          <div class="grid gap-3 md:grid-cols-[minmax(0,1fr)_auto_auto] md:items-end">
            <UFormField :label="t('economy.accounts.search.label')"><UInput v-model="search" class="w-full" :placeholder="t('economy.accounts.search.placeholder')" @keyup.enter="submitSearch" /></UFormField>
            <UCheckbox v-model="includeSystem" :label="t('economy.accounts.search.includeSystem')" />
            <UButton :label="t('economy.common.search')" icon="i-lucide-search" @click="submitSearch" />
          </div>
        </UCard>

        <div v-if="props.controller.state.value === 'loading'" class="space-y-3">
          <USkeleton v-for="row in 4" :key="row" class="h-24 w-full" />
        </div>
        <UCard v-else-if="props.controller.accounts.value.length === 0">
          <p class="text-sm text-muted">{{ t('economy.accounts.state.empty') }}</p>
        </UCard>
        <div v-else class="grid gap-3 xl:grid-cols-2">
          <UCard v-for="account in props.controller.accounts.value" :key="account.accountId">
            <template #header>
              <div class="flex flex-wrap items-center justify-between gap-2">
                <div><p class="font-semibold text-highlighted">{{ account.crossplatformId ?? account.accountId }}</p><p class="text-xs text-muted">{{ account.accountId }}</p></div>
                <UBadge :color="badgeColor(account)" variant="subtle">{{ t(!account.enabled ? 'economy.accounts.status.disabled' : account.isFrozen ? 'economy.accounts.status.frozen' : 'economy.accounts.status.normal') }}</UBadge>
              </div>
            </template>
            <dl class="grid grid-cols-3 gap-3 text-sm">
              <div><dt class="text-muted">{{ t('economy.accounts.balance.posted') }}</dt><dd class="mt-1 font-medium">{{ formatEconomyAmount(account.postedBalance) }}</dd></div>
              <div><dt class="text-muted">{{ t('economy.accounts.balance.reservedDebit') }}</dt><dd class="mt-1 font-medium">{{ formatEconomyAmount(account.reservedDebit) }}</dd></div>
              <div><dt class="text-muted">{{ t('economy.accounts.balance.available') }}</dt><dd class="mt-1 font-medium">{{ formatEconomyAmount(account.availableBalance) }}</dd></div>
            </dl>
            <template #footer>
              <div class="flex justify-end">
                <UButton v-if="account.kind === 'Player'" :color="account.isFrozen ? 'success' : 'warning'" :label="t(account.isFrozen ? 'economy.accounts.action.unfreeze' : 'economy.accounts.action.freeze')" variant="outline" :loading="props.controller.mutationAccountId.value === account.accountId" @click="emit('setFrozen', account, !account.isFrozen)" />
              </div>
            </template>
          </UCard>
        </div>
        <div v-if="props.controller.nextCursor.value" class="flex justify-center"><UButton color="neutral" :label="t('economy.common.loadMore')" variant="outline" :loading="props.controller.isLoading.value" @click="emit('loadNext')" /></div>

        <UCard>
          <template #header><div><h2 class="font-semibold">{{ t('economy.accounts.adjustment.title') }}</h2><p class="text-sm text-muted">{{ t('economy.accounts.adjustment.description') }}</p></div></template>
          <div class="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
            <UFormField :label="t('economy.accounts.adjustment.playerId')"><UInput v-model="crossplatformId" class="w-full" /></UFormField>
            <UFormField :label="t('economy.accounts.adjustment.direction')"><USelect v-model="side" class="w-full" :items="sideItems" /></UFormField>
            <UFormField :label="t('economy.accounts.adjustment.amount')"><UInput v-model="amount" class="w-full" inputmode="numeric" /></UFormField>
            <UFormField :label="t('economy.accounts.adjustment.reason')"><UInput v-model="reason" class="w-full" /></UFormField>
          </div>
          <template #footer><div class="flex justify-end"><UButton :label="t('economy.accounts.adjustment.submit')" :disabled="!adjustmentValid || props.controller.mutationAccountId.value !== null" @click="submitAdjustment" /></div></template>
        </UCard>
      </UContainer>
    </template>
  </UDashboardPanel>
</template>
