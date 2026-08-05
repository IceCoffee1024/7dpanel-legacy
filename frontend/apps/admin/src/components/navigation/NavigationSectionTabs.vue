<script setup lang="ts">
import { useNavigation } from '../../app/navigation/useNavigation'
import SectionTabs from './SectionTabs.vue'

const navigation = useNavigation()
</script>

<template>
  <SectionTabs
    v-if="navigation.sectionItems.value.length > 1"
    :items="navigation.sectionItems.value"
  />
</template>
