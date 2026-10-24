import type { CSSProperties } from 'react'
import { Link } from 'react-router'
import { api, installerAvailable, installerUrl, type Leaderboard } from '../api'
import { formatCount, formatKd } from '../format'
import { SearchBox } from './Search'
import { useLoad, type Load } from '../load'
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
  const kills = useLoad(() => api.leaderboard('mw4', 'kills'), [])
  const installer = useLoad(installerAvailable, [])

  return (
    <div className="space-y-16 pt-12 sm:pt-20">
      <section className="grid gap-12 lg:grid-cols-[1.25fr_1fr]">
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

        <Bracket
          tone="you"
          className="rise flex flex-col gap-8 bg-surface/50 px-5 py-6 shadow-[0_24px_48px_-28px_rgb(0_0_0/0.9)] sm:px-7"
          style={{ '--i': 2 } as CSSProperties}
          label="Bestenlisten MW4"
        >
          <TopList title="Top K/D · MW4" board={top} format={formatKd} />
          <TopList title="Meiste Kills · MW4" board={kills} format={formatCount} />
          <Link
            to="/leaderboards"
            className="display mt-auto inline-flex items-center gap-1 self-start text-lg font-bold tracking-[0.1em] text-muted transition-colors duration-200 hover:text-you"
          >
            Alle Rankings, auch nach Modus
            <Icon name="next" className="size-4" />
          </Link>
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
        <ol className="grid gap-8 sm:grid-cols-3 sm:gap-6">
          {[
            ['Installieren', 'Setup ausführen, der Client legt sich in den Tray und startet künftig mit Windows.'],
            ['Zustimmen', 'Einmal bestätigen, dass deine Werte erfasst werden dürfen. Öffentlich wird dein Profil erst, wenn du es willst.'],
            ['Spielen', 'Nach jedem Match liest der Client Ergebnis und Einsatzbericht. Ein paar Sekunden später steht es hier.'],
          ].map(([title, text], i) => (
            <li key={title} className="border-t border-line pt-4">
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

/** The top five of one board, linking to their profiles. */
function TopList({ title, board, format }: { title: string; board: Load<Leaderboard>; format: (value: number) => string }) {
  return (
    <section>
      <h2 className="display mb-3 text-3xl font-bold">{title}</h2>
      {board.state === 'loading' && (
        <div className="space-y-px">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-11" />
          ))}
        </div>
      )}
      {board.state === 'error' && <Message>Rankings konnten nicht geladen werden.</Message>}
      {board.state === 'ready' && board.data.entries.length === 0 && (
        <p className="py-3 text-muted">Noch niemand hat {board.data.minMatches} Matches für ein Ranking. Sei der Erste.</p>
      )}
      {board.state === 'ready' && board.data.entries.length > 0 && (
        <ol>
          {board.data.entries.slice(0, 5).map((entry) => (
            <li key={entry.playerId}>
              <Link
                to={`/player/${entry.playerId}`}
                className="group grid grid-cols-[2.5rem_1fr_auto] items-center gap-3 border-b border-line/60 py-2.5 transition-colors duration-200 hover:bg-surface"
              >
                <span className={`display text-2xl font-extrabold ${entry.rank === 1 ? 'text-you' : 'text-faint'}`}>{entry.rank}</span>
                <span className="display truncate text-xl font-bold group-hover:text-you">{entry.displayName}</span>
                <span className="display text-2xl font-bold">{format(entry.value)}</span>
              </Link>
            </li>
          ))}
        </ol>
      )}
    </section>
  )
}
