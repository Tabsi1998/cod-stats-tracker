import { afterEach, describe, expect, it, vi } from 'vitest'

import { scrollToTop } from './scroll'

// Newer Chrome resolves window.scrollTo to a Promise. An effect that returns it
// hands React a non-function "cleanup" and the tree crashes with
// `TypeError: l is not a function`, leaving a blank page on every in-app
// navigation (#40).
describe('scrollToTop', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('returns nothing even when window.scrollTo resolves to a Promise', () => {
    vi.stubGlobal(
      'scrollTo',
      vi.fn(() => Promise.resolve()),
    )

    const returned = scrollToTop()

    expect(returned).toBeUndefined()
    expect(window.scrollTo).toHaveBeenCalledWith({ top: 0 })
  })
})
