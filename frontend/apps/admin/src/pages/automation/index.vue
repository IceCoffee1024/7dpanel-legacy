<route lang="json">
{ "meta": { "requiresAuth": true, "roles": ["Owner"] } }
</route>

<script setup lang="ts">
import { useRouter } from 'vue-router'
import { AutomationView, useAutomation } from '../../features/automation'

const router = useRouter()
const controller = useAutomation({ onSessionExpired: () => void router.replace({ path: '/login', query: { redirect: '/automation' } }) })
</script>

<template><AutomationView :controller="controller" /></template>
