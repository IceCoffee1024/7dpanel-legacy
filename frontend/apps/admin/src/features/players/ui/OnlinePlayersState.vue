<script setup lang="ts">
import type { OnlinePlayersErrorCode, OnlinePlayersState } from '../model/useOnlinePlayers'

import { computed } from 'vue'

type DisplayState = OnlinePlayersState | 'empty'

const props = withDefaults(defineProps<{
  state: DisplayState
  errorCode?: OnlinePlayersErrorCode
  capturedAtUtc?: string
}>(), {
  errorCode: null,
  capturedAtUtc: undefined,
})

defineEmits<{
  refresh: []
}>()

const content = computed(() => {
  if (props.state === 'empty') {
    return {
      icon: 'i-lucide-users',
      title: '当前没有在线玩家',
      description: props.capturedAtUtc === undefined
        ? ''
        : `快照捕获于 ${new Intl.DateTimeFormat('zh-CN', { dateStyle: 'medium', timeStyle: 'medium' }).format(new Date(props.capturedAtUtc))}`,
    }
  }
  if (props.state === 'forbidden') {
    return {
      icon: 'i-lucide-shield-alert',
      title: '无权查看在线玩家',
      description: '当前身份没有访问在线玩家数据的权限。',
    }
  }
  if (props.errorCode === 'game-not-ready') {
    return {
      icon: 'i-lucide-loader-circle',
      title: '游戏仍在加载',
      description: '服务器尚未准备好在线玩家快照，请稍后重试。',
    }
  }
  return {
    icon: 'i-lucide-wifi-off',
    title: '无法获取在线玩家',
    description: '尚未获得可显示的玩家快照，请检查服务状态后重试。',
  }
})
</script>

<template>
  <div
    v-if="state === 'loading'"
    aria-label="正在加载在线玩家"
    class="space-y-3"
    data-testid="players-loading"
  >
    <USkeleton v-for="row in 5" :key="row" class="h-14 w-full" />
  </div>

  <section
    v-else
    :data-testid="state === 'empty' ? 'players-empty' : `players-${state}`"
    class="mx-auto flex min-h-72 max-w-md flex-col items-center justify-center py-12 text-center"
  >
    <span class="mb-4 flex size-11 items-center justify-center rounded-md bg-elevated text-muted">
      <UIcon :name="content.icon" class="size-5" />
    </span>
    <h2 class="text-base font-semibold text-highlighted">
      {{ content.title }}
    </h2>
    <p v-if="content.description" class="mt-2 text-sm text-muted">
      {{ content.description }}
    </p>
    <div v-if="state === 'forbidden'" class="mt-6">
      <UButton
        color="neutral"
        icon="i-lucide-arrow-left"
        label="返回概览"
        to="/"
        variant="outline"
      />
    </div>
    <UButton
      v-else-if="state === 'offline'"
      class="mt-6"
      color="neutral"
      icon="i-lucide-refresh-cw"
      label="重新加载"
      variant="outline"
      @click="$emit('refresh')"
    />
  </section>
</template>
