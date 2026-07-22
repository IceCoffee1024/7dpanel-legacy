<script setup lang="ts">
import { useHead } from '@unhead/vue'
import { useColorMode } from '@vueuse/core'
import { computed } from 'vue'
import { useRoute } from 'vue-router'

import AppShell from './app/AppShell.vue'

const colorMode = useColorMode()
const route = useRoute()
const themeColor = computed(() => colorMode.value === 'dark' ? '#18181b' : '#ffffff')

useHead({
  meta: [
    { name: 'theme-color', content: themeColor },
  ],
})
</script>

<template>
  <UApp>
    <RouterView v-if="route.meta.public" />
    <AppShell v-else />
  </UApp>
</template>
