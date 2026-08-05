import type { DiscordConfigurationDraft, DiscordMode, SecretOperation } from '../api/discord'
import type { DiscordController } from '../model/useDiscord'

import { computed, reactive, watch } from 'vue'
import { useI18n } from 'vue-i18n'

export function useDiscordViewForm(controller: DiscordController) {
  const { t } = useI18n()
  const form = reactive({ isEnabled: false, mode: 'Webhook' as DiscordMode, applicationId: '', guildId: '', publicChannelId: '', bridgeGameToDiscord: false, bridgeDiscordToGame: false, proxyEnabled: false, proxyEndpoint: '', targets: [] as Array<{ targetKey: string, deliveryMode: DiscordMode, channelId: string, isEnabled: boolean }> })
  const selectedTarget = reactive({ key: '' })
  const binding = reactive({ crossplatformId: '' })
  const botToken = reactive({ operation: 'Keep' as SecretOperation['operation'], value: '', clearConfirmed: false })
  const interactionPublicKey = reactive({ value: '' })
  const interactionEndpoint = '/api/v1/integrations/discord/interactions'
  const modeItems = computed(() => [
    { label: t('discord.mode.Webhook'), value: 'Webhook' },
    { label: t('discord.mode.Bot'), value: 'Bot' },
  ])
  const targetItems = computed(() => form.targets.map(target => ({ label: target.targetKey, value: target.targetKey })))

  watch(() => controller.configuration.value, (configuration) => {
    if (configuration === null)
      return
    Object.assign(form, { isEnabled: configuration.isEnabled, mode: configuration.mode, applicationId: configuration.applicationId ?? '', guildId: configuration.guildId ?? '', publicChannelId: configuration.publicChannelId ?? '', bridgeGameToDiscord: configuration.bridgeGameToDiscord, bridgeDiscordToGame: configuration.bridgeDiscordToGame, proxyEnabled: configuration.proxy.isEnabled, proxyEndpoint: configuration.proxy.endpoint ?? '', targets: configuration.targets.map(target => ({ targetKey: target.targetKey, deliveryMode: target.deliveryMode, channelId: target.channelId ?? '', isEnabled: target.isEnabled })) })
    selectedTarget.key = configuration.targets[0]?.targetKey ?? ''
  }, { immediate: true })

  function addTarget() {
    form.targets.push({ targetKey: `target-${form.targets.length + 1}`, deliveryMode: form.mode, channelId: '', isEnabled: true })
  }

  function draft(): DiscordConfigurationDraft | null {
    const configuration = controller.configuration.value
    if (configuration === null)
      return null
    return { expectedVersion: configuration.version, isEnabled: form.isEnabled, mode: form.mode, applicationId: form.applicationId.trim() || null, guildId: form.guildId.trim() || null, publicChannelId: form.publicChannelId.trim() || null, bridgeGameToDiscord: form.bridgeGameToDiscord, bridgeDiscordToGame: form.bridgeDiscordToGame, proxy: { isEnabled: form.proxyEnabled, endpoint: form.proxyEnabled ? (form.proxyEndpoint.trim() || null) : null, hasCredentials: configuration.proxy.hasCredentials }, targets: form.targets.map(target => ({ targetKey: target.targetKey.trim(), deliveryMode: target.deliveryMode, channelId: target.channelId.trim() || null, isEnabled: target.isEnabled })) }
  }

  function save() {
    const value = draft()
    if (value)
      void controller.save(value)
  }

  function applyBotToken() {
    let operation: SecretOperation
    if (botToken.operation === 'Replace') {
      const value = botToken.value.trim()
      if (value === '')
        return
      operation = { operation: 'Replace', value }
    }
    else if (botToken.operation === 'Clear') {
      if (!botToken.clearConfirmed)
        return
      operation = { operation: 'Clear' }
    }
    else {
      operation = { operation: 'Keep' }
    }
    botToken.value = ''
    botToken.clearConfirmed = false
    void controller.updateSecret('botToken', operation)
  }

  function replaceInteractionPublicKey() {
    const value = interactionPublicKey.value.trim()
    if (value === '')
      return
    interactionPublicKey.value = ''
    void controller.updateSecret('interactionPublicKey', { operation: 'Replace', value })
  }

  function removeBinding(discordSubject: string) {
    // Browser confirmation is intentional for this destructive, one-step action.
    // eslint-disable-next-line no-alert
    if (window.confirm(t('discord.binding.confirmRemove', { discordSubject })))
      void controller.removeBinding(discordSubject)
  }

  return {
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
  }
}
