<script setup lang="ts">
import type { AccountQuery, BalanceAdjustmentInput, EconomyAccount } from '../../features/economy/api/economy'

import { onMounted } from 'vue'
import { EconomyAccountsView, useEconomyAccounts } from '../../features/economy'

const controller = useEconomyAccounts()
onMounted(() => {
  void controller.refresh({ includeSystem: false })
})
function refresh(query: AccountQuery) {
  void controller.refresh(query)
}
function loadNext() {
  void controller.loadNext()
}
function setFrozen(account: EconomyAccount, isFrozen: boolean) {
  void controller.setFrozen(account, isFrozen)
}
function adjust(input: BalanceAdjustmentInput) {
  void controller.adjust(input)
}
</script>

<template>
  <EconomyAccountsView
    :controller="controller"
    @refresh="refresh"
    @load-next="loadNext"
    @set-frozen="setFrozen"
    @adjust="adjust"
  />
</template>

<route lang="json">
{ "meta": { "requiresAuth": true, "roles": ["Owner"] } }
</route>
