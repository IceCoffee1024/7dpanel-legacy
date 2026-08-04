import type { BackupRecord } from '../model/useBackups'

import { mount } from '@vue/test-utils'
import { expect, it } from 'vitest'

import RestoreConfirmModal from './RestoreConfirmModal.vue'

const backup: BackupRecord = {
  id: 'backup-42',
  kind: 'World',
  sizeBytes: 1_572_864,
  sha256: 'a'.repeat(64),
  worldId: 'Navezgane',
  gameVersion: '3.0.1-b4',
  validationStatus: 'Verified',
  createdAtUtc: '2026-08-03T01:02:03Z',
  sourceJobId: 'job-42',
  manifestVersion: 1,
}

it('repeats the fixed backup identity and validation evidence before staging restore', () => {
  const wrapper = mount(RestoreConfirmModal, {
    props: { backup, disabled: false, open: true },
    global: {
      stubs: {
        UAlert: { props: ['title', 'description'], template: '<div>{{ title }} {{ description }}</div>' },
        Alert: { props: ['title', 'description'], template: '<div>{{ title }} {{ description }}</div>' },
        UButton: { props: ['disabled', 'label'], template: '<button :disabled="disabled">{{ label }}</button>' },
        Button: { props: ['disabled', 'label'], template: '<button :disabled="disabled">{{ label }}</button>' },
        UCheckbox: true,
        Checkbox: true,
        UModal: { template: '<section><slot name="body" /><slot name="footer" :close="() => {}" /></section>' },
        Modal: { template: '<section><slot name="body" /><slot name="footer" :close="() => {}" /></section>' },
      },
    },
  })

  expect(wrapper.text()).toContain('backup-42')
  expect(wrapper.text()).toContain('Verified')
  expect(wrapper.text()).toContain('1.5 MiB')
  expect(wrapper.text()).toContain('Navezgane')
})

it('does not permit confirmation when a stale catalog row is not verified', () => {
  const wrapper = mount(RestoreConfirmModal, {
    props: {
      backup: { ...backup, validationStatus: 'ValidationFailed' },
      disabled: false,
      open: true,
    },
    global: {
      stubs: {
        UAlert: { props: ['title', 'description'], template: '<div>{{ title }} {{ description }}</div>' },
        Alert: { props: ['title', 'description'], template: '<div>{{ title }} {{ description }}</div>' },
        UButton: { props: ['disabled', 'label'], template: '<button :disabled="disabled">{{ label }}</button>' },
        Button: { props: ['disabled', 'label'], template: '<button :disabled="disabled">{{ label }}</button>' },
        UCheckbox: true,
        Checkbox: true,
        UModal: { template: '<section><slot name="body" /><slot name="footer" :close="() => {}" /></section>' },
        Modal: { template: '<section><slot name="body" /><slot name="footer" :close="() => {}" /></section>' },
      },
    },
  })

  expect(wrapper.get('button:last-child').attributes('disabled')).toBeDefined()
})
