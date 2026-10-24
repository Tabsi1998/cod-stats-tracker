import { useState, type CSSProperties, type FormEvent } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { api } from '../api'
import { formatCount, formatDate, formatKd } from '../format'
import { useLoad } from '../load'
import { Icon, Message, Skeleton } from '../ui'

export function SearchBox({ initial = '', compact = false }: { initial?: string; compact?: boolean }) {
  const [text, setText] = useState(initial)
  const navigate = useNavigate()
  const id = compact ? 'player-search-header' : 'player-search'

  function submit(event: FormEvent) {
    event.preventDefault()
    const q = text.trim()
    if (q.length >= 2) navigate(`/search?q=${encodeURIComponent(q)}`)
  }

  return (
    <form onSubmit={submit} role="search" className={`flex ${compact ? '' : 'max-w-2xl'}`}>
      <label htmlFor={id} className="sr-only">
        Spielername
      </label>
      <div className="relative min-w-0 flex-1">
        <Icon name="search" className="pointer-events-none absolute top-1/2 left-3 size-5 -translate-y-1/2 text-faint" />
        <input
          id={id}
          value={text}
          onChange={(event) => setText(event.target.value)}
          placeholder={compact ? 'Spieler suchen' : 'Spielername, z. B. Tabsi98'}
          minLength={2}
          maxLength={40}
          className={`w-full border border-line bg-deep/80 pl-10 text-text outline-none transition-colors duration-200 placeholder:text-faint focus:border-you ${
            compact ? 'py-1.5 pr-3 text-[0.95rem]' : 'py-3 pr-4 text-lg'
          }`}
        />
      </div>
      {!compact && (
        <button
          type="submit"
          className="display bg-you px-6 text-lg font-bold tracking-[0.1em] text-bg transition-[filter] duration-200 hover:brightness-110 active:brightness-95"
        >
          Suchen
        </button>
      )}
    </form>
  )
}

export function Search() {
  const [params] = useSearchParams()
  const q = params.get('q') ?? ''
  const results = useLoad(() => (q.length >= 2 ? api.search(q) : Promise.resolve([])), [q])

  return (
    <div className="space-y-8 pt-10">
      <h1 className="display text-5xl font-extrabold">Spieler finden</h1>
      <SearchBox key={q} initial={q} />
      {q.length >= 2 && (
        <section aria-live="polite" className="space-y-3">
          <h2 className="text-muted">
            Ergebnisse für „<span className="text-text">{q}</span>“
          </h2>
          {results.state === 'loading' && (
            <div className="space-y-px">
              {[0, 1, 2].map((i) => (
                <Skeleton key={i} className="h-16" />
              ))}
            </div>
          )}
          {results.state === 'error' && <Message>Die Suche ist gerade nicht erreichbar. Versuch es gleich noch einmal.</Message>}
          {results.state === 'ready' && results.data.length === 0 && (
            <Message>Kein öffentliches Profil gefunden. Profile erscheinen erst, wenn der Spieler im Client zustimmt.</Message>
          )}
          {results.state === 'ready' && results.data.length > 0 && (
            <ul className="divide-y divide-line border-y border-line">
              {results.data.map((player, i) => (
                <li key={player.id} className="rise" style={{ '--i': i } as CSSProperties}>
                  <Link
                    to={`/player/${player.id}`}
                    className="group flex items-center justify-between gap-4 px-2 py-4 transition-colors duration-200 hover:bg-surface"
                  >
                    <span className="min-w-0">
                      <span className="display block truncate text-2xl font-bold group-hover:text-you">{player.displayName}</span>
                      <span className="text-sm text-faint">
                        Kennung #{player.id.slice(-6)} · zuletzt {formatDate(player.lastMatchAt)}
                      </span>
                    </span>
                    <span className="flex shrink-0 items-center gap-6 text-right">
                      <span>
                        <span className="display block text-2xl font-bold">{formatKd(player.kdRatio)}</span>
                        <span className="text-sm text-faint">K/D</span>
                      </span>
                      <span className="hidden sm:block">
                        <span className="display block text-2xl font-bold">{formatCount(player.matches)}</span>
                        <span className="text-sm text-faint">Matches</span>
                      </span>
                      <Icon name="next" className="size-5 text-faint transition-transform duration-200 group-hover:translate-x-0.5 group-hover:text-you" />
                    </span>
                  </Link>
                </li>
              ))}
            </ul>
          )}
          {results.state === 'ready' && results.data.length > 1 && (
            <p className="text-sm text-faint">Gleiche Namen sind getrennte Spieler; die Kennung unterscheidet sie.</p>
          )}
        </section>
      )}
    </div>
  )
}
