import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { api } from '../api'
import { formatCount, formatDate, formatKd } from '../format'
import { useLoad } from '../load'
import { Card, Message } from '../ui'

export function SearchBox({ initial = '' }: { initial?: string }) {
  const [text, setText] = useState(initial)
  const navigate = useNavigate()

  function submit(event: FormEvent) {
    event.preventDefault()
    const q = text.trim()
    if (q.length >= 2) navigate(`/search?q=${encodeURIComponent(q)}`)
  }

  return (
    <form onSubmit={submit} role="search" className="flex max-w-xl gap-2">
      <label htmlFor="player-search" className="sr-only">
        Spielername
      </label>
      <input
        id="player-search"
        value={text}
        onChange={(event) => setText(event.target.value)}
        placeholder="Spielername suchen, z. B. ShadowPlayer"
        minLength={2}
        maxLength={40}
        className="min-w-0 flex-1 rounded-lg border border-line bg-surface px-4 py-2.5 outline-none placeholder:text-muted focus:border-accent"
      />
      <button type="submit" className="rounded-lg bg-raised px-4 py-2.5 font-medium hover:bg-line">
        Suchen
      </button>
    </form>
  )
}

export function Search() {
  const [params] = useSearchParams()
  const q = params.get('q') ?? ''
  const results = useLoad(() => (q.length >= 2 ? api.search(q) : Promise.resolve([])), [q])

  return (
    <div className="space-y-6">
      <SearchBox key={q} initial={q} />
      <Card>
        <h1 className="mb-3 font-semibold">Ergebnisse für „{q}“</h1>
        {results.state === 'loading' && <Message>Suche läuft …</Message>}
        {results.state === 'error' && <Message>Die Suche ist gerade nicht erreichbar.</Message>}
        {results.state === 'ready' && results.data.length === 0 && (
          <Message>Kein öffentliches Profil gefunden. Profile erscheinen erst, wenn der Spieler zustimmt.</Message>
        )}
        {results.state === 'ready' && results.data.length > 0 && (
          <ul className="divide-y divide-line">
            {results.data.map((player) => (
              <li key={player.id}>
                <Link to={`/player/${player.id}`} className="flex items-center justify-between gap-4 py-3 hover:text-accent">
                  <span>
                    <span className="font-medium">{player.displayName}</span>
                    <span className="ml-2 font-mono text-xs text-muted">#{player.id.slice(-6)}</span>
                  </span>
                  <span className="tabular text-right text-sm text-muted">
                    {formatCount(player.matches)} Matches · K/D {formatKd(player.kdRatio)}
                    <span className="hidden sm:inline"> · zuletzt {formatDate(player.lastMatchAt)}</span>
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        )}
        {results.state === 'ready' && results.data.length > 1 && (
          <p className="mt-3 text-xs text-muted">
            Gleiche Namen sind getrennte Spieler. Die Kennung hinter dem Namen unterscheidet sie.
          </p>
        )}
      </Card>
    </div>
  )
}
