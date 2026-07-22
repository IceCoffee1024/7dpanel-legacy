import type { KickPlayerFeedback, OnlinePlayer } from '..'

import { mount } from '@vue/test-utils'
import { expect, it } from 'vitest'

import KickPlayerDialog from './KickPlayerDialog.vue'

const player: OnlinePlayer = {
  entityId: 7,
  name: 'Test Player',
  platformIdentity: {
    combinedId: 'Steam_76561198000000000',
    platform: 'Steam',
  },
  crossplatformIdentity: null,
  ping: 42,
  level: 18,
  health: 93,
}

const modalStub = {
  props: ['open', 'title', 'description'],
  emits: ['update:open'],
  template: `
    <section v-if="open" role="dialog">
      <h2>{{ title }}</h2>
      <p>{{ description }}</p>
      <slot name="body" />
      <slot name="footer" />
      <button data-testid="modal-dismiss" @click="$emit('update:open', false)">关闭</button>
    </section>
  `,
}

function mountDialog(options: {
  player?: OnlinePlayer | null
  isSubmitting?: boolean
  feedback?: KickPlayerFeedback | null
} = {}) {
  return mount(KickPlayerDialog, {
    props: {
      'open': true,
      'player': options.player === undefined ? player : options.player,
      'isSubmitting': options.isSubmitting ?? false,
      'feedback': options.feedback ?? null,
      'onUpdate:open': () => {},
    },
    global: {
      stubs: {
        Icon: true,
        Modal: modalStub,
        UIcon: true,
        UModal: modalStub,
      },
    },
  })
}

it('shows only the fixed approved player identity', () => {
  const unsafePlayer = {
    ...player,
    ip: '192.0.2.1',
    token: 'Bearer secret',
    rawError: 'native stack trace',
  } as unknown as OnlinePlayer
  const wrapper = mountDialog({ player: unsafePlayer })

  expect(wrapper.text()).toContain('Test Player')
  expect(wrapper.text()).toContain('Steam')
  expect(wrapper.text()).toContain('Steam_76561198000000000')
  expect(wrapper.text()).not.toMatch(/192\.0\.2\.1|Bearer secret|native stack trace/)
})

it('trims and emits a valid reason without clearing it on an unrelated parent rerender', async () => {
  const wrapper = mountDialog()
  const textarea = wrapper.get('textarea')
  await textarea.setValue('  违反服务器规则  ')
  await wrapper.setProps({ feedback: { code: 'player_action_busy', message: '请稍后重试' } })

  expect(wrapper.get('textarea').element.value).toBe('  违反服务器规则  ')
  await wrapper.get('[data-testid="confirm-kick-player"]').trigger('click')

  expect(wrapper.emitted('confirm')).toEqual([['违反服务器规则']])
})

it.each([
  ['empty after trimming', '   '],
  ['over 200 characters', '原'.repeat(201)],
] as const)('disables confirmation for %s', async (_, reason) => {
  const wrapper = mountDialog()

  await wrapper.get('textarea').setValue(reason)

  expect(wrapper.get('[data-testid="confirm-kick-player"]').attributes()).toHaveProperty('disabled')
  expect(wrapper.emitted('confirm')).toBeUndefined()
})

it.each(['a', '原'.repeat(200)])('accepts a reason at the supported boundary', async (reason) => {
  const wrapper = mountDialog()

  await wrapper.get('textarea').setValue(reason)
  await wrapper.get('[data-testid="confirm-kick-player"]').trigger('click')

  expect(wrapper.emitted('confirm')).toEqual([[reason]])
})

it('locks textarea, cancellation, closing and confirmation while submitting', async () => {
  const wrapper = mountDialog({ isSubmitting: true })

  expect(wrapper.get('textarea').attributes()).toHaveProperty('disabled')
  expect(wrapper.get('[data-testid="cancel-kick-player"]').attributes()).toHaveProperty('disabled')
  expect(wrapper.get('[data-testid="confirm-kick-player"]').attributes()).toHaveProperty('disabled')
  await wrapper.get('[data-testid="cancel-kick-player"]').trigger('click')
  await wrapper.get('[data-testid="modal-dismiss"]').trigger('click')

  expect(wrapper.emitted('cancel')).toBeUndefined()
  expect(wrapper.emitted('update:open')).toBeUndefined()
})

it('renders only stable feedback as status', () => {
  const wrapper = mountDialog({
    feedback: {
      code: 'unknown',
      message: '结果尚无法确认',
    },
  })

  const status = wrapper.get('[role="status"]')
  expect(status.text()).toBe('结果尚无法确认')
  expect(status.text()).not.toMatch(/token|exception|stack/i)
})
