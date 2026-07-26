import { PiniaColada } from '@pinia/colada'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useSendChat } from './useSendChat'

function mountSendChat(send = vi.fn().mockResolvedValue(undefined)) {
  const auth = { authorizationHeader: 'Bearer owner' as string | null, expireSession: vi.fn() }
  let chat!: ReturnType<typeof useSendChat>
  const Host = defineComponent({
    setup() {
      chat = useSendChat({ auth, send })
      return () => null
    },
  })
  const wrapper = mount(Host, { global: { plugins: [createPinia(), PiniaColada] } })
  return { auth, chat: () => chat, send, wrapper }
}

describe('useSendChat', () => {
  it('sends global and private drafts, clears success inputs and keeps at most 50 history items', async () => {
    const mounted = mountSendChat()
    for (let index = 1; index <= 51; index++) {
      mounted.chat().setDraft(` message ${index} `)
      if (index === 51)
        mounted.chat().setTarget('EOS_target')
      await mounted.chat().submit()
    }

    expect(mounted.send).toHaveBeenLastCalledWith('Bearer owner', {
      message: 'message 51',
      targetCrossplatformId: 'EOS_target',
    }, expect.any(AbortSignal))
    expect(mounted.chat().draft.value).toBe('')
    expect(mounted.chat().targetCrossplatformId.value).toBeNull()
    expect(mounted.chat().sendHistory.value).toHaveLength(50)
    expect(mounted.chat().sendHistory.value[0]).toBe('message 2')
    mounted.wrapper.unmount()
  })

  it('keeps draft and target after failure and exposes a safe error code without retrying', async () => {
    const send = vi.fn().mockRejectedValue(new HttpError('http', 'secret', {
      problemCode: 'chat_target_offline',
      status: 409,
    }))
    const mounted = mountSendChat(send)
    mounted.chat().setDraft('hello')
    mounted.chat().setTarget('EOS_target')

    await mounted.chat().submit()

    expect(send).toHaveBeenCalledOnce()
    expect(mounted.chat().draft.value).toBe('hello')
    expect(mounted.chat().targetCrossplatformId.value).toBe('EOS_target')
    expect(mounted.chat().error.value).toEqual({ code: 'target_offline' })
    mounted.wrapper.unmount()
  })

  it('expires an absent or rejected session and aborts an in-flight send on unmount', async () => {
    const unauthenticated = mountSendChat()
    unauthenticated.auth.authorizationHeader = null
    unauthenticated.chat().setDraft('hello')
    await unauthenticated.chat().submit()
    expect(unauthenticated.auth.expireSession).toHaveBeenCalledOnce()
    expect(unauthenticated.send).not.toHaveBeenCalled()
    expect(unauthenticated.chat().error.value).toEqual({ code: 'session_expired' })
    unauthenticated.wrapper.unmount()

    let reject!: (reason: unknown) => void
    let signal: AbortSignal | undefined
    const pending = new Promise<void>((_resolve, rejectPromise) => {
      reject = rejectPromise
    })
    const mounted = mountSendChat(vi.fn((_authorization, _input, nextSignal) => {
      signal = nextSignal
      return pending
    }))
    mounted.chat().setDraft('still here')
    void mounted.chat().submit()
    mounted.wrapper.unmount()
    reject(new HttpError('aborted', 'cancelled'))
    await flushPromises()
    expect(signal?.aborted).toBe(true)
  })

  it('expires a 401 session once and keeps the rejected draft', async () => {
    const mounted = mountSendChat(vi.fn().mockRejectedValue(
      new HttpError('http', 'secret', { status: 401 }),
    ))
    mounted.chat().setDraft('retry me')

    await mounted.chat().submit()

    expect(mounted.auth.expireSession).toHaveBeenCalledOnce()
    expect(mounted.chat().draft.value).toBe('retry me')
    expect(mounted.chat().error.value).toEqual({ code: 'session_expired' })
    mounted.wrapper.unmount()
  })
})
