<route lang="json">
{ "meta": { "requiresAuth": true, "roles": ["Owner"] } }
</route>

<script setup lang="ts">
import { useRouter } from 'vue-router'
import { GeoIpView, useGeoIp } from '../../features/geoip'

const router = useRouter()
const controller = useGeoIp({ onSessionExpired: () => void router.replace({ path: '/login', query: { redirect: '/integrations/geoip' } }) })
</script>

<template>
  <GeoIpView :controller="controller" />
</template>
