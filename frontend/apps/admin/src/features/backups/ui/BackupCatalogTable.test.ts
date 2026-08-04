import type { BackupRecord } from '../model/useBackups'

import { mount } from '@vue/test-utils'
import { expect, it } from 'vitest'

import BackupCatalogTable from './BackupCatalogTable.vue'

const verifiedBackup: BackupRecord = {
  id: 'backup-verified',
  kind: 'World',
  sizeBytes: 1_572_864,
  sha256: 'a'.repeat(64),
  worldId: 'Navezgane',
  gameVersion: '3.0.1-b4',
  validationStatus: 'Verified',
  createdAtUtc: '2026-08-03T01:02:03Z',
  sourceJobId: 'job-verified',
  manifestVersion: 1,
}

const unverifiedBackup: BackupRecord = {
  ...verifiedBackup,
  id: 'backup-unverified',
  validationStatus: 'ValidationFailed',
}

function render() {
  return mount(BackupCatalogTable, {
    props: {
      backups: [verifiedBackup, unverifiedBackup],
      disabled: false,
    },
    global: {
      stubs: {
        UBadge: true,
        Badge: true,
        UButton: {
          props: ['disabled', 'label'],
          emits: ['click'],
          template: '<button :disabled="disabled" @click="$emit(\'click\')">{{ label }}</button>',
        },
        Button: {
          props: ['disabled', 'label'],
          emits: ['click'],
          template: '<button :disabled="disabled" @click="$emit(\'click\')">{{ label }}</button>',
        },
        UCard: { template: '<section><slot name="header" /><slot /></section>' },
        Card: { template: '<section><slot name="header" /><slot /></section>' },
        UTable: {
          props: ['data'],
          template: '<div><template v-for="row in data" :key="row.id"><slot name="actions-cell" :row="{ original: row }" /></template></div>',
        },
        Table: {
          props: ['data'],
          template: '<div><template v-for="row in data" :key="row.id"><slot name="actions-cell" :row="{ original: row }" /></template></div>',
        },
      },
    },
  })
}

it('only exposes restore for backups whose server validation is verified', async () => {
  const wrapper = render()
  const verifiedRestore = wrapper.get('[data-testid="restore-backup-backup-verified"]')
  const unverifiedRestore = wrapper.get('[data-testid="restore-backup-backup-unverified"]')

  expect(verifiedRestore.attributes('disabled')).toBeUndefined()
  expect(unverifiedRestore.attributes('disabled')).toBeDefined()

  await verifiedRestore.trigger('click')
  await unverifiedRestore.trigger('click')

  expect(wrapper.emitted('restore')).toEqual([[verifiedBackup]])
})
