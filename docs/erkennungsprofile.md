# Erkennungsprofile

Ein Profil beschreibt **einen Bildschirm eines Spiels bei einem Seitenverhältnis**. Bereiche sind relativ zum Spielfenster (0..1), damit dasselbe Profil für 1080p, 1440p und 4K gilt. Für 16:10 oder 21:9 braucht es eigene Profile.

```json
{
  "id": "mw4-mp-result-16x9-v1",
  "game": "mw4_mp",
  "screen": "result",
  "aspect": 1.7778,
  "verified": false,
  "anchors": [
    { "region": { "x": 0.40, "y": 0.04, "width": 0.20, "height": 0.07 }, "anyOf": ["VICTORY", "DEFEAT", "SIEG", "NIEDERLAGE"] }
  ],
  "fields": {
    "kills":  { "region": { "x": 0.62, "y": 0.41, "width": 0.05, "height": 0.035 } },
    "deaths": { "region": { "x": 0.68, "y": 0.41, "width": 0.05, "height": 0.035 } },
    "result": { "region": { "x": 0.40, "y": 0.04, "width": 0.20, "height": 0.07 } }
  },
  "ownName": { "region": { "x": 0.30, "y": 0.41, "width": 0.18, "height": 0.035 } },
  "winWords": ["VICTORY", "SIEG"],
  "lossWords": ["DEFEAT", "NIEDERLAGE"],
  "drawWords": ["DRAW", "UNENTSCHIEDEN"]
}
```

Die Werte oben sind **Beispiele, keine echten Koordinaten**.

| Feld | Bedeutung |
|---|---|
| `screen` | `menu`, `loading`, `inMatch` (auch Tab-Scoreboard), `result` |
| `game` | `mw4_mp`, `wz_br`, `wz_resurgence`; Pflicht bei `result` |
| `anchors` | Alle Anker müssen passen (ein Wort aus `anyOf`, ein falscher Buchstabe ab 6 Zeichen erlaubt) |
| `fields` | `kills`, `deaths`, `assists`, `score`, `placement`, `result`, `mode`, `map` |
| `verified` | Erst `true`, wenn das Profil gegen echte Screenshots getestet ist. Sonst nie „sichere“ Matches |

## Neues Profil erstellen

1. Screenshots in `tests/capture-fixtures/<spiel>/<auflösung>/` ablegen, mit gleichnamiger `.json` der richtigen Werte.
2. Bereiche ausmessen (Pixel ÷ Breite bzw. Höhe).
3. Profil nach `apps/desktop/CodStats.Desktop/profiles/` legen, Test gegen die Fixtures laufen lassen.
4. Erst wenn alle Fixtures richtig gelesen werden: `verified: true`.
