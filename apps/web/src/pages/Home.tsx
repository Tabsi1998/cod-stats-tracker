import type { CSSProperties } from 'react'
import { Link } from 'react-router'
import { api, installerAvailable, installerUrl } from '../api'
import { formatCount, formatKd } from '../format'
import { SearchBox } from './Search'
import { useLoad } from '../load'
import { useCountUp } from '../motion'
import { Bracket, CommunityNote, Icon, Message, Skeleton } from '../ui'

/** A platform number inside the lead sentence, counting up once. */
function Live({ value }: { value: number }) {
  const shown = useCountUp(value, 1200)
  return <span className="display text-you tabular">{formatCount(Math.round(shown ?? 0))}</span>
}

export function Home() {
  const platform = useLoad(api.platformStats, [])
  const top = useLoad(() => api.leaderboard('mw4', 'kd'), [])
  const installer = useLoad(installerAvailable, [])

  return (
    <div className="space-y-16 pt-12 sm:pt-20">
      <section className="grid gap-12 lg:grid-cols-[1.25fr_1fr] lg:items-end">
        <div className="space-y-7">
          <h1 className="rise display text-6xl font-extrabold sm:text-8xl">
            Jedes Match.
            <br />
            <span className="text-win">Automatisch</span> erfasst.
          </h1>
          <p className="rise max-w-[58ch] text-xl text-muted" style={{ '--i': 1 } as CSSProperties}>
            Der Windows-Client liest am Matchende deinen Einsatzbericht in MW4 und Warzone und stellt ihn hierher: Ergebnis,
            K/D, Modus, Karte, XP. Kein Konto, kein Passwort, öffentlich nur mit deiner Zustimmung.
          </p>
          <div className="rise text-xl" style={{ '--i': 2 } as CSSProperties} aria-live="polite">
            {platform.state === 'ready' ? (
              <>
                Bisher <Live value={platform.data.matches} /> {platform.data.matches === 1 ? 'Match' : 'Matches'} und{' '}
                <Live value={platform.data.kills} /> Kills von <Live value={platform.data.players} />{' '}
                {platform.data.players === 1 ? 'Spieler' : 'Spielern'}.
              </>
            ) : platform.state === 'loading' ? (
              <Skeleton className="h-7 w-96 max-w-full" />
            ) : null}
          </div>
          <div className="rise" style={{ '--i': 3 } as CSSProperties}>
            <SearchBox />
          </div>
        </div>

        <Bracket tone="you" className="rise px-5 py-6 sm:px-7" style={{ '--i': 2 } as CSSProperties} label="Top K/D in MW4">
          <div className="mb-4 flex items-baseline justify-between gap-4">
            <h2 className="display text-3xl font-bold">Top K/D · MW4</h2>
            <Link to="/leaderboards" className="inline-flex items-center gap-1 text-muted transition-colors duration-200 hover:text-you">
              Alle Rankings
              <Icon name="next" className="size-4" />
            </Link>
          </div>
          {top.state === 'loading' && (
            <div className="space-y-px">
              {[0, 1, 2, 3, 4].map((i) => (
                <Skeleton key={i} className="h-11" />
              ))}
            </div>
          )}
          {top.state === 'error' && <Message>Rankings konnten nicht geladen werden.</Message>}
          {top.state === 'ready' && top.data.entries.length === 0 && (
            <Message>Noch niemand hat {top.data.minMatches} Matches für ein Ranking. Sei der Erste.</Message>
          )}
          {top.state === 'ready' && top.data.entries.length > 0 && (
            <ol>
              {top.data.entries.slice(0, 5).map((entry) => (
                <li key={entry.playerId}>
                  <Link
                    to={`/player/${entry.playerId}`}
                    className="group grid grid-cols-[2.5rem_1fr_auto] items-center gap-3 border-b border-line/60 py-2.5 transition-colors duration-200 hover:bg-surface"
                  >
                    <span className={`display text-2xl font-extrabold ${entry.rank === 1 ? 'text-you' : 'text-faint'}`}>{entry.rank}</span>
                    <span className="display truncate text-xl font-bold group-hover:text-you">{entry.displayName}</span>
                    <span className="display text-2xl font-bold">{formatKd(entry.value)}</span>
                  </Link>
                </li>
              ))}
            </ol>
          )}
        </Bracket>
      </section>

      <section className="grid gap-10 border-t border-line pt-12 lg:grid-cols-[1fr_1.4fr]">
        <div className="space-y-5">
          <h2 className="display text-5xl font-extrabold">Der Client</h2>
          <p className="max-w-[55ch] text-lg text-muted">
            Einmal installieren und vergessen. Er startet mit Windows, wartet im Tray auf das Spiel und liest nur deinen
            eigenen Bildschirm, keinen Spielspeicher.
          </p>
          {installer.state === 'ready' && installer.data ? (
            <a
              href={installerUrl}
              className="display inline-flex items-center gap-2 bg-you px-6 py-3 text-xl font-bold tracking-[0.1em] text-bg transition-[filter,transform] duration-200 hover:brightness-110 active:translate-y-px"
            >
              <Icon name="download" />
              Für Windows herunterladen
            </a>
          ) : (
            <p className="text-muted">{installer.state === 'loading' ? '…' : 'Der Download folgt in Kürze.'}</p>
          )}
          <p className="text-sm text-faint">Windows 10 oder 11, 64 Bit.</p>
        </div>
        <ol className="grid gap-px bg-line sm:grid-cols-3">
          {[
            ['Installieren', 'Setup ausführen, der Client legt sich in den Tray und startet künftig mit Windows.'],
            ['Zustimmen', 'Einmal bestätigen, dass deine Werte erfasst werden dürfen. Öffentlich wird dein Profil erst, wenn du es willst.'],
            ['Spielen', 'Nach jedem Match liest der Client Ergebnis und Einsatzbericht. Ein paar Sekunden später steht es hier.'],
          ].map(([title, text], i) => (
            <li key={title} className="bg-bg p-5">
              <span className="display text-5xl font-extrabold text-win">{i + 1}</span>
              <h3 className="display mt-3 text-2xl font-bold">{title}</h3>
              <p className="mt-2 text-muted">{text}</p>
            </li>
          ))}
        </ol>
      </section>

      <CommunityNote />
    </div>
  )
}
