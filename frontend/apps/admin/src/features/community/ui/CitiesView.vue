<script setup lang="ts">
import type { City, CityInput } from '../api/community'
import type { CommunityController } from '../model/useCommunity'

import { shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'

import CityForm from './CityForm.vue'
import CommunityMutationAlert from './CommunityMutationAlert.vue'
import CommunityStateAlert from './CommunityStateAlert.vue'

const props = defineProps<{ controller: CommunityController }>()
const emit = defineEmits<{
  refresh: []
  save: [input: CityInput]
  dismissMutation: []
}>()
const { t } = useI18n()
const selectedCity = shallowRef<City | null>(null)

function select(city: City) {
  selectedCity.value = city
}

function clearSelection() {
  selectedCity.value = null
}
</script>

<template>
  <UDashboardPanel id="community-cities">
    <template #header>
      <UDashboardNavbar :title="t('community.cities.title')">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <UButton
            color="neutral"
            icon="i-lucide-refresh-cw"
            :label="t('community.cities.refresh')"
            variant="outline"
            :loading="props.controller.citiesState.value === 'loading' || props.controller.fullCityListState.value === 'loading'"
            @click="emit('refresh')"
          />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <UContainer class="space-y-5 py-5">
        <CommunityMutationAlert :state="props.controller.mutationState.value" @dismiss="emit('dismissMutation')" />
        <CommunityStateAlert :state="props.controller.fullCityListState.value" :subject="t('community.cities.listSubject')" @retry="emit('refresh')" />

        <section class="space-y-3" aria-labelledby="cities-heading">
          <div class="flex flex-wrap items-end justify-between gap-3">
            <div>
              <h2 id="cities-heading" class="text-base font-semibold text-highlighted">
                {{ t('community.cities.listTitle') }}
              </h2>
              <p class="text-sm text-muted">
                {{ t('community.cities.listDescription') }}
              </p>
            </div>
            <UButton
              color="neutral"
              :label="t('community.cities.create')"
              variant="outline"
              @click="clearSelection"
            />
          </div>

          <div v-if="props.controller.fullCityListState.value === 'loading' && props.controller.fullCities.value.length === 0" class="space-y-3">
            <USkeleton v-for="row in 3" :key="row" class="h-28 w-full" />
          </div>
          <UCard v-else-if="props.controller.fullCityListState.value === 'empty'">
            <p class="text-sm text-muted">
              {{ t('community.cities.listEmpty') }}
            </p>
          </UCard>
          <div v-else-if="props.controller.fullCityListState.value !== 'forbidden' && props.controller.fullCityListState.value !== 'unavailable'" class="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
            <UCard v-for="city in props.controller.fullCities.value" :key="city.cityId">
              <template #header>
                <div class="flex min-w-0 items-start justify-between gap-2">
                  <div class="min-w-0">
                    <h3 class="font-semibold text-highlighted">
                      {{ city.name }}
                    </h3><p class="break-all text-xs text-muted">
                      {{ city.cityId }}
                    </p>
                  </div>
                  <UBadge :color="city.enabled ? 'success' : 'neutral'" variant="subtle">
                    {{ city.enabled ? t('community.common.enabled') : t('community.common.disabled') }}
                  </UBadge>
                </div>
              </template>
              <p class="text-sm">
                {{ city.description || t('community.cities.noDescription') }}
              </p>
              <p class="mt-2 text-xs text-muted">
                {{ city.position.worldId }} · {{ t('community.common.coordinates', { x: city.position.x, y: city.position.y, z: city.position.z }) }} · {{ t('community.cities.sortOrder', { value: city.sortOrder }) }}
              </p>
              <template #footer>
                <div class="flex justify-end">
                  <UButton
                    color="neutral"
                    :label="t('community.common.edit')"
                    variant="outline"
                    @click="select(city)"
                  />
                </div>
              </template>
            </UCard>
          </div>
        </section>

        <CityForm
          :city="selectedCity"
          :saving="props.controller.mutationTarget.value?.kind === 'city'"
          @cancel="clearSelection"
          @save="emit('save', $event)"
        />
      </UContainer>
    </template>
  </UDashboardPanel>
</template>
