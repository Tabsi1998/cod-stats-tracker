---
name: COD Stats
description: A player's record as the game's own after-action report, on night-ops ground.
colors:
  bg: "oklch(0.155 0.018 252)"
  deep: "oklch(0.125 0.016 252)"
  surface: "oklch(0.195 0.02 250)"
  raised: "oklch(0.235 0.022 248)"
  line: "oklch(0.32 0.022 248)"
  bracket: "oklch(0.62 0.03 240)"
  text: "oklch(0.955 0.008 240)"
  muted: "oklch(0.72 0.022 240)"
  faint: "oklch(0.6 0.02 242)"
  win: "oklch(0.84 0.14 205)"
  loss: "oklch(0.72 0.17 45)"
  you: "oklch(0.88 0.16 98)"
  left: "oklch(0.66 0.03 265)"
  draw: "oklch(0.8 0.02 240)"
typography:
  display:
    fontFamily: "Saira Condensed, Arial Narrow, sans-serif"
    fontSize: "3.75rem"
    fontWeight: 800
    lineHeight: 0.95
    letterSpacing: "0.02em"
  figure:
    fontFamily: "Saira Condensed, Arial Narrow, sans-serif"
    fontSize: "3rem"
    fontWeight: 700
    lineHeight: 0.95
    letterSpacing: "0.02em"
    fontFeature: "tnum"
  headline:
    fontFamily: "Saira Condensed, Arial Narrow, sans-serif"
    fontSize: "1.5rem"
    fontWeight: 700
    lineHeight: 0.95
    letterSpacing: "0.02em"
  title:
    fontFamily: "Saira Condensed, Arial Narrow, sans-serif"
    fontSize: "1.25rem"
    fontWeight: 700
    lineHeight: 0.95
    letterSpacing: "0.02em"
  label:
    fontFamily: "Saira Condensed, Arial Narrow, sans-serif"
    fontSize: "0.875rem"
    fontWeight: 600
    lineHeight: 0.95
    letterSpacing: "0.12em"
  body:
    fontFamily: "Barlow, Segoe UI, system-ui, sans-serif"
    fontSize: "1rem"
    fontWeight: 400
    lineHeight: 1.5
    letterSpacing: "normal"
    fontFeature: "tnum"
  caption:
    fontFamily: "Barlow, Segoe UI, system-ui, sans-serif"
    fontSize: "0.875rem"
    fontWeight: 400
    lineHeight: 1.43
    letterSpacing: "normal"
rounded:
  none: "0px"
spacing:
  hairline-gap: "6px"
  sm: "12px"
  md: "20px"
  lg: "32px"
  section: "40px"
  page-gutter: "16px"
  page-gutter-sm: "24px"
components:
  button-primary:
    backgroundColor: "{colors.you}"
    textColor: "{colors.bg}"
    typography: "{typography.title}"
    rounded: "{rounded.none}"
    padding: "12px 24px"
  button-outline:
    backgroundColor: "transparent"
    textColor: "{colors.muted}"
    typography: "{typography.title}"
    rounded: "{rounded.none}"
    padding: "12px 0"
  button-outline-hover:
    textColor: "{colors.text}"
  segmented:
    backgroundColor: "{colors.deep}"
    textColor: "{colors.muted}"
    rounded: "{rounded.none}"
    padding: "8px 14px"
  segmented-active:
    backgroundColor: "{colors.raised}"
    textColor: "{colors.text}"
  tab:
    textColor: "{colors.muted}"
    typography: "{typography.title}"
    padding: "12px 8px 10px"
  tab-active:
    textColor: "{colors.text}"
  input-search:
    backgroundColor: "{colors.deep}"
    textColor: "{colors.text}"
    rounded: "{rounded.none}"
    padding: "12px 16px 12px 40px"
  outcome-tag-win:
    textColor: "{colors.win}"
    typography: "{typography.label}"
    rounded: "{rounded.none}"
    padding: "2px 8px"
  outcome-tag-loss:
    textColor: "{colors.loss}"
    typography: "{typography.label}"
    rounded: "{rounded.none}"
    padding: "2px 8px"
  outcome-tag-left:
    textColor: "{colors.left}"
    typography: "{typography.label}"
    rounded: "{rounded.none}"
    padding: "2px 8px"
  mode-tag:
    backgroundColor: "{colors.text}"
    textColor: "{colors.bg}"
    typography: "{typography.title}"
    rounded: "{rounded.none}"
    padding: "4px 12px"
  bracket-cluster:
    backgroundColor: "{colors.surface}"
    rounded: "{rounded.none}"
    padding: "28px 32px"
  skeleton:
    backgroundColor: "{colors.raised}"
    rounded: "{rounded.none}"
