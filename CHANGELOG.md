# Changelog

Alle wichtigen Änderungen an diesem Projekt. Format nach [Keep a Changelog](https://keepachangelog.com/de/1.1.0/), Versionen nach [SemVer](https://semver.org/lang/de/).

## [Unreleased]

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

[Unreleased]: https://github.com/Tabsi1998/cod-stats-tracker/compare/v0.1.1...HEAD
[0.1.1]: https://github.com/Tabsi1998/cod-stats-tracker/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/Tabsi1998/cod-stats-tracker/releases/tag/v0.1.0
