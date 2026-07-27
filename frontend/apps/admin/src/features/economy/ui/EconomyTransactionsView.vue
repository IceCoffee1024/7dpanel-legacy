<script setup lang="ts">
import type { EconomyTransactionsController } from '../model/useEconomy'

import { shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { formatEconomyAmount } from '../api/economy'

const props = defineProps<{ controller: EconomyTransactionsController }>()
const emit = defineEmits<{
  refresh: [query: { relatedCrossplatformId?: string, accountId?: string, type?: string, businessKind?: string }]
  loadNext: []
}>()
const { t } = useI18n()
const playerId = shallowRef('')
const accountId = shallowRef('')
const type = shallowRef('')
const businessKind = shallowRef('')

function search() {
  emit('refresh', {
    relatedCrossplatformId: playerId.value.trim() || undefined,
    accountId: accountId.value.trim() || undefined,
    type: type.value.trim() || undefined,
    businessKind: businessKind.value.trim() || undefined,
  })
}
</script>

<template>
  <UDashboardPanel id="economy-transactions">
    <template #header>
      <UDashboardNavbar :title="t('economy.transactions.title')">
        <template #leading><UDashboardSidebarCollapse /></template>
        <template #right><UButton color="neutral" icon="i-lucide-refresh-cw" :label="t('economy.common.refresh')" variant="outline" :loading="props.controller.isLoading.value" @click="search" /></template>
      </UDashboardNavbar>
    </template>
    <template #body>
      <UContainer class="space-y-5 py-5">
        <UAlert v-if="props.controller.state.value === 'stale'" color="warning" :title="t('economy.transactions.state.stale')" :description="props.controller.errorCode.value ?? undefined" />
        <UAlert v-else-if="props.controller.state.value === 'failed' || props.controller.state.value === 'forbidden'" color="error" :title="t(props.controller.state.value === 'forbidden' ? 'economy.transactions.state.forbidden' : 'economy.transactions.state.unavailable')" :description="props.controller.errorCode.value ?? undefined" />
        <UCard>
          <div class="grid gap-3 md:grid-cols-2 xl:grid-cols-5 xl:items-end">
            <UFormField :label="t('economy.transactions.playerId')"><UInput v-model="playerId" class="w-full" /></UFormField>
            <UFormField :label="t('economy.transactions.accountId')"><UInput v-model="accountId" class="w-full" /></UFormField>
            <UFormField :label="t('economy.transactions.type')"><UInput v-model="type" class="w-full" /></UFormField>
            <UFormField :label="t('economy.transactions.businessKind')"><UInput v-model="businessKind" class="w-full" /></UFormField>
            <UButton icon="i-lucide-search" :label="t('economy.common.search')" @click="search" />
          </div>
        </UCard>
        <div v-if="props.controller.state.value === 'loading'" class="space-y-3"><USkeleton v-for="row in 5" :key="row" class="h-24 w-full" /></div>
        <UCard v-else-if="props.controller.transactions.value.length === 0"><p class="text-sm text-muted">{{ t('economy.transactions.state.empty') }}</p></UCard>
        <div v-else class="space-y-3">
          <UCard v-for="transaction in props.controller.transactions.value" :key="transaction.transactionId">
            <template #header>
              <div class="flex flex-wrap items-start justify-between gap-2">
                <div><p class="font-semibold text-highlighted">{{ transaction.type }}</p><p class="text-xs text-muted">{{ transaction.transactionId }} · {{ new Date(transaction.occurredAtUtc).toLocaleString() }}</p></div>
                <UBadge color="neutral" variant="subtle">{{ transaction.status }}</UBadge>
              </div>
            </template>
            <p v-if="transaction.reason" class="mb-3 text-sm">{{ transaction.reason }}</p>
            <div class="grid gap-2 md:grid-cols-2">
              <div v-for="entry in transaction.entries" :key="entry.entryId" class="rounded-md border border-default p-3 text-sm">
                <div class="flex justify-between gap-3"><code class="truncate text-xs text-muted">{{ entry.accountId }}</code><span :class="entry.side === 'Credit' ? 'text-success' : 'text-error'">{{ entry.side === 'Credit' ? '+' : '-' }}{{ formatEconomyAmount(entry.amount) }}</span></div>
                <p class="mt-1 text-xs text-muted">{{ t('economy.transactions.balanceAfter', { amount: formatEconomyAmount(entry.balanceAfter) }) }}</p>
              </div>
            </div>
          </UCard>
        </div>
        <div v-if="props.controller.nextCursor.value" class="flex justify-center"><UButton color="neutral" :label="t('economy.common.loadMore')" variant="outline" :loading="props.controller.isLoading.value" @click="emit('loadNext')" /></div>
      </UContainer>
    </template>
  </UDashboardPanel>
</template>
