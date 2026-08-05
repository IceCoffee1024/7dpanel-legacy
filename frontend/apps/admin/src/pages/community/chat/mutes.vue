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

import NavigationSectionTabs from '../../../components/navigation/NavigationSectionTabs.vue'
import { useChatMutes } from '../../../features/game-chat/model/useChatMutes'
import ChatMutesView from '../../../features/game-chat/ui/ChatMutesView.vue'

const router = useRouter()
const controller = useChatMutes({
  onSessionExpired: () => router.replace({
    path: '/login',
    query: { redirect: '/community/chat/mutes' },
  }),
})
</script>

<template>
  <NavigationSectionTabs />
  <ChatMutesView :controller="controller" />
</template>