---

# Design System: COD Stats

## Overview

**Creative North Star: "The After-Action Report"**

Every surface reads like the debrief the game shows after a match: one report per match, one service record per player. Numbers are the protagonists, set huge in condensed uppercase on a deep blue-black night-ops ground, each with a small tracked label beneath, exactly as the in-game report stacks value over caption. The colours are the game's own team colours, used as meaning rather than decoration: cyan is your side and a win, signal orange is the enemy and a loss, operator yellow is "you" and the current selection, slate is a match that was left.

Density is high but calm. Clusters of figures are held by the system's signature, four thin L-shaped corner brackets drawn around a cluster instead of a box, and everything else is separated by single hairlines, never by rounded cards. Shapes are square throughout. Motion happens once, on arrival: brackets close in onto their cluster, numbers count up, the result word flares, form cells fill, bars grow. After that, the page is still. The build refuses the category default of a grey dashboard of same-size stat cards with one accent.

Unknown values render as an en dash ("–"), never as 0; this is a visual rule as much as a data rule, since the dash sits in the same figure slot and the same type as a real number.

**Key Characteristics:**
- Night-ops ground: blue-black base with a faint cool radial glow from the top left, fixed to the viewport.
- Condensed uppercase display type (Saira Condensed) for names, numbers, result words, labels and controls; Barlow for running text; tabular numerals everywhere.
- Outcome colours carry meaning only: win/own cyan, loss/enemy orange, you/selection yellow, left slate.
- Corner-bracket frames around every debrief cluster; hairlines elsewhere; zero corner radius.
- One-time arrival motion on an expo-out curve, fully disabled under reduced motion.

## Colors

A dark, cool, low-chroma steel ground with four saturated role colours borrowed from the game's scoreboards; the role colours are never used for ornament.

### Primary
- **Operator Yellow** (`you`): the viewer's own perspective and the current selection. The K/D hero figure, XP totals, rank 1, the "Stats" half of the wordmark, the active underline in tabs and segmented controls, the primary button fill, focus rings, text caret and selection tint, and the hover colour of links and names.

### Secondary
- **Own-Team Cyan** (`win`): a win and your own team. Win result word ("SIEG"), win form cells and outcome tags, your team's final score, win-rate bars and the win-rate line chart, the step numerals on the home page, and the "Automatisch" word in the home headline.
- **Signal Orange** (`loss`): a loss and the enemy. Loss result word, loss form cells and tags, enemy final score, the unfilled remainder of win-rate bars (at 40%), negative deltas against the player's average.

### Tertiary
- **Left Slate** (`left`): a match that was left before the end. Its result word ("VERLASSEN"), form cells, tags and the left tone of the bracket.
- **Draw Grey** (`draw`): a drawn match; tag and text only.

### Neutral
- **Night-Ops Ground** (`bg`): the page background, under a `radial-gradient(120% 60% at 15% -10%, oklch(0.22 0.04 230 / 0.55), transparent 60%)` glow; also the text colour on yellow and white fills.
- **Deep Well** (`deep`): recessed controls (segmented groups at 70%, search input at 80%), footer (60%), scrollbar track.
- **Steel Surface** (`surface`): bracket clusters at 50%, row hover background, segmented hover.
- **Raised Steel** (`raised`): the active segment, chart tooltip, chart hover cursor, skeleton blocks (70%).
- **Hairline** (`line`): every divider, table rule, input and control border, chart grid (50%), scrollbar thumb.
- **Bracket Steel** (`bracket`): the default colour of the corner brackets.
- **Report White** (`text`): primary text and figures; also the fill of the white mode tag on the match page.
- **Muted Steel** (`muted`): secondary text, figure labels, inactive tabs and controls.
- **Faint Steel** (`faint`): hints, captions, table headers, axis ticks, ranks below the podium, chevrons at rest.

