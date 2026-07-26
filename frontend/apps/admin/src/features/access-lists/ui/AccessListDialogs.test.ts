import type { Component } from 'vue'

import { mount } from '@vue/test-utils'
import { expect, it } from 'vitest'

import BanDialog from './BanDialog.vue'
import WhitelistDialog from './WhitelistDialog.vue'

const modalStub = {
  props: ['open'],
  template: '<section v-if="open"><slot name="body" /></section>',
}

const controlStub = {
  inheritAttrs: false,
  template: '<input v-bind="$attrs">',
}

const textareaStub = {
  inheritAttrs: false,
  template: '<textarea v-bind="$attrs" />',
}

function mountDialog(component: Component) {
  return mount(component, {
    props: {
      'open': true,
      'entry': null,
      'onUpdate:open': () => {},
    },
    global: {
      stubs: {
        Button: true,
        FormField: { template: '<div><slot /></div>' },
        Input: controlStub,
        Modal: modalStub,
        Textarea: textareaStub,
        UButton: true,
        UFormField: { template: '<div><slot /></div>' },
        UInput: controlStub,
        UModal: modalStub,
        UTextarea: textareaStub,
      },
    },
  })
}

it('fills the ban form width with every text control', () => {
  const wrapper = mountDialog(BanDialog)
  const inputs = wrapper.findAll('input')

  expect(inputs).toHaveLength(3)
  for (const input of inputs)
    expect(input.classes()).toContain('w-full')
  expect(wrapper.get('textarea').classes()).toContain('w-full')
})

it('fills the whitelist form width with every text control', () => {
  const wrapper = mountDialog(WhitelistDialog)
  const inputs = wrapper.findAll('input')

  expect(inputs).toHaveLength(2)
  for (const input of inputs)
    expect(input.classes()).toContain('w-full')
})
