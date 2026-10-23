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
- Windows-Client: erkennt, ob Call of Duty läuft (Prozess). Die Bilderkennung fehlt noch.
- Docker Compose mit Caddy (HTTPS), Betriebsanleitung mit Backup und Restore.
