import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from './App'

describe('navigation', () => {
  beforeEach(() => {
    // Newer Chrome returns a promise from scrollTo; React must not take it for an effect cleanup (issue #40).
    vi.spyOn(window, 'scrollTo').mockImplementation((() => Promise.resolve()) as unknown as typeof window.scrollTo)
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) => {
        if (url.includes('platform-stats')) return Response.json({ players: 1, matches: 5, kills: 131, lastMatchAt: null })
        if (url.includes('leaderboards')) return Response.json({ game: 'mw4', metric: 'kd', minMatches: 20, entries: [] })
        return new Response(null, { status: 404 })
      }),
    )
  })

  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('keeps the page when moving between pages', async () => {
    render(
      <MemoryRouter initialEntries={['/']}>
        <App />
      </MemoryRouter>,
    )
    expect(await screen.findByText('Der Client')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('link', { name: 'Rankings' }))
    expect(await screen.findByRole('heading', { name: 'Rankings' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('link', { name: 'Suche' }))
    expect(await screen.findByRole('heading', { name: 'Spieler finden' })).toBeInTheDocument()
  })
})
