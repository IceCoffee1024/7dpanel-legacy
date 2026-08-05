<route lang="json">
{ "meta": { "requiresAuth": true, "roles": ["Owner"] } }
</route>

<script setup lang="ts">
import { useRouter } from 'vue-router'
import NavigationSectionTabs from '../../../components/navigation/NavigationSectionTabs.vue'
import { GeoIpView, useGeoIp } from '../../../features/geoip'

const router = useRouter()
const controller = useGeoIp({ onSessionExpired: () => void router.replace({ path: '/login', query: { redirect: '/system/integrations/geoip' } }) })
</script>

<template>
  <NavigationSectionTabs />
  <GeoIpView :controller="controller" />
</template>
