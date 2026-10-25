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

### Lobby-Vergleich (`ownRow.lobby`)

`"lobby": true` in `ownRow` liest aus denselben Tabellen zusätzlich die Lobby: Anzahl der Zeilen (Name in der Namensspalte und Wert unter der Punkte-Überschrift, `columns.score`) und wie viele davon mehr Punkte haben. Die Namensspalte liegt in jeder Tabelle beim gleichen Abstand vom linken Rand wie der eigene Name. Nur für das End-Scoreboard einschalten, wo beide Teams vollständig sind. Ergebnis in den Feldern `lobby.size` und `lobby.rank`; in `expected.json` als `lobbySize` und `lobbyRank` prüfbar.

## Neues Profil erstellen

1. Screenshots in `tests/capture-fixtures/<spiel>/<auflösung>/` ablegen, mit gleichnamiger `.json` der richtigen Werte.
2. Bereiche ausmessen (Pixel ÷ Breite bzw. Höhe).
3. Profil nach `apps/desktop/CodStats.Desktop/profiles/` legen, Test gegen die Fixtures laufen lassen.
4. Erst wenn alle Fixtures richtig gelesen werden: `verified: true`.

## Was die echten MW4-Bilder gelehrt haben (24.10.2026)

Erste Profile in `apps/desktop/CodStats.Desktop/profiles/`, gemessen an 3840×2160-Aufnahmen mit deutscher Oberfläche (Herrschaft, Stellung, Team-Deathmatch, Inflation):

| Erkenntnis | Lösung im Profil |
|---|---|
| Spalten wechseln je Modus (GELD, ZIELZEIT, ASSISTS, TODE …) | `ownRow.columns`: Spalte über ihre Überschrift finden, nicht über eine feste Position |
| Die eigene Zeile steht mal auf Platz 1, mal auf Platz 6 | `ownRow`: Name aus der eigenen Leiste in den Tabellen suchen (`tables`, beide Seiten) |
| Einzelne Ziffern allein liest die OCR oft nicht | ganze Tabelle bzw. ganze Leiste in einem Durchgang lesen; `labelled`: Wert unter der Beschriftung („TODE“ → „17“) |
| „SIEG“ ist so groß, dass die OCR es nicht erkennt | `maxHeight: 40` verkleinert den Bereich vorher |
| SIEG steht links, NIEDERLAGE rechts | `more`: zweiter Bereich für Anker und Feld |
| Weißer HUD-Text auf hellem Himmel | `whiteText: true`: nur fast weiße Pixel bleiben |
| Ergebnisbildschirm nur ~4 s sichtbar, danach Wiederholung „Beste Aktion“ | Client liest dort alle 0,5 s; Auswertung 8 s nach dem Ende des Ergebnisses |
| Bei 1440p und 1080p liest die OCR das Banner bei anderen Größen | `tryHeights: [40, 80, 0]`: in drei Größen lesen, alle Texte zählen |
| Tabellenschrift bei 1080p nur ~13 px hoch | `minPixelHeight`: Bereich vorher vergrößern (höchstens bis zur OCR-Grenze) |
| Eliminierungen = Abschüsse + Assists | gespeichert werden Abschüsse als Kills; die K/D im Spiel („Elim/T-Quote“) rechnet mit Eliminierungen |

Werkzeug dazu: `tools/CaptureFixtures` (Aufnehmen, `ocr`, `read`, `scan`, `replay`).