### Named Rules
**The Team Colour Rule.** Cyan, orange, yellow and slate always mean win/own, loss/enemy, you/selection and left. A colour that does not carry one of those meanings stays neutral. The one standing exception is the home page, where cyan marks the product's own side (the headline word "Automatisch" and the step numerals).

**The Tint, Not Fill Rule.** Outcome tags use their role colour at 15% as background with the full colour as text; only form cells, bars, the primary button and the mode tag use solid fills.

## Typography

**Display Font:** Saira Condensed (with Arial Narrow, sans-serif), self-hosted, weights 500–800
**Body Font:** Barlow (with Segoe UI, system-ui, sans-serif), self-hosted, weights 400–600

**Character:** A narrow, squared military condensed face for everything the report itself would print, always uppercase with slight tracking and a tight 0.95 line height, against a plain, legible grotesque for explanatory sentences. All numerals are tabular, so columns and count-ups never jitter.

### Hierarchy
- **Display** (800, 3.75rem → 8rem by breakpoint, 0.95): the one page-defining word: the player name (3.75/4.5rem), the match result word (4.5/8rem), the home headline (3.75/6rem), page titles at 3rem.
- **Figure** (700–800, 2.25rem / 3rem–3.75rem / 3.75rem–6rem for md/lg/xl, 0.95, tabular): count-up statistics. The final score is the largest figure (6rem → 8rem). A trailing "%" or "XP" steps down to about 0.55em on the same line.
- **Headline** (700, 1.5rem–1.875rem, 0.95): section headings inside a page (chart titles, "Modus-Werte", "Fortschritt", list titles).
- **Title** (700, 1.125rem–1.25rem, tracking 0.06–0.14em): table cell names (mode, map, player), navigation, tabs, buttons, mode and map tags.
- **Body** (Barlow 400, 1rem, 1.5): running text; lead paragraphs at 1.125–1.25rem in Muted Steel, capped at 55–60ch; the community note capped at 70ch.
- **Label** (Saira 600, 0.875rem, tracking 0.12em, uppercase): the caption beneath every figure and the table header row.
- **Caption** (Barlow 400, 0.875rem): hints under figures, legends, notes, metadata lines in Faint Steel.

### Named Rules
**The Report Print Rule.** If the in-game report would print it (a name, a number, a result, a label, a control), it is set in the condensed uppercase display face. Sentences that explain are set in Barlow, sentence case.

**The Value Over Label Rule.** A figure is always the number first, large, with its label beneath in small tracked caps; never a label above a number.

## Layout

A single centred column of at most 72rem (Tailwind `max-w-6xl`) with 16px gutters, 24px from 640px up. Pages start 40px below the sticky header (48–80px on the home page) and stack their blocks with 32–64px of vertical rhythm (`space-y-8` to `space-y-16`). Inside a bracket cluster figures sit on a two-column grid (gaps 24px × 32px) that becomes a two-part layout at 1024px (figures | form strip, or six tiles in a row on the match page). The header is sticky with an 85% ground and backdrop blur; the compact search appears in it from 768px.

Lists switch form, not just size: below 640px the match list is a stacked row per match (mode + outcome tag, date and map line, K/D at the right); from 640px it is a full table with a 44rem minimum width that scrolls horizontally. Breakdown tables scroll horizontally on phones and hide their inline win-rate bars there. Segmented controls wrap.

## Elevation & Depth

Mostly flat, tonal layering: depth comes from stepping the ground through deep → bg → surface → raised, from translucent fills over the radial glow, and from hairlines. One shadow exists and it belongs to the bracket clusters only: a long, low, soft fall-off that lifts the debrief off the ground without outlining it. Text glow appears only inside the result-word flare animation and fades to nothing.

### Shadow Vocabulary
- **Debrief Lift** (`box-shadow: 0 24px 48px -28px rgb(0 0 0 / 0.9)`): under every bracket cluster that carries a surface fill (profile debrief, further values, match tiles, home top lists).

