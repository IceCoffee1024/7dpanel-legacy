<script setup lang="ts">
import type { ProfileSection, SkillSnapshot, SkillValue } from './playerProfileUi'

import { useI18n } from 'vue-i18n'

import PlayerEvidenceBadge from './PlayerEvidenceBadge.vue'

defineProps<{ section: ProfileSection<SkillSnapshot>, snapshots: readonly SkillSnapshot[] }>()
const emit = defineEmits<{ loadMore: [] }>()
const { d, t } = useI18n()

function valueLabel(skill: SkillValue): string | number {
  if (skill.state === 'Known')
    return skill.value ?? t('players.profile.skills.unknown')
  if (skill.state === 'NotLoaded')
    return t('players.profile.skills.notLoaded')
  if (skill.state === 'UnsupportedByVersion')
    return t('players.profile.skills.unsupportedByVersion')
  return t('players.profile.skills.unknown')
}
</script>

<template>
  <section class="space-y-3" aria-labelledby="player-skills-title">
    <div class="flex flex-wrap items-center justify-between gap-2">
      <h2 id="player-skills-title" class="font-semibold text-highlighted">{{ t('players.profile.skills.title') }}</h2>
      <PlayerEvidenceBadge :state="section.state" />
    </div>
    <template v-if="section.value">
      <p class="text-sm text-muted">{{ d(new Date(section.value.observedAtUtc), 'playerObservation') }} · {{ section.value.gameVersion }}</p>
      <dl class="grid grid-cols-2 gap-3 rounded-lg border border-default p-3">
        <div><dt class="text-sm text-muted">{{ t('players.fields.level') }}</dt><dd>{{ section.value.level ?? t('players.profile.skills.unknown') }}</dd></div>
        <div><dt class="text-sm text-muted">{{ t('players.fields.skillPoints') }}</dt><dd>{{ section.value.skillPoints ?? t('players.profile.skills.unknown') }}</dd></div>
      </dl>
      <div class="hidden overflow-x-auto md:block"><table class="w-full text-sm"><thead><tr class="border-b border-default text-left text-muted"><th class="p-2">{{ t('players.profile.skills.skill') }}</th><th class="p-2">{{ t('players.profile.skills.value') }}</th><th class="p-2">{{ t('players.fields.state') }}</th></tr></thead><tbody><tr v-for="skill in section.value.values" :key="skill.skillKey" class="border-b border-muted"><td class="p-2 font-mono">{{ skill.skillKey }}</td><td class="p-2">{{ valueLabel(skill) }}</td><td class="p-2">{{ skill.state }}</td></tr></tbody></table></div>
      <ul class="space-y-2 md:hidden"><li v-for="skill in section.value.values" :key="skill.skillKey" class="rounded-lg border border-default p-3"><p class="break-all font-mono text-sm">{{ skill.skillKey }}</p><p>{{ valueLabel(skill) }}</p></li></ul>
    </template>
    <UAlert v-else color="neutral" :title="t(`players.profile.section.${section.state.toLowerCase()}`)" />
    <UButton v-if="snapshots.length" color="neutral" size="sm" variant="outline" :label="t('players.history.loadMore')" @click="emit('loadMore')" />
  </section>
</template>
