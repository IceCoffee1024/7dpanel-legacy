<script setup lang="ts">
import type { CommandPermission, GameAdmin, PanelRole, PanelUser } from '../api/permissions'

import { onMounted, reactive, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '../../auth'
import {
  createPanelUser,
  deletePanelUser,
  fetchCommandPermissions,
  fetchGameAdmins,
  fetchPanelUsers,
  removeCommandPermission,
  removeGameAdmin,
  resetPanelUserPassword,
  updatePanelUser,
  upsertCommandPermission,
  upsertGameAdmin,
} from '../api/permissions'

const auth = useAuthStore()
const { t } = useI18n()
const roles: PanelRole[] = ['Owner', 'Admin', 'Viewer']
const users = shallowRef<readonly PanelUser[]>([])
const admins = shallowRef<readonly GameAdmin[]>([])
const commands = shallowRef<readonly CommandPermission[]>([])
const loading = shallowRef(true)
const feedback = shallowRef('')
const pendingDelete = shallowRef('')
const newUser = reactive({ username: '', password: '', role: 'Viewer' as PanelRole, enabled: true })
const newAdmin = reactive({ playerId: '', displayName: '', permissionLevel: 1000 })
const newCommand = reactive({ command: '', permissionLevel: 1000 })

function authorization() {
  if (!auth.authorizationHeader)
    throw new Error('Authentication required')
  return auth.authorizationHeader
}

async function refresh() {
  loading.value = true
  feedback.value = ''
  const header = authorization()
  const results = await Promise.allSettled([
    fetchPanelUsers(header),
    fetchGameAdmins(header),
    fetchCommandPermissions(header),
  ])
  if (results[0].status === 'fulfilled')
    users.value = results[0].value
  if (results[1].status === 'fulfilled')
    admins.value = results[1].value
  if (results[2].status === 'fulfilled')
    commands.value = results[2].value
  if (results.some(result => result.status === 'rejected'))
    feedback.value = t('permissions.feedback.partial')
  loading.value = false
}

async function addUser() {
  await createPanelUser(authorization(), { ...newUser })
  Object.assign(newUser, { username: '', password: '', role: 'Viewer', enabled: true })
  await refresh()
}

async function saveUser(user: PanelUser) {
  await updatePanelUser(authorization(), user)
  await refresh()
}

async function resetPassword(user: PanelUser, password: string) {
  if (password.length < 8) {
    feedback.value = t('permissions.feedback.passwordTooShort')
    return
  }
  await resetPanelUserPassword(authorization(), user.subject, password)
  feedback.value = t('permissions.feedback.passwordReset', { username: user.username })
}

async function removeUser(user: PanelUser) {
  const key = `user:${user.subject}`
  if (pendingDelete.value !== key) {
    pendingDelete.value = key
    return
  }
  await deletePanelUser(authorization(), user.subject)
  pendingDelete.value = ''
  await refresh()
}

async function saveAdmin(admin: GameAdmin) {
  await upsertGameAdmin(authorization(), admin)
  Object.assign(newAdmin, { playerId: '', displayName: '', permissionLevel: 1000 })
  await refresh()
}

async function deleteAdmin(playerId: string) {
  await removeGameAdmin(authorization(), playerId)
  await refresh()
}

async function saveCommand(item: CommandPermission) {
  await upsertCommandPermission(authorization(), item)
  Object.assign(newCommand, { command: '', permissionLevel: 1000 })
  await refresh()
}

async function deleteCommand(command: string) {
  await removeCommandPermission(authorization(), command)
  await refresh()
}

onMounted(() => void refresh())
</script>

<template>
  <UDashboardPanel id="permissions">
    <template #header>
      <UDashboardNavbar :title="t('permissions.title')">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <UButton
            color="neutral"
            icon="i-lucide-refresh-cw"
            :label="t('permissions.refresh')"
            :loading="loading"
            @click="refresh"
          />
        </template>
      </UDashboardNavbar>
    </template>
    <template #body>
      <div class="space-y-8">
        <UAlert
          color="neutral"
          icon="i-lucide-shield-alert"
          variant="subtle"
          :title="t('permissions.separation.title')"
          :description="t('permissions.separation.description')"
        />
        <p v-if="feedback" role="status" class="text-sm text-warning-700 dark:text-warning-400">
          {{ feedback }}
        </p>

        <section class="space-y-3">
          <h2 class="text-lg font-semibold">
            {{ t('permissions.panelUsers.title') }}
          </h2>
          <div class="grid gap-2 md:grid-cols-5">
            <UInput
              v-model="newUser.username"
              name="new-panel-username"
              :aria-label="t('permissions.panelUsers.username')"
              :placeholder="t('permissions.panelUsers.username')"
            />
            <UInput
              v-model="newUser.password"
              name="new-panel-password"
              type="password"
              :aria-label="t('permissions.panelUsers.initialPassword')"
              :placeholder="t('permissions.panelUsers.initialPassword')"
            />
            <USelect
              v-model="newUser.role"
              name="new-panel-role"
              :aria-label="t('permissions.panelUsers.role')"
              :items="roles"
            />
            <UCheckbox v-model="newUser.enabled" name="new-panel-enabled" :label="t('permissions.panelUsers.enabled')" />
            <UButton color="neutral" :label="t('permissions.panelUsers.create')" @click="addUser" />
          </div>
          <article v-for="user in users" :key="user.subject" class="grid gap-2 border-b border-default py-3 md:grid-cols-6 md:items-center">
            <UInput v-model="user.username" :name="`username-${user.subject}`" :aria-label="t('permissions.panelUsers.username')" />
            <USelect
              v-model="user.role"
              :name="`role-${user.subject}`"
              :aria-label="t('permissions.panelUsers.role')"
              :items="roles"
            />
            <UCheckbox v-model="user.enabled" :name="`enabled-${user.subject}`" :label="t('permissions.panelUsers.enabled')" />
            <UButton
              color="neutral"
              :label="t('common.save')"
              variant="outline"
              @click="saveUser(user)"
            />
            <UInput
              :id="`password-${user.subject}`"
              :name="`password-${user.subject}`"
              type="password"
              :aria-label="t('permissions.panelUsers.newPassword')"
              :placeholder="t('permissions.panelUsers.newPassword')"
              @keyup.enter="resetPassword(user, ($event.target as HTMLInputElement).value); ($event.target as HTMLInputElement).value = ''"
            />
            <UButton
              color="error"
              class="text-error-700 dark:text-error-400"
              :label="pendingDelete === `user:${user.subject}` ? t('permissions.panelUsers.confirmDelete') : t('permissions.panelUsers.delete')"
              variant="ghost"
              @click="removeUser(user)"
            />
          </article>
        </section>

        <section class="space-y-3">
          <h2 class="text-lg font-semibold">
            {{ t('permissions.gameAdmins.title') }}
          </h2>
          <div class="grid gap-2 md:grid-cols-4">
            <UInput
              v-model="newAdmin.playerId"
              name="new-game-admin-player-id"
              :aria-label="t('permissions.fields.playerId')"
              :placeholder="t('permissions.fields.playerId')"
            />
            <UInput
              v-model="newAdmin.displayName"
              name="new-game-admin-display-name"
              :aria-label="t('permissions.fields.displayName')"
              :placeholder="t('permissions.fields.displayName')"
            />
            <UInput
              v-model.number="newAdmin.permissionLevel"
              name="new-game-admin-permission-level"
              type="number"
              min="0"
              max="2000"
              :aria-label="t('permissions.fields.permissionLevel')"
            />
            <UButton color="neutral" :label="t('permissions.action.upsert')" @click="saveAdmin(newAdmin)" />
          </div>
          <article v-for="admin in admins" :key="admin.playerId" class="grid gap-2 border-b border-default py-3 md:grid-cols-4 md:items-center">
            <code>{{ admin.playerId }}</code><span>{{ admin.displayName }}</span>
            <UInput
              v-model.number="admin.permissionLevel"
              :name="`admin-permission-${admin.playerId}`"
              type="number"
              min="0"
              max="2000"
              :aria-label="`${t('permissions.fields.permissionLevel')}: ${admin.displayName}`"
            />
            <div class="flex gap-2">
              <UButton
                color="neutral"
                :label="t('common.save')"
                variant="outline"
                @click="saveAdmin(admin)"
              /><UButton
                color="error"
                class="text-error-700 dark:text-error-400"
                :label="t('permissions.action.remove')"
                variant="ghost"
                @click="deleteAdmin(admin.playerId)"
              />
            </div>
          </article>
        </section>

        <section class="space-y-3">
          <h2 class="text-lg font-semibold">
            {{ t('permissions.commands.title') }}
          </h2>
          <div class="grid gap-2 md:grid-cols-3">
            <UInput
              v-model="newCommand.command"
              name="new-command"
              :aria-label="t('permissions.commands.command')"
              :placeholder="t('permissions.commands.command')"
            />
            <UInput
              v-model.number="newCommand.permissionLevel"
              name="new-command-permission-level"
              type="number"
              min="0"
              max="2000"
              :aria-label="t('permissions.fields.permissionLevel')"
            />
            <UButton color="neutral" :label="t('permissions.action.upsert')" @click="saveCommand({ ...newCommand, description: null })" />
          </div>
          <article v-for="item in commands" :key="item.command" class="grid gap-2 border-b border-default py-3 md:grid-cols-4 md:items-center">
            <code>{{ item.command }}</code><span class="text-sm text-muted">{{ item.description ?? t('permissions.commands.noDescription') }}</span>
            <UInput
              v-model.number="item.permissionLevel"
              :name="`command-permission-${item.command}`"
              type="number"
              min="0"
              max="2000"
              :aria-label="`${t('permissions.fields.permissionLevel')}: ${item.command}`"
            />
            <div class="flex gap-2">
              <UButton
                color="neutral"
                :label="t('common.save')"
                variant="outline"
                @click="saveCommand(item)"
              /><UButton
                color="error"
                class="text-error-700 dark:text-error-400"
                :label="t('permissions.commands.restoreDefault')"
                variant="ghost"
                @click="deleteCommand(item.command)"
              />
            </div>
          </article>
        </section>
      </div>
    </template>
  </UDashboardPanel>
</template>
