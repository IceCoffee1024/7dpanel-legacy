<script setup lang="ts">
import type { DiscordController } from '../model/useDiscord'

import { useDiscordViewForm } from './useDiscordViewForm'

const props = defineProps<{ controller: DiscordController }>()
const {
  t,
  form,
  selectedTarget,
  binding,
  botToken,
  interactionPublicKey,
  interactionEndpoint,
  modeItems,
  targetItems,
  addTarget,
  save,
  applyBotToken,
  replaceInteractionPublicKey,
  removeBinding,
} = useDiscordViewForm(props.controller)
</script>

<template>
  <UDashboardPanel id="discord-integration">
    <template #header>
      <UDashboardNavbar :title="t('discord.title')">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template><template #right>
          <UButton
            color="neutral"
            icon="i-lucide-refresh-cw"
            :label="t('discord.common.refresh')"
            variant="outline"
            :loading="controller.state.value === 'loading'"
            @click="controller.refresh"
          />
        </template>
      </UDashboardNavbar>
    </template>
    <template #body>
      <UContainer class="space-y-5 py-5">
        <USkeleton v-if="controller.state.value === 'loading'" class="h-48 w-full" />
        <UAlert v-else-if="controller.state.value === 'forbidden'" color="error" :title="t('discord.state.forbidden')" />
        <UAlert v-else-if="controller.state.value === 'failed'" color="error" :title="t('discord.state.unavailable')">
          <template #actions>
            <UButton
              color="neutral"
              :label="t('discord.common.retry')"
              variant="outline"
              @click="controller.refresh"
            />
          </template>
        </UAlert>
        <UAlert v-else-if="controller.state.value === 'stale'" color="warning" :title="t('discord.state.stale')" />
        <UAlert
          v-if="controller.errorCode.value"
          color="error"
          :title="t('discord.state.operationIncomplete')"
          :description="controller.errorCode.value"
        />

        <template v-if="controller.configuration.value">
          <UCard>
            <template #header>
              <div>
                <h2 class="font-semibold">
                  {{ t('discord.configuration.title') }}
                </h2><p class="text-sm text-muted">
                  {{ t('discord.configuration.description') }}
                </p>
              </div>
            </template>
            <UForm class="space-y-4" :state="form" @submit="save">
              <div class="grid gap-4 md:grid-cols-2">
                <UFormField :label="t('discord.configuration.enabled')">
                  <USwitch v-model="form.isEnabled" />
                </UFormField><UFormField :label="t('discord.configuration.mode')">
                  <USelect v-model="form.mode" :items="modeItems" />
                </UFormField><UFormField :label="t('discord.configuration.applicationId')">
                  <UInput v-model="form.applicationId" />
                </UFormField><UFormField :label="t('discord.configuration.guildId')">
                  <UInput v-model="form.guildId" />
                </UFormField><UFormField :label="t('discord.configuration.publicChannelId')">
                  <UInput v-model="form.publicChannelId" />
                </UFormField><UFormField :label="t('discord.configuration.botCredential')">
                  <UBadge :color="controller.configuration.value.hasBotToken ? 'success' : 'neutral'" :label="t(controller.configuration.value.hasBotToken ? 'discord.common.configured' : 'discord.common.notConfigured')" />
                </UFormField><UFormField :label="t('discord.configuration.gameToDiscord')">
                  <USwitch v-model="form.bridgeGameToDiscord" />
                </UFormField><UFormField :label="t('discord.configuration.discordToGame')">
                  <USwitch v-model="form.bridgeDiscordToGame" />
                </UFormField><UFormField :label="t('discord.configuration.proxyEnabled')">
                  <USwitch v-model="form.proxyEnabled" />
                </UFormField><UFormField v-if="form.proxyEnabled" :label="t('discord.configuration.proxyEndpoint')">
                  <UInput v-model="form.proxyEndpoint" type="url" />
                </UFormField>
              </div>
              <div class="space-y-3">
                <div class="flex items-center justify-between">
                  <h3 class="font-medium">
                    {{ t('discord.targets.title') }}
                  </h3><UButton
                    color="neutral"
                    icon="i-lucide-plus"
                    :label="t('discord.targets.add')"
                    size="sm"
                    variant="outline"
                    @click="addTarget"
                  />
                </div><div v-for="(target, index) in form.targets" :key="index" class="grid gap-3 rounded-lg border border-default p-3 md:grid-cols-4">
                  <UInput v-model="target.targetKey" :placeholder="t('discord.targets.keyPlaceholder')" /><USelect v-model="target.deliveryMode" :items="modeItems" /><UInput v-model="target.channelId" :placeholder="t('discord.targets.channelPlaceholder')" /><div class="flex items-center justify-between gap-2">
                    <USwitch v-model="target.isEnabled" /><UBadge :color="controller.configuration.value.targets[index]?.hasCredential ? 'success' : 'neutral'" :label="t(controller.configuration.value.targets[index]?.hasCredential ? 'discord.targets.credentialConfigured' : 'discord.targets.noCredential')" />
                  </div>
                </div>
              </div>
              <div class="flex justify-end">
                <UButton :label="t('discord.configuration.save')" type="submit" :loading="controller.isMutating.value" />
              </div>
            </UForm>
            <section class="mt-5 border-t border-default pt-4" data-testid="discord-secret-bot-token">
              <div class="flex flex-wrap items-center justify-between gap-2">
                <div>
                  <h3 class="font-medium">
                    {{ t('discord.secrets.botToken') }}
                  </h3><p class="text-sm text-muted">
                    {{ t('discord.secrets.submissionOnly') }}
                  </p>
                </div>
                <UBadge :color="controller.configuration.value.hasBotToken ? 'success' : 'neutral'" :label="t(controller.configuration.value.hasBotToken ? 'discord.common.configured' : 'discord.common.notConfigured')" />
              </div>
              <div class="mt-3 grid gap-3 md:grid-cols-[minmax(0,1fr)_auto]">
                <UInput
                  v-model="botToken.value"
                  autocomplete="off"
                  :disabled="botToken.operation !== 'Replace'"
                  type="password"
                />
                <div class="flex flex-wrap gap-2">
                  <UButton
                    data-testid="secret-replace"
                    color="neutral"
                    :label="t('discord.secrets.replace')"
                    size="sm"
                    type="button"
                    :variant="botToken.operation === 'Replace' ? 'solid' : 'outline'"
                    @click="botToken.operation = 'Replace'"
                  /><UButton
                    data-testid="secret-clear"
                    color="error"
                    :label="t('discord.secrets.clear')"
                    size="sm"
                    type="button"
                    :variant="botToken.operation === 'Clear' ? 'solid' : 'outline'"
                    @click="botToken.operation = 'Clear'"
                  />
                </div>
              </div>
              <label v-if="botToken.operation === 'Clear'" class="mt-3 flex items-center gap-2 text-sm"><input v-model="botToken.clearConfirmed" data-testid="secret-clear-confirm" type="checkbox">{{ t('discord.secrets.clearConfirm') }}</label>
              <div class="mt-3 flex justify-end">
                <UButton
                  data-testid="secret-apply"
                  :disabled="botToken.operation === 'Keep' || (botToken.operation === 'Replace' ? !botToken.value.trim() : !botToken.clearConfirmed)"
                  :label="t('discord.secrets.apply')"
                  type="button"
                  :loading="controller.isMutating.value"
                  @click="applyBotToken"
                />
              </div>
            </section>
          </UCard>

          <UCard data-testid="discord-inbound-transport">
            <template #header>
              <div>
                <h2 class="font-semibold">
                  {{ t('discord.inbound.title') }}
                </h2><p class="text-sm text-muted">
                  {{ t('discord.inbound.transportDescription') }}
                </p>
              </div>
            </template>
            <div class="space-y-4">
              <UFormField :label="t('discord.inbound.endpoint')">
                <code class="block break-all rounded bg-elevated px-3 py-2 text-sm">{{ interactionEndpoint }}</code><template #hint>
                  <span class="text-sm text-muted">{{ t('discord.inbound.endpointHelp') }}</span>
                </template>
              </UFormField><UFormField :label="t('discord.inbound.publicKey')">
                <div class="flex flex-col gap-3 sm:flex-row">
                  <UInput v-model="interactionPublicKey.value" autocomplete="off" type="password" /><UButton
                    data-testid="interaction-public-key-apply"
                    :disabled="!interactionPublicKey.value.trim()"
                    :label="t('discord.inbound.replacePublicKey')"
                    type="button"
                    :loading="controller.isMutating.value"
                    @click="replaceInteractionPublicKey"
                  />
                </div><template #hint>
                  <span class="text-sm text-muted">{{ t('discord.secrets.submissionOnly') }}</span>
                </template>
              </UFormField>
            </div>
          </UCard>

          <UCard>
            <template #header>
              <h2 class="font-semibold">
                {{ t('discord.health.title') }}
              </h2>
            </template><UAlert v-if="controller.healthState.value === 'unavailable'" color="warning" :title="t('discord.health.unavailable')" /><div v-else-if="controller.healthState.value === 'loading'" class="text-sm text-muted">
              {{ t('discord.common.loading') }}
            </div><div v-else-if="controller.health.value" class="grid gap-3 sm:grid-cols-2">
              <div class="rounded-lg border border-default p-3">
                <div class="flex items-center justify-between gap-2">
                  <span class="font-medium">{{ t('discord.health.gateway') }}</span><UBadge :label="controller.health.value.gateway.state" />
                </div><p v-if="controller.health.value.gateway.errorCode" class="mt-2 break-words text-sm text-muted">
                  {{ controller.health.value.gateway.errorCode }}
                </p>
              </div><div class="rounded-lg border border-default p-3">
                <div class="flex items-center justify-between gap-2">
                  <span class="font-medium">{{ t('discord.health.inbound') }}</span><UBadge :label="controller.health.value.inbound.state" />
                </div><p v-if="controller.health.value.inbound.errorCode" class="mt-2 break-words text-sm text-muted">
                  {{ controller.health.value.inbound.errorCode }}
                </p>
              </div>
            </div>
          </UCard>

          <UCard>
            <template #header>
              <div>
                <h2 class="font-semibold">
                  {{ t('discord.testDelivery.title') }}
                </h2><p class="text-sm text-muted">
                  {{ t('discord.testDelivery.description') }}
                </p>
              </div>
            </template><div class="flex flex-col gap-3 sm:flex-row">
              <USelect
                v-model="selectedTarget.key"
                class="min-w-56"
                :items="targetItems"
                :placeholder="t('discord.testDelivery.selectTarget')"
              /><UButton
                :label="t('discord.testDelivery.send')"
                :disabled="!selectedTarget.key"
                :loading="controller.isMutating.value"
                @click="controller.testDelivery(selectedTarget.key)"
              />
            </div><UAlert
              v-if="controller.lastDelivery.value"
              class="mt-4"
              color="success"
              :title="t('discord.testDelivery.accepted', { status: controller.lastDelivery.value.status })"
              :description="controller.lastDelivery.value.deliveryId"
            />
          </UCard>

          <div class="grid gap-5 xl:grid-cols-2">
            <UCard>
              <template #header>
                <h2 class="font-semibold">
                  {{ t('discord.delivery.title') }}
                </h2>
              </template><UAlert
                v-if="controller.deliveryState.value === 'unavailable'"
                color="warning"
                :title="t('discord.delivery.unavailableTitle')"
                :description="t('discord.delivery.unavailableDescription')"
              /><div v-else-if="controller.deliveryState.value === 'loading'" class="text-sm text-muted">
                {{ t('discord.common.loading') }}
              </div><div v-else-if="controller.deliveries.value.length === 0" class="text-sm text-muted">
                {{ t('discord.delivery.empty') }}
              </div><div v-else class="space-y-3">
                <div v-for="delivery in controller.deliveries.value" :key="delivery.deliveryId" class="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-default p-3">
                  <div>
                    <div class="font-medium">
                      {{ delivery.targetKey }}
                    </div><div class="text-xs text-muted">
                      {{ t('discord.delivery.retryCount', { deliveryId: delivery.deliveryId, count: delivery.retryCount }) }}
                    </div>
                  </div><div class="flex items-center gap-2">
                    <UBadge :color="delivery.status === 'Succeeded' ? 'success' : delivery.status === 'Failed' || delivery.status === 'ResultUnknown' ? 'warning' : 'neutral'" :label="delivery.status" /><UButton
                      v-if="delivery.status === 'Failed' || delivery.status === 'ResultUnknown'"
                      color="neutral"
                      :label="t('discord.common.retry')"
                      size="xs"
                      variant="outline"
                      @click="controller.retryDelivery(delivery.deliveryId)"
                    />
                  </div>
                </div>
              </div>
            </UCard>
            <UCard>
              <template #header>
                <h2 class="font-semibold">
                  {{ t('discord.binding.title') }}
                </h2>
              </template><UAlert v-if="controller.bindingState.value === 'unavailable'" color="warning" :title="t('discord.binding.unavailable')" /><div v-else-if="controller.bindingState.value === 'loading'" class="text-sm text-muted">
                {{ t('discord.common.loading') }}
              </div><div v-else-if="controller.bindings.value.length === 0" class="text-sm text-muted">
                {{ t('discord.binding.empty') }}
              </div><div v-else class="space-y-2">
                <div v-for="item in controller.bindings.value" :key="item.discordSubject" class="flex items-center justify-between gap-3 rounded-lg border border-default p-3">
                  <div>
                    <div class="font-medium">
                      {{ item.crossplatformId }}
                    </div><div class="text-xs text-muted">
                      {{ t('discord.binding.subject', { discordSubject: item.discordSubject }) }}
                    </div>
                  </div><UButton
                    color="error"
                    :label="t('discord.binding.remove')"
                    size="xs"
                    variant="soft"
                    @click="removeBinding(item.discordSubject)"
                  />
                </div>
              </div><div class="mt-4 flex flex-col gap-3 sm:flex-row">
                <UInput v-model="binding.crossplatformId" :placeholder="t('discord.binding.playerPlaceholder')" /><UButton
                  :label="t('discord.binding.createCode')"
                  :disabled="!binding.crossplatformId.trim()"
                  :loading="controller.isMutating.value"
                  @click="controller.createBindingCode(binding.crossplatformId)"
                />
              </div><UAlert
                v-if="controller.bindingCode.value"
                class="mt-4"
                color="warning"
                :title="t('discord.binding.codeOnce')"
              >
                <template #description>
                  <div class="space-y-1">
                    <div class="font-mono text-lg">
                      {{ controller.bindingCode.value.code }}
                    </div><div>{{ t('discord.binding.expiresAt', { time: new Date(controller.bindingCode.value.expiresAtUtc).toLocaleString() }) }}</div><UButton
                      color="neutral"
                      :label="t('discord.binding.hide')"
                      size="xs"
                      variant="outline"
                      @click="controller.clearBindingCode"
                    />
                  </div>
                </template>
              </UAlert>
            </UCard>
          </div>

          <UCard>
            <template #header>
              <h2 class="font-semibold">
                {{ t('discord.commands.title') }}
              </h2>
            </template><UAlert v-if="controller.commandState.value === 'unavailable'" color="warning" :title="t('discord.commands.unavailable')" /><div v-else-if="controller.commands.value.length === 0" class="text-sm text-muted">
              {{ t('discord.commands.empty') }}
            </div><div v-else class="grid gap-3 sm:grid-cols-3">
              <div v-for="command in controller.commands.value" :key="command.commandKey" class="rounded-lg border border-default p-3">
                <div class="font-mono font-medium">
                  /{{ command.commandKey }}
                </div><div class="mt-2 flex gap-2">
                  <UBadge :color="command.isEnabled ? 'success' : 'neutral'" :label="t(command.isEnabled ? 'discord.commands.enabled' : 'discord.commands.disabled')" /><UBadge color="neutral" :label="t(command.remoteAllowed ? 'discord.commands.remoteAllowed' : 'discord.commands.bindingOnly')" />
                </div>
              </div>
            </div>
          </UCard>
        </template>
      </UContainer>
    </template>
  </UDashboardPanel>
</template>
