import type { AdminLocaleRuntime } from '../../../app/i18n'
import type { ChatMutesController } from '../model/useChatMutes'

import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { readonly, shallowRef } from 'vue'

import { createAdminI18n } from '../../../app/i18n'
import ChatMutesView from './ChatMutesView.vue'

function localeRuntime(locale: 'en' | 'zh-CN'): AdminLocaleRuntime {
  return createAdminI18n({
    repository: {
      restore: () => locale,
      save: () => true,
      subscribe: () => () => {},
    },
    documentElement: { lang: '' },
  })
}

function makeController(options: {
  mutating?: boolean
  release?: ChatMutesController['release']
} = {}): ChatMutesController {
  return {
    state: readonly(shallowRef('ready')),
    mutes: readonly(shallowRef(Object.freeze([Object.freeze({
      crossplatformId: 'EOS_technical_identity',
      displayName: null,
      reason: 'spam',
      mutedUntilUtc: null,
      createdBy: 'owner',
      createdAtUtc: '2026-07-26T08:00:00Z',
      updatedBy: 'owner',
      updatedAtUtc: '2026-07-26T08:00:00Z',
    })]))),
    nextCursor: readonly(shallowRef(null)),
    pageNumber: readonly(shallowRef(1)),
    isMutating: readonly(shallowRef(options.mutating ?? false)),
    create: vi.fn().mockResolvedValue(true),
    update: vi.fn().mockResolvedValue(true),
    release: options.release ?? vi.fn().mockResolvedValue(true),
    goToPage: vi.fn(),
    refresh: vi.fn(),
    retry: vi.fn(),
    dispose: vi.fn(),
  }
}

const buttonStub = {
  inheritAttrs: false,
  props: ['disabled', 'label', 'loading', 'type'],
  emits: ['click'],
  template: '<button v-bind="$attrs" :disabled="disabled" :type="type || \'button\'" @click="$emit(\'click\', $event)">{{ label }}<slot /></button>',
}
const modalStub = {
  props: ['open', 'title', 'description'],
  emits: ['update:open'],
  template: '<section v-if="open" role="dialog" :aria-label="title"><h2>{{ title }}</h2><p>{{ description }}</p><slot name="body" /><slot name="footer" /></section>',
}
const formStub = {
  props: ['state'],
  emits: ['submit'],
  template: '<form @submit.prevent="$emit(\'submit\')"><slot /></form>',
}
const formFieldStub = {
  props: ['label', 'hint'],
  template: '<label><span>{{ label }}</span><slot /><small>{{ hint }}</small></label>',
}
const inputStub = {
  inheritAttrs: false,
  props: ['modelValue', 'disabled', 'placeholder'],
  emits: ['update:modelValue'],
  template: '<input v-bind="$attrs" :disabled="disabled" :placeholder="placeholder" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)">',
}
const textareaStub = {
  inheritAttrs: false,
  props: ['modelValue'],
  emits: ['update:modelValue'],
  template: '<textarea v-bind="$attrs" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
}
const checkboxStub = {
  props: ['modelValue', 'label'],
  emits: ['update:modelValue'],
  template: '<label><input type="checkbox" :checked="modelValue" @change="$emit(\'update:modelValue\', $event.target.checked)">{{ label }}</label>',
}

function mountView(controller = makeController(), locale: 'en' | 'zh-CN' = 'en') {
  const runtime = localeRuntime(locale)
  const wrapper = mount(ChatMutesView, {
    props: { controller },
    global: {
      plugins: [runtime.i18n],
      stubs: {
        Alert: { props: ['title', 'description'], template: '<div role="alert">{{ title }}{{ description }}<slot name="actions" /></div>' },
        UAlert: { props: ['title', 'description'], template: '<div role="alert">{{ title }}{{ description }}<slot name="actions" /></div>' },
        Badge: { props: ['label'], template: '<span>{{ label }}<slot /></span>' },
        UBadge: { props: ['label'], template: '<span>{{ label }}<slot /></span>' },
        Button: buttonStub,
        UButton: buttonStub,
        Checkbox: checkboxStub,
        UCheckbox: checkboxStub,
        DashboardNavbar: { props: ['title'], template: '<header><h1>{{ title }}</h1><slot name="leading" /><slot name="right" /></header>' },
        UDashboardNavbar: { props: ['title'], template: '<header><h1>{{ title }}</h1><slot name="leading" /><slot name="right" /></header>' },
        DashboardPanel: { template: '<main><slot name="header" /><slot name="body" /></main>' },
        UDashboardPanel: { template: '<main><slot name="header" /><slot name="body" /></main>' },
        DashboardSidebarCollapse: true,
        UDashboardSidebarCollapse: true,
        Form: formStub,
        UForm: formStub,
        FormField: formFieldStub,
        UFormField: formFieldStub,
        Input: inputStub,
        UInput: inputStub,
        Modal: modalStub,
        UModal: modalStub,
        Pagination: true,
        UPagination: true,
        Skeleton: true,
        USkeleton: true,
        Table: { props: ['columns', 'data'], template: '<table><thead><tr><th v-for="column in columns" :key="column.id || column.accessorKey">{{ column.header }}</th></tr></thead><tbody><tr v-for="record in data" :key="record.crossplatformId"><td><slot name="actions-cell" :row="{ original: record }" /></td></tr></tbody></table>' },
        UTable: { props: ['columns', 'data'], template: '<table><thead><tr><th v-for="column in columns" :key="column.id || column.accessorKey">{{ column.header }}</th></tr></thead><tbody><tr v-for="record in data" :key="record.crossplatformId"><td><slot name="actions-cell" :row="{ original: record }" /></td></tr></tbody></table>' },
        Textarea: textareaStub,
        UTextarea: textareaStub,
      },
    },
  })
  return { runtime, wrapper }
}

