<script setup lang="ts">
import type { TransactionQuery } from '../../features/economy/api/economy'

import { onMounted } from 'vue'
import { EconomyTransactionsView, useEconomyTransactions } from '../../features/economy'

const controller = useEconomyTransactions()
onMounted(() => {
  void controller.refresh()
})
function refresh(query: TransactionQuery) {
  void controller.refresh(query)
}
function loadNext() {
  void controller.loadNext()
}
</script>

<template>
  <EconomyTransactionsView :controller="controller" @refresh="refresh" @load-next="loadNext" />
</template>

<route lang="json">
{ "meta": { "requiresAuth": true, "roles": ["Owner"] } }
</route>
