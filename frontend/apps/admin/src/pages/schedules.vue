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

import { useSchedules } from '../features/schedules/model/useSchedules'
import SchedulesView from '../features/schedules/ui/SchedulesView.vue'

const router = useRouter()
const controller = useSchedules({
  onSessionExpired: () => {
    void router.replace({ path: '/login', query: { redirect: '/schedules' } })
  },
})
</script>

<template>
  <SchedulesView :controller="controller" />
</template>
