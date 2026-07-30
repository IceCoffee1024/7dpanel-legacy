<script setup lang="ts">
import type { FriendshipRecord } from '../api/community'
import type { CommunityViewState } from '../model/useCommunity'

import { useI18n } from 'vue-i18n'

import CommunityStateAlert from './CommunityStateAlert.vue'

defineProps<{
  records: readonly FriendshipRecord[]
  state: CommunityViewState
}>()

defineEmits<{ retry: [] }>()

const { t } = useI18n()
</script>

<template>
  <section class="space-y-3" aria-labelledby="friendship-records-heading">
    <div>
      <h2 id="friendship-records-heading" class="text-base font-semibold text-highlighted">
        {{ t('community.teleport.friendshipRecordsTitle') }}
      </h2>
      <p class="text-sm text-muted">
        {{ t('community.teleport.friendshipRecordsDescription') }}
      </p>
    </div>
    <CommunityStateAlert :state="state" :subject="t('community.teleport.friendshipRecordsSubject')" @retry="$emit('retry')" />
    <div v-if="state === 'loading' && records.length === 0" class="space-y-3">
      <USkeleton v-for="row in 2" :key="row" class="h-36 w-full" />
    </div>
    <UCard v-else-if="state === 'empty'">
      <p class="text-sm text-muted">
        {{ t('community.teleport.friendshipRecordsEmpty') }}
      </p>
    </UCard>
    <div v-else-if="state !== 'forbidden' && state !== 'unavailable'" class="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
      <UCard v-for="record in records" :key="record.friendshipId">
        <template #header>
          <h3 class="break-all font-semibold text-highlighted">
            {{ record.friendshipId }}
          </h3>
        </template>
        <dl class="grid gap-3 text-sm">
          <div>
            <dt class="text-muted">
              {{ t('community.teleport.friendshipMemberA') }}
            </dt><dd class="mt-1 break-all">
              {{ record.memberACrossplatformId }}
            </dd>
          </div>
          <div>
            <dt class="text-muted">
              {{ t('community.teleport.friendshipMemberB') }}
            </dt><dd class="mt-1 break-all">
              {{ record.memberBCrossplatformId }}
            </dd>
          </div>
          <div>
            <dt class="text-muted">
              {{ t('community.teleport.friendshipCreatedBy') }}
            </dt><dd class="mt-1 break-all">
              {{ record.createdByCrossplatformId }}
            </dd>
          </div>
          <div>
            <dt class="text-muted">
              {{ t('community.teleport.friendshipAcceptedAt') }}
            </dt><dd class="mt-1 break-all">
              {{ record.acceptedAtUtc }}
            </dd>
          </div>
        </dl>
      </UCard>
    </div>
  </section>
</template>