### Named Rules
**The One Lift Rule.** Only a bracket cluster casts a shadow. Panels, rows, controls and tags stay flat; hover is a tonal background change, not elevation.

## Shapes

Square everywhere: zero radius on clusters, controls, inputs, buttons, tags, skeletons, form cells, bars and chart tooltips. Form is drawn by line, not by closed boxes: the corner bracket (four L-corners, 14px arms, 1.5px stroke, in Bracket Steel or a role colour), single hairline rules on top or bottom of sections and rows, and a 2px yellow bar under active tabs and segments. Form-strip cells are square (aspect 1:1) on a ten-column grid with 6px gaps. The wordmark repeats the bracket: two cyan and two orange corners around a yellow square, drawn in SVG.

Icons are one family: 20px box, 1.75px stroke, round caps and joins, `currentColor`, inline SVG.

## Components

### Buttons
Few and blunt; the system prefers links and controls over buttons.
- **Shape:** square corners (0px).
- **Primary:** Operator Yellow fill, Night-Ops Ground text, display face 1.125–1.25rem bold, tracking 0.1em, 12px × 24px padding (search submit, Windows download).
- **Hover / Active:** `brightness(1.1)` on hover, `brightness(0.95)` or a 1px press-down on active, 200ms.
- **Outline:** full-width hairline border, Muted Steel text in the display face; on hover the border turns yellow and the text white ("Weitere Matches"). Disabled at 50% opacity.

### Chips (Outcome and mode tags)
- **Outcome tag:** role colour at 15% background, role-colour text, display face 0.875rem bold, tracking 0.1em, 2px × 8px. Unknown uses the hairline at 40% with muted text.
- **Mode tag:** solid Report White plate with ground-coloured text, tracking 0.14em, next to the map name in Muted Steel; on the match page only.

### Cards / Containers
- **Bracket cluster:** the debrief container. Steel Surface at 50%, corner brackets in the outcome tone of its content (yellow on the home top lists, cyan/orange/slate on the match tiles, neutral steel on the profile), 28–32px padding (20px on phones), Debrief Lift shadow. Leaderboards use the bracket without fill or shadow.
- **Sections:** separated by a single top hairline with 20px of padding above the content; no card.

### Inputs / Fields
- **Search:** Deep Well at 80%, hairline border, 40px left padding for the inline search icon in Faint Steel, Faint Steel placeholder, yellow caret. Compact variant in the header at 0.95rem.
- **Focus:** the border turns Operator Yellow (200ms colour transition); keyboard focus elsewhere is a 2px yellow outline with 2px offset.
- **Select:** the mode filter matches the segmented control (deep fill, hairline, display face, yellow border on focus).

### Navigation
- **Header:** sticky, 85% ground with backdrop blur, bottom hairline. Wordmark at 1.5rem extrabold, "Stats" in yellow. Nav links in the display face 1.125rem bold, tracking 0.1em, Muted Steel → Report White on hover and when active.
- **Segmented control (game, period, metric):** a hairline-bordered Deep Well group; inactive segments in Muted Steel, hover to Steel Surface; the active segment fills Raised Steel with white text and a 2px yellow bar inset 8px from each side at the bottom. Radio-group semantics.
- **Tabs:** equal-width tabs over a bottom hairline, display face 1rem → 1.125rem bold, tracking 0.08em; an optional count follows in Barlow Faint Steel. A single 2px yellow bar slides to the active tab (500ms, expo-out).
- **Back link:** chevron icon plus text in Muted Steel, yellow on hover.

### Row links (lists and tables)
Every match row, search result and ranking row is a link. Hover changes the row background to Steel Surface, turns the name yellow and nudges the trailing chevron 2px right while it turns yellow (200ms). No lift, no shadow.

