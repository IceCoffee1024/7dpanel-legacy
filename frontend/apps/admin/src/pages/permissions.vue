<route lang="json">
{"meta":{"requiresAuth":true,"roles":["Owner"]}}
</route>

<script setup lang="ts">
import { PermissionsView } from '../features/permissions'
</script>

<template>
  <PermissionsView />
</template>
