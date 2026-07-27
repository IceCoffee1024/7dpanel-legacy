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

import { useAuditWorkspace } from '../features/audit/model/useAuditWorkspace'
import AuditWorkspace from '../features/audit/ui/AuditWorkspace.vue'
import { useGameEvents } from '../features/game-events/model/useGameEvents'

const router = useRouter()
function onSessionExpired() {
  return router.replace({
    path: '/login',
    query: { redirect: '/audit' },
  })
}
const audit = useAuditWorkspace({ onSessionExpired })
const gameEvents = useGameEvents({ onSessionExpired })
</script>

<template>
  <AuditWorkspace :audit="audit" :game-events="gameEvents" />
</template>
