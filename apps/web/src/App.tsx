import { lazy, Suspense, useEffect } from 'react'
import { Link, NavLink, Route, Routes, useLocation } from 'react-router'
import { Home } from './pages/Home'
import { Leaderboards } from './pages/Leaderboards'
import { Search, SearchBox } from './pages/Search'
import { PageErrorBoundary } from './errors'
import { Message, Skeleton } from './ui'

// Profile and match pages carry the charts; they load when first opened.
const Player = lazy(() => import('./pages/Player').then((m) => ({ default: m.Player })))
const MatchPage = lazy(() => import('./pages/Match').then((m) => ({ default: m.MatchPage })))

/** The mark: two report brackets around a dot, drawn, not a font glyph. */
function Mark() {
  return (
    <svg viewBox="0 0 24 24" className="size-7" aria-hidden="true">
      <path d="M2 8V2h6M22 16v6h-6" fill="none" stroke="var(--color-win)" strokeWidth="2.5" />
      <path d="M16 2h6v6M8 22H2v-6" fill="none" stroke="var(--color-loss)" strokeWidth="2.5" />
      <rect x="9" y="9" width="6" height="6" fill="var(--color-you)" />
    </svg>
  )
}

export function App() {
  const location = useLocation()
  // A new page starts at the top, as a link to another match should. A block, not an expression: newer browsers
  // return a promise from scrollTo, which React would take for a cleanup function and crash on the next page.
  useEffect(() => {
    window.scrollTo({ top: 0 })
  }, [location.pathname])

  const nav = ({ isActive }: { isActive: boolean }) =>
    `display relative px-1 py-1 text-lg font-bold tracking-[0.1em] transition-colors duration-200 ${isActive ? 'text-text' : 'text-muted hover:text-text'}`
  return (
    <div className="flex min-h-screen flex-col">
      <header className="sticky top-0 z-20 border-b border-line/70 bg-bg/85 backdrop-blur-md">
        <div className="mx-auto flex max-w-6xl items-center gap-4 px-4 py-3 sm:gap-8 sm:px-6">
          <Link to="/" className="flex shrink-0 items-center gap-2.5" aria-label="COD Stats, Startseite">
            <Mark />
            <span className="display text-2xl font-extrabold tracking-[0.06em]">
              COD <span className="text-you">Stats</span>
            </span>
          </Link>
          <nav className="flex gap-5" aria-label="Hauptnavigation">
            <NavLink to="/leaderboards" className={nav}>
              Rankings
            </NavLink>
            <NavLink to="/search" className={nav}>
              Suche
            </NavLink>
          </nav>
          <div className="ml-auto hidden w-72 md:block">
            <SearchBox compact />
          </div>
        </div>
      </header>
      <main className="mx-auto w-full max-w-6xl flex-1 px-4 pb-16 sm:px-6">
        <PageErrorBoundary key={location.pathname}>
          <Suspense fallback={<Skeleton className="mt-10 h-96" />}>
            <Routes>
              <Route index element={<Home />} />
              <Route path="search" element={<Search />} />
              <Route path="player/:id" element={<Player />} />
              <Route path="player/:id/match/:matchId" element={<MatchPage />} />
              <Route path="leaderboards" element={<Leaderboards />} />
              <Route path="*" element={<Message>Diese Seite gibt es nicht.</Message>} />
            </Routes>
          </Suspense>
        </PageErrorBoundary>
      </main>
      <footer className="border-t border-line/70 bg-deep/60">
        <div className="mx-auto flex max-w-6xl flex-col gap-1 px-4 py-6 text-sm text-faint sm:flex-row sm:justify-between sm:px-6">
          <span>COD Stats · community-erfasst, selbst gehostet</span>
          <span>Nicht mit Activision verbunden. Call of Duty ist eine Marke von Activision Publishing, Inc.</span>
        </div>
      </footer>
    </div>
  )
}