describe('chatMutesView', () => {
  it('localizes visible mute management text without translating technical identities', () => {
    const english = mountView()
    const chinese = mountView(makeController(), 'zh-CN')

    expect(english.wrapper.text()).toContain('Chat mutes')
    expect(english.wrapper.text()).toContain('Permanent')
    expect(english.wrapper.text()).toContain('EOS_technical_identity')
    expect(chinese.wrapper.text()).toContain('禁言管理')
    expect(chinese.wrapper.text()).toContain('永久')
    expect(chinese.wrapper.text()).toContain('EOS_technical_identity')

    english.runtime.dispose()
    chinese.runtime.dispose()
  })

  it('renders distinct desktop and mobile contracts for the same mute', () => {
    const { runtime, wrapper } = mountView()

    expect(wrapper.get('[data-testid="mute-desktop-table"]').classes()).toContain('md:block')
    expect(wrapper.get('[data-testid="mute-mobile-list"]').classes()).toContain('md:hidden')
    expect(wrapper.get('[data-testid="mute-mobile-list"]').attributes('aria-label')).toBe('Active chat mutes on small screens')
    expect(wrapper.get('[data-testid="edit-mute-mobile-EOS_technical_identity"]').attributes('aria-label')).toBe('Edit mute for EOS_technical_identity')
    expect(wrapper.get('[data-testid="edit-mute-desktop-EOS_technical_identity"]').attributes('aria-label')).toBe('Edit mute for EOS_technical_identity')

    runtime.dispose()
  })

  it('requires confirmation before releasing a mute', async () => {
    const release = vi.fn().mockResolvedValue(true)
    const { runtime, wrapper } = mountView(makeController({ release }))

    await wrapper.get('[data-testid="release-mute-mobile-EOS_technical_identity"]').trigger('click')

    expect(release).not.toHaveBeenCalled()
    expect(wrapper.get('[role="dialog"]').text()).toContain('Release the chat mute for EOS_technical_identity?')
    expect(wrapper.get('[data-testid="confirm-release-mute"]').attributes('aria-label')).toBe('Confirm releasing the chat mute for EOS_technical_identity')

    await wrapper.get('[data-testid="confirm-release-mute"]').trigger('click')
    await flushPromises()

    expect(release).toHaveBeenCalledOnce()
    expect(release).toHaveBeenCalledWith('EOS_technical_identity', null)
    expect(wrapper.findAll('[role="dialog"]')).toHaveLength(0)
    runtime.dispose()
  })

  it('announces localized validation errors and preserves the form', async () => {
    const controller = makeController()
    const { runtime, wrapper } = mountView(controller)

    await wrapper.get('[data-testid="create-mute-button"]').trigger('click')
    await wrapper.get('#chat-mute-form').trigger('submit')

    expect(wrapper.get('[role="alert"]').text()).toBe('Cross-platform ID and reason are required')
    expect(controller.create).not.toHaveBeenCalled()
    expect(wrapper.get('[role="dialog"]').attributes('aria-label')).toBe('Add chat mute')
    runtime.dispose()
  })

  it('disables mutation entry points while a mutation is in flight', () => {
    const { runtime, wrapper } = mountView(makeController({ mutating: true }))

    expect(wrapper.get('[data-testid="create-mute-button"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-testid="edit-mute-mobile-EOS_technical_identity"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-testid="release-mute-mobile-EOS_technical_identity"]').attributes('disabled')).toBeDefined()
    runtime.dispose()
  })
})
