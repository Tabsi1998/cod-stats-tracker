import { Link, NavLink, Route, Routes } from 'react-router'
import { Home } from './pages/Home'
import { Leaderboards } from './pages/Leaderboards'
import { Player } from './pages/Player'
import { Search } from './pages/Search'
import { Message } from './ui'

export function App() {
  const nav = ({ isActive }: { isActive: boolean }) => (isActive ? 'text-text' : 'text-muted hover:text-text')
  return (
    <div className="mx-auto flex min-h-screen max-w-5xl flex-col px-4 sm:px-6">
      <header className="flex items-center justify-between py-5">
        <Link to="/" className="text-lg font-bold tracking-tight">
          COD <span className="text-accent">Stats</span>
        </Link>
        <nav className="flex gap-5 text-sm">
          <NavLink to="/leaderboards" className={nav}>
            Rankings
          </NavLink>
          <NavLink to="/search" className={nav}>
            Suche
          </NavLink>
        </nav>
      </header>
      <main className="flex-1 pb-12">
        <Routes>
          <Route index element={<Home />} />
          <Route path="search" element={<Search />} />
          <Route path="player/:id" element={<Player />} />
          <Route path="leaderboards" element={<Leaderboards />} />
          <Route path="*" element={<Message>Diese Seite gibt es nicht.</Message>} />
        </Routes>
      </main>
      <footer className="border-t border-line py-6 text-xs text-muted">
        Nicht mit Activision verbunden. Call of Duty ist eine Marke von Activision Publishing, Inc.
      </footer>
    </div>
  )
}