### Figure (signature)
The number large in the display face, its tracked label beneath, an optional caption hint below that. It counts up once from 0 on first appearance over 900ms with an exponential ease-out (the home page's inline counters run 1200ms); later value changes appear immediately; null stays "–". Tone switches the number to yellow (you) or an outcome colour.

### Form strip (signature)
The last 20 results, oldest left, as solid square cells in outcome colours on a ten-column grid; cells grow from the bottom one after another (500ms, 25ms stagger after 300ms). A legend of 10px squares and a caption explain it; screen readers get the sequence as text.

### Result banner (signature)
On the match page the outcome word is the headline (SIEG, NIEDERLAGE, VERLASSEN) in its role colour at 4.5rem → 8rem, flaring once on arrival (1400ms: tracking closes from 0.12em to 0.02em while a currentColor glow peaks and fades). The final score sits beside it as two huge figures, cyan | orange, with labels beneath.

### Bars and charts
Win-rate bars are 6px tall: cyan fill over an orange 40% track; ranking bars are 4px, yellow for rank 1 and cyan at 70% otherwise; both grow from the left once (900ms, 40ms stagger). Charts use the hairline grid at 50% (horizontal only), Faint Steel axis text at 13px without axis lines or ticks, kills bars coloured by outcome, K/D line in yellow and win-rate line in cyan at 2.5px, square Raised Steel tooltips.

### Filter bar
Two lines under the player's name: game and period as segmented controls, then the narrowing filters (mode, map, weapon, class, outcome) as selects behind a filter icon. A select is set like the segmented control (Deep Well, hairline, display face); while it narrows, its border and chevron turn Operator Yellow, the selection colour. Options list only what the player has played, each with its match count ("KAPERN · 12"). "Filter aufheben" appears as a plain text control with a close icon only when something narrows. Every filter lives in the URL. A name in a breakdown table (mode, map, weapon) and a map × mode cell narrow the filters to it and return to the overview.

### Delta
The change against the period right before, as the hint under a debrief figure: an up or down arrow and the difference, cyan when it improved and orange when it got worse (the outcome colours: better = own side), "ggü. gestern" in Faint Steel. Shown only when the earlier period had matches.

### Map × mode matrix and session rows
The matrix is a hairline table: map names as row titles, modes as column heads, each played cell a row link with the win rate in the display face, the match count in Faint Steel and a full-width 6px win-rate bar; unplayed cells hold "–". Session rows stack date and time span, a strip of 20px outcome squares (each a link to its match), and K/D, matches and best match as figures, separated by hairlines. Records put the best kill count in a yellow bracket and the rest on a figure grid, each linking to its match.

### Skeleton
Square Raised Steel blocks at 70% with a pulse, in the shape of the content that will arrive.

## Do's and Don'ts

### Do:
- **Do** frame every debrief cluster with the four-corner bracket (14px arms, 1.5px), tinted to the outcome it reports.
- **Do** set numbers, names, results, labels and controls in Saira Condensed uppercase with tabular numerals, and stack value over label.
- **Do** keep cyan = win/own, orange = loss/enemy, yellow = you/selection, slate = left, on every surface including charts and the form strip.
- **Do** show an unknown value as "–" in the same slot and type a number would use.
- **Do** run arrival motion once on the expo-out curve (`cubic-bezier(0.16, 1, 0.3, 1)`), stagger by index (60ms for rising content), and turn it all off under `prefers-reduced-motion`.
- **Do** separate sections with a single hairline and let tonal steps (deep, bg, surface, raised) carry depth.

### Don't:
- **Don't** use rounded corners anywhere; the system radius is 0.
- **Don't** wrap content in closed bordered or rounded cards; use brackets for clusters and hairlines for everything else.
- **Don't** build a grid of same-size stat cards with a single accent; give the defining figure the largest size and let the rest step down.
- **Don't** use the role colours decoratively; outside the home page's cyan headline word and step numerals they mean win, loss, you/selection or left only.
- **Don't** cast shadows outside bracket clusters; hover is a background tone, not a lift.
- **Don't** show game artwork, map images or Activision logos; names only.

## Link-Vorschau

`apps/web/public/og.png` (1200 × 630) ist die Vorschau beim Teilen, für alle Seiten gleich. Quelle: `apps/web/design/og-image.html` (Schriften und Farben der Webseite), mit Chromium in genau dieser Größe gerendert. Profile und Matches bringen ihren Text aus der API mit (ARCHITECTURE.md, „Link-Vorschau“); das Bild bleibt dasselbe.
