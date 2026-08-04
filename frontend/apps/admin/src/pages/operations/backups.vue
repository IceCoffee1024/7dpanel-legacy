<route lang="json">
{
  "meta": {
    "requiresAuth": true,
    "roles": ["Owner"]
  }
}
</route>

<script setup lang="ts">
import { computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { useBackupPolicies } from '../../features/backups/model/useBackupPolicies'
import { useBackups } from '../../features/backups/model/useBackups'
import BackupsView from '../../features/backups/ui/BackupsView.vue'

const router = useRouter()
const route = useRoute()
function onSessionExpired() {
  void router.replace({ path: '/login', query: { redirect: '/operations/backups' } })
}

const controller = useBackups({
  onSessionExpired,
})
const policyController = useBackupPolicies({ onSessionExpired })

function operationIdFromQuery(value: unknown): string | null {
  if (typeof value === 'string' && value.trim() !== '')
    return value
  if (Array.isArray(value)) {
    const first = value.find(item => typeof item === 'string' && item.trim() !== '')
    return typeof first === 'string' ? first : null
  }
  return null
}

const operationId = computed(() => operationIdFromQuery(route.query.operationId))

watch(operationId, (nextOperationId) => {
  if (nextOperationId !== null && controller.activeJob.value?.id !== nextOperationId)
    void controller.resume(nextOperationId)
}, { immediate: true })

watch(() => controller.activeJob.value?.id, (nextOperationId) => {
  if (nextOperationId === undefined || nextOperationId === operationId.value)
    return
  void router.replace({ query: { ...route.query, operationId: nextOperationId } })
})
</script>

<template>
  <BackupsView :controller="controller" :policy-controller="policyController" />
</template>
