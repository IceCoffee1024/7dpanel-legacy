<script setup lang="ts">
import { computed } from 'vue'

import { useServerHealth } from '../composables/useServerHealth'

const { state, data, error, lastSuccessfulAt, refresh } = useServerHealth()

const statusIcon = computed(() => {
  if (state.value === 'loading') {
    return 'i-lucide-loader-circle'
  }
  if (state.value === 'fresh') {
    return 'i-lucide-server'
  }
  if (state.value === 'stale') {
    return 'i-lucide-clock-alert'
  }
  return 'i-lucide-server-off'
})

const statusTitle = computed(() => {
  if (state.value === 'loading') {
    return '正在获取服务器状态'
  }
  if (state.value === 'fresh') {
    return '服务器运行正常'
  }
  if (state.value === 'stale') {
    return '服务器状态已过期'
  }
  return '无法获取服务器状态'
})

const statusDescription = computed(() => {
  if (state.value === 'loading') {
    return '正在连接后端健康端点。'
  }
  if (state.value === 'fresh') {
    return '最近一次健康检查已成功完成。'
  }
  if (state.value === 'stale') {
    return '保留最后一次成功结果，等待新的健康检查。'
  }
  return error.value?.code === 'http'
    ? '后端拒绝了健康检查请求。'
    : '尚未从后端获取有效的服务器状态。'
})

const lastSampleLabel = computed(() => {
  if (lastSuccessfulAt.value === null) {
    return ''
  }
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'medium',
  }).format(lastSuccessfulAt.value)
})
</script>

<template>
  <UDashboardPanel id="overview">
    <template #header>
      <UDashboardNavbar title="概览">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <section class="mx-auto flex w-full max-w-5xl flex-1 items-center justify-center py-16">
        <div class="flex max-w-md flex-col items-center text-center">
          <span
            class="mb-5 flex size-12 items-center justify-center rounded-md bg-elevated text-muted"
            :class="{ 'animate-spin': state === 'loading' }"
          >
            <UIcon :name="statusIcon" class="size-6" />
          </span>
          <h2 class="text-base font-semibold text-highlighted">
            {{ statusTitle }}
          </h2>
          <p class="mt-2 text-sm text-muted">
            {{ statusDescription }}
          </p>

          <div v-if="data" class="mt-5 flex flex-wrap justify-center gap-2 text-sm text-muted">
            <UBadge color="neutral" variant="subtle">
              {{ data.product }}
            </UBadge>
            <UBadge color="neutral" variant="subtle">
              v{{ data.version }}
            </UBadge>
          </div>

          <p v-if="lastSampleLabel" class="mt-3 text-xs text-dimmed">
            最近成功采样：{{ lastSampleLabel }}
          </p>

          <UButton
            v-if="state === 'stale' || state === 'offline'"
            class="mt-6"
            color="neutral"
            icon="i-lucide-refresh-cw"
            label="重新检查"
            variant="outline"
            @click="refresh"
          />
        </div>
      </section>
    </template>
  </UDashboardPanel>
</template>
