<script setup lang="ts">
import type { GrantOperation, GrantRewardInput } from '../../../features/rewards/api/rewards'
import { onMounted } from 'vue'
import { RewardOperationsView, useRewardOperations } from '../../../features/rewards'

const controller = useRewardOperations()
onMounted(() => {
  void controller.refresh()
})
function refresh() {
  void controller.refresh()
}
function grant(input: GrantRewardInput) {
  void controller.grant(input)
}
function confirm(operation: GrantOperation) {
  void controller.confirm(operation)
}
function refund(operation: GrantOperation) {
  void controller.refund(operation)
}
function compensate(operation: GrantOperation) {
  void controller.compensate(operation)
}
</script>

<template>
  <RewardOperationsView
    :controller="controller"
    @refresh="refresh"
    @grant="grant"
    @confirm="confirm"
    @refund="refund"
    @compensate="compensate"
  />
</template>

<route lang="json">
{ "meta": { "requiresAuth": true, "roles": ["Owner"] } }
</route>
