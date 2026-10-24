import { Component, type ReactNode } from 'react'

/**
 * A page that fails shows a message with a way out instead of an empty screen; the header and other pages keep
 * working. Keyed by path in the app, so going to another page starts fresh.
 */
export class PageErrorBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false }

  static getDerivedStateFromError() {
    return { failed: true }
  }

  render() {
    if (!this.state.failed) return this.props.children
    return (
      <div className="space-y-4 py-16 text-center">
        <p className="display text-3xl font-bold">Diese Seite konnte nicht angezeigt werden.</p>
        <p className="text-muted">Meist hilft neu laden, etwa nach einem Update der Seite.</p>
        <button
          type="button"
          onClick={() => window.location.reload()}
          className="display bg-you px-6 py-2.5 text-lg font-bold tracking-[0.1em] text-bg transition-[filter] duration-200 hover:brightness-110"
        >
          Neu laden
        </button>
      </div>
    )
  }
}
