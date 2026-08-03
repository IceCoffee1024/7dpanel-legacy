<route lang="json">
{
  "meta": {
    "requiresAuth": true,
    "roles": ["Owner"]
  }
}
</route>

<script setup lang="ts">
import { useRouter } from 'vue-router'

import { useBackupPolicies } from '../../features/backups/model/useBackupPolicies'
import { useBackups } from '../../features/backups/model/useBackups'
import BackupsView from '../../features/backups/ui/BackupsView.vue'

const router = useRouter()
function onSessionExpired() {
  void router.replace({ path: '/login', query: { redirect: '/operations/backups' } })
}

const controller = useBackups({
  onSessionExpired,
})
const policyController = useBackupPolicies({ onSessionExpired })
</script>

<template>
  <BackupsView :controller="controller" :policy-controller="policyController" />
</template>
