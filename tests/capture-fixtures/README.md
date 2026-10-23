# Capture-Fixtures

Echte Spielbilder für die Erkennungstests. Bilder werden **nicht** eingecheckt (siehe `.gitignore`), weil fremde Spielernamen sichtbar sind.

Ablage: `<spiel>/<auflösung>/<bildschirm>_<nr>.png`, z. B. `mw4/2560x1440/scoreboard_end_01.png`.

Zu jedem Bild eine gleichnamige `.json` mit den richtigen Werten (Kills, Deaths, …), damit Tests die Erkennung prüfen können. Die JSON-Dateien dürfen ins Repo, solange sie keine fremden Namen enthalten.

Benötigt:

1. MW4 Multiplayer: Scoreboard während des Matches, eigene Zeile hervorgehoben
2. MW4 Multiplayer: Ergebnisbildschirm nach Matchende (Sieg und Niederlage, verschiedene Modi)
3. Warzone BR: Endbildschirm mit Platzierung und Kills
4. Warzone Resurgence: Endbildschirm
5. Menü/Lobby mit eigenem Spielernamen
6. Ladebildschirm/Matchstart beider Spiele
