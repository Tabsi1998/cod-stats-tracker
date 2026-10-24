# Changelog

Alle wichtigen Änderungen an diesem Projekt. Format nach [Keep a Changelog](https://keepachangelog.com/de/1.1.0/), Versionen nach [SemVer](https://semver.org/lang/de/).

## [Unreleased]

### Hinzugefügt
- Server und Client speichern alle erkannten Werte: Status (beendet/verlassen), Eliminierungen, Endstand, XP, Level und Modus-Werte; ältere Clients laden weiter ohne sie hoch.
- Auswertung: Verlassen-Quote, längste Siegesserie, bestes Match, Ø Punkte, Ø XP, XP gesamt, Eliminierungen gesamt.
- Auswertung nach Modus und nach Karte (Matches, Siegquote, K/D, Ø Kills, Ø Punkte).
- Öffentlicher Endpunkt für ein einzelnes Match (`/api/v1/public/players/{id}/matches/{matchId}`) für die Match-Detailseite.
- Ranglisten lassen sich auf einen Modus begrenzen (`mode=`).

### Geändert
- Verlassene Matches gehen automatisch hoch, wenn der Matchbeginn erkannt wurde; sie zählen für K/D und Kills, nicht für die Siegquote.
- Windows-Client: Match-Liste zeigt „Verlassen“.

## [0.1.2] - 2026-10-24

### Behoben
- Windows-Client: Rundenergebnisse (Kollateral, Front, Kapern) wurden als ganzes Match gezählt; ein Match zählt jetzt erst, wenn Einsatzbericht oder Lobby folgen.
- Windows-Client: Endergebnisse mit schwer lesbarem Banner (z. B. Kapern-Sieg) wurden verpasst.
- Windows-Client: Tab-Scoreboard konnte die Werte des gerade ausgewählten Mitspielers übernehmen.
- Windows-Client: Arbeitsspeicher wuchs während der Erkennung auf mehrere GB (4K-Bilder wurden nicht rechtzeitig freigegeben); jetzt ein wiederverwendeter Bildpuffer und eine feste Speichergrenze.
- Windows-Client: Erkennung bricht bei Fehlern nicht mehr still ab, sondern startet die Aufnahme neu.
- Windows-Client: Zeitraum-Auswahl und Tabellenüberschriften waren weiß auf weiß.
- Download-Knopf lieferte die Startseite statt des Installers: Installer liegt jetzt in `deploy/downloads` (bleibt bei Updates), `install.sh`/`update.sh` holen ihn vom neuesten Release, Knopf erscheint erst, wenn er da ist.
- `SHA256SUMS` hat Linux-Zeilenenden und lässt sich mit `sha256sum -c` prüfen.

### Hinzugefügt
- Windows-Client: Der Einsatzbericht wird gelesen und bestätigt das Matchende. Karte, Endstand, XP, Level, Eliminierungen und Modus-Werte werden schon erkannt; gespeichert und angezeigt werden sie ab 0.2.0.
- Windows-Client: Frei für alle wird erkannt, mit Platzierung (Top 3 = Sieg wie im Spiel).
- Windows-Client: Verlassene Matches werden erkannt (Werte aus dem letzten Scoreboard, kein Sieg und keine Niederlage); bis 0.2.0 bleiben sie auf dem PC zur Prüfung.
- Windows-Client: Lobby und Hauptmenü werden erkannt; neue Modi Kapern, Kollateral, Kill Block, Waffenspiel.
- Windows-Client: Protokolldatei `%LOCALAPPDATA%\CodStats\client.log` (Zustände, Matches, Fehler; keine Bilder, keine fremden Namen).

## [0.1.1] - 2026-10-24

### Geändert
- Windows-Client: Serveradresse ist standardmäßig https://codstats.tabelander.co.at (änderbar beim Einrichten und in den Einstellungen).

### Behoben
- Release-Notizen auf GitHub zeigen Umlaute richtig.
- Hinter eigenem Reverse Proxy wird kein HTTPS-Port mehr geöffnet; `install.sh` bricht bei einem schon belegten Port mit klarer Meldung ab.

## [0.1.0] - 2026-10-24

### Hinzugefügt
- Erkennung MW4 Multiplayer (deutsche Oberfläche, 16:9): Ergebnis-Scoreboard (Sieg/Niederlage, Modus, Abschüsse, Assists, Tode, Punkte, eigener Name), Tab-Scoreboard als Rückfallebene, laufendes Match; geprüft an echten Aufnahmen aus 4 Modi, jeweils in 4K, 1440p und 1080p.
- CoD-Prozess wird über das Muster `cod<NN>-cod` bzw. `cod` erkannt (am echten PC geprüft), Clan-Tags werden aus Namen entfernt.
- Repo-Grundgerüst mit Architektur- und Sicherheitsdoku.
- API: signierte Geräteidentität ohne Konto (ECDSA P-256, Zeitstempel, einmalige Nonce), Registrierung, Umzug per Wiederherstellungsschlüssel.
- API: idempotenter Match-Upload mit Plausibilitätsprüfung und Duplikaterkennung, Korrekturen, Export, Löschen aller Daten.
- API: Statistik getrennt nach MW4, Warzone und Übersicht mit Zeitraumfiltern; K/D aus Gesamtsummen.
- API: öffentliche Spielersuche, Profile, Matches, Rankings mit Mindestanzahl, Plattformzahlen; Rate Limiting.
- Webseite: Startseite, Suche, Spielerprofil mit Diagrammen (Uhrzeit statt Datum, wenn alle Matches am selben Tag liegen), Rankings.
- Windows-Client: Zustimmung beim ersten Start, Tray, Autostart, Geräteschlüssel (DPAPI), SQLite, automatische Synchronisierung mit Wiederholung, eigenes Dashboard mit Filtern und Diagrammen, Match nachtragen und korrigieren, Wiederherstellung, Export, Löschen.
- Windows-Client: erkennt, ob Call of Duty läuft (Prozess).
- Erkennung: Fensteraufnahme (Windows Graphics Capture) und Windows-OCR nur während CoD läuft, datengetriebene Erkennungsprofile, Zustandsautomat mit Mehrfachlesung, Tab-Scoreboard als Rückfallebene, automatische Namensbestätigung nach 3 Matches. Spielprofile für MW4/Warzone fehlen noch (brauchen echte Screenshots).
- Windows-Client: im Tray rund 45 MB Arbeitsspeicher statt 260 MB (EXE nicht mehr selbst komprimiert, Speicher wird beim Wechsel ins Tray freigegeben), Spielwächter fragt nur noch die CoD-Prozesse ab.
- Windows-Installer (53 MB, ohne Adminrechte, Startmenü, Deinstallation entfernt Autostart und fragt nach den lokalen Daten), portable ZIP, Prüfsummen; GitHub baut bei Tag `v*` ein Entwurfs-Release.
- Docker Compose mit Caddy (HTTPS), Betriebsanleitung mit Backup und Restore.
- Betrieb hinter eigenem Reverse Proxy: `install.sh --behind-proxy`, nur HTTP, echte Besucher-IP nur von vertrauten Proxys (getrennte Ratenlimits je Besucher).
- Server-Skripte: `install.sh` (einmalig, Domain + Zufallspasswort), `update.sh` (Backup, Pull, Bauen, Neustart, Rollback), `backup.sh`, `restore.sh`, `start.sh`, `stop.sh`.

[Unreleased]: https://github.com/Tabsi1998/cod-stats-tracker/compare/v0.1.2...HEAD
[0.1.2]: https://github.com/Tabsi1998/cod-stats-tracker/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/Tabsi1998/cod-stats-tracker/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/Tabsi1998/cod-stats-tracker/releases/tag/v0.1.0
