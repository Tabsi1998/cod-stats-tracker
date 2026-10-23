# Changelog

Alle wichtigen Änderungen an diesem Projekt. Format nach [Keep a Changelog](https://keepachangelog.com/de/1.1.0/), Versionen nach [SemVer](https://semver.org/lang/de/).

## [Unreleased]

### Hinzugefügt
- Repo-Grundgerüst mit Architektur- und Sicherheitsdoku.
- API: signierte Geräteidentität ohne Konto (ECDSA P-256, Zeitstempel, einmalige Nonce), Registrierung, Umzug per Wiederherstellungsschlüssel.
- API: idempotenter Match-Upload mit Plausibilitätsprüfung und Duplikaterkennung, Korrekturen, Export, Löschen aller Daten.
- API: Statistik getrennt nach MW4, Warzone und Übersicht mit Zeitraumfiltern; K/D aus Gesamtsummen.
- API: öffentliche Spielersuche, Profile, Matches, Rankings mit Mindestanzahl, Plattformzahlen; Rate Limiting.
- Webseite: Startseite, Suche, Spielerprofil mit Diagrammen, Rankings.
- Windows-Client: Zustimmung beim ersten Start, Tray, Autostart, Geräteschlüssel (DPAPI), SQLite, automatische Synchronisierung mit Wiederholung, eigenes Dashboard mit Filtern und Diagrammen, Match nachtragen und korrigieren, Wiederherstellung, Export, Löschen.
- Windows-Client: erkennt, ob Call of Duty läuft (Prozess).
- Erkennung: Fensteraufnahme (Windows Graphics Capture) und Windows-OCR nur während CoD läuft, datengetriebene Erkennungsprofile, Zustandsautomat mit Mehrfachlesung, Tab-Scoreboard als Rückfallebene, automatische Namensbestätigung nach 3 Matches. Spielprofile für MW4/Warzone fehlen noch (brauchen echte Screenshots).
- Windows-Client: im Tray rund 45 MB Arbeitsspeicher statt 260 MB (EXE nicht mehr selbst komprimiert, Speicher wird beim Wechsel ins Tray freigegeben), Spielwächter fragt nur noch die CoD-Prozesse ab.
- Windows-Installer (53 MB, ohne Adminrechte, Startmenü, Deinstallation entfernt Autostart und fragt nach den lokalen Daten), portable ZIP, Prüfsummen; GitHub baut bei Tag `v*` ein Entwurfs-Release.
- Docker Compose mit Caddy (HTTPS), Betriebsanleitung mit Backup und Restore.
- Server-Skripte: `install.sh` (einmalig, Domain + Zufallspasswort), `update.sh` (Backup, Pull, Bauen, Neustart, Rollback), `backup.sh`, `restore.sh`, `start.sh`, `stop.sh`.
