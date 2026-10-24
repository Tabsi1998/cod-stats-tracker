---
version: 1
slug: "apps-web"
primary_target: "apps/web"
related_targets: []
---

# Surface brief: COD Stats website (apps/web)

Mode: Operate. Visitors look up a player (often themselves), read their form and numbers, drill into a single match, compare in rankings. Desktop next to the game and phone between sessions, equally.

Content: real recorded matches only (mode, map, result or left, K/D/A, eliminations, score, final score, XP, level, mode values), statistics per game group and period, breakdown by mode and map, rankings with mode filter. Unknown values render as "–", never 0.

Constraints: no Activision logos, weapon or map artwork (names only); German UI; self-hosted fonts; prefers-reduced-motion respected.

## Direction contract

THESIS: A player's record reads like the game's own after-action report: one debrief per match, one service record per player. Refuses the category default of a grey dashboard of same-size stat cards with a single accent.

OWN-WORLD: Night-ops ground (deep blue-black, oklch ~0.15 0.02 250) with raised steel panels; condensed uppercase display (Saira Condensed) for names, numbers and result words, Barlow for UI text, tabular numerals. Cyan = win / own team, signal orange = loss / enemy, operator yellow = "you" and selection, slate = left. Signature: corner-bracket frames (the report's tile brackets) around every debrief cluster, drawn as thin L-corners, not boxes.

STORY: The visitor finds a player, sees at once how good they are (K/D, win rate, form of the last games), then opens any match and gets its full debrief; modes and maps answer "where am I strong".

FIRST VIEWPORT: Profile: name in large condensed caps with service line (since, matches, former names); game + period controls; the debrief panel in brackets: K/D huge, win rate, kills, matches, then the form strip of the last 20 results as cyan/orange/slate cells; tabs Overview / Matches / Modes / Maps beneath. Match page: result word as the banner (SIEG cyan, NIEDERLAGE orange, VERLASSEN slate), white mode tag + map, final score cyan | orange, tile row in brackets, XP and level.

FORM: Pinned by the user ("Taktisches HUD", 24.10.2026); concept roll not run because the direction was pinned; position 1 of 1. Seed key: none (user-pinned).

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance

Signature interaction and motion: on first render the bracket corners draw outward and the debrief numbers count up once, staggered; tabs move a yellow underline; table rows link to match pages with a subtle lift on hover. No motion with prefers-reduced-motion.
