# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users
Call of Duty players from the public community (MW4 multiplayer, later Warzone) who install the Windows client and want to see their own statistics and compare themselves with others. They look at the site both at the PC next to the game and on the phone between sessions; both must work equally well.

## Product Purpose
COD Stats records a player's matches automatically from their own screen (OCR on the result screen and the after-action report) and shows them as public profiles, per-match details and rankings. Success: a player plays, the match appears on the site without any manual step, and the numbers are right.

## Positioning
Community-recorded, own data only: every value comes from the player's own PC, tied to a device identity (ECDSA key), not to a name alone. Values that could not be read stay unknown instead of guessed. Self-hostable (Docker), open to anyone who installs the client.

## Operating Context
- Windows client (tray app) recognises matches and uploads them; the website is read-only for visitors.
- Profiles are public only when the player allows it and the name is confirmed.
- Data per match: game, mode, map, result or "left", kills, deaths, assists, eliminations, score, placement (free-for-all, Warzone), team and enemy final score, XP, level, mode values (captures, data uploaded, confirms, objective time …).
- Statistics: K/D (summed, never averaged), win rate (left matches excluded), leave rate, streaks, best match, breakdown by mode and by map, rankings.

## Capabilities and Constraints
- Stack: React 19, React Router 8, Tailwind 4, Recharts, Vite; served by Caddy; API in ASP.NET Core under `/api/v1`.
- German UI (du-form), numbers in German format.
- Unknown values are shown as unknown ("–"), never as 0.
- Game, mode and map names may be shown. Map images and other game artwork: wanted later, not available now; do not fabricate or hotlink them.

## Brand Commitments
- Name: "COD Stats". No official Call of Duty / Activision logos, weapon or map artwork for now.

## Evidence on Hand
- Real matches of the author (MW4, Oct 2026) via the demo stack and the live server https://codstats.tabelander.co.at.
- No testimonials, user counts or press; do not invent any.

## Product Principles
1. Correct before complete: an unknown value is shown as unknown.
2. Every match tells its own story: each recorded game has its own page with everything that was read.
3. Own data only, visible only with consent.
4. Works the same at the desk and on the phone.
