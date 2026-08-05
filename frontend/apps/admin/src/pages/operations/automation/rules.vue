<route lang="json">
{ "meta": { "requiresAuth": true, "roles": ["Owner"] } }
</route>

<script setup lang="ts">
import { useRouter } from 'vue-router'
import NavigationSectionTabs from '../../../components/navigation/NavigationSectionTabs.vue'
import { AutomationView, useAutomation } from '../../../features/automation'

const router = useRouter()
const controller = useAutomation({ onSessionExpired: () => void router.replace({ path: '/login', query: { redirect: '/operations/automation/rules' } }) })
</script>

<template>
  <NavigationSectionTabs />
  <AutomationView :controller="controller" />
</template>
