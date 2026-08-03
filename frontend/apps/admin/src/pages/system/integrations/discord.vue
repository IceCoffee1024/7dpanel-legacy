<route lang="json">
{ "meta": { "requiresAuth": true, "roles": ["Owner"] } }
</route>

<script setup lang="ts">
import { useRouter } from 'vue-router'
import { DiscordView, useDiscord } from '../../../features/discord'

const router = useRouter()
const controller = useDiscord({ onSessionExpired: () => void router.replace({ path: '/login', query: { redirect: '/system/integrations/discord' } }) })
</script>

<template>
  <DiscordView :controller="controller" />
</template>
