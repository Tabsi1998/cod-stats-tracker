# Changelog

Alle wichtigen Änderungen an diesem Projekt. Format nach [Keep a Changelog](https://keepachangelog.com/de/1.1.0/), Versionen nach [SemVer](https://semver.org/lang/de/).

## [Unreleased]

### Behoben
- Endstand im Einsatzbericht: Die beiden Zahlen werden zusätzlich je in ihrer Teamfarbe gelesen (Cyan eigenes Team, Orange Gegner, mit Farbfilter). Fehlt die gemeinsame Zeile, setzt sich der Endstand aus beiden Farben zusammen; widersprechen sich die Lesungen, bleibt er leer.
- Karten werden nur noch als Name aus der offiziellen MW4-Kartenliste gespeichert (z. B. „REACTOR U92“ statt „REACTOR IJ92“); ein unbekannter Name bleibt leer und steht im Client-Log.
- Tode fehlten oft (z. B. Herrschaft): Die Leiste unter dem Ergebnis wird jetzt in drei Durchgängen gelesen, und die ELIM/T-QUOTE prüft die Tode gegen (45 Eliminierungen bei 1.67 sind nur mit 27 Toden möglich). Fehlen die Tode, aber passt genau ein Wert zur Quote, wird er übernommen; passt der gelesene nicht, kommt das Match zur Prüfung.
- XP wurden manchmal falsch gelesen (931 statt 9311): mehr Durchgänge ohne Weißfilter, und XP unter den Punkten gelten als Lesefehler und werden verworfen.
- Level nach einem Level-Up (grüne Schrift) wurde nicht gelesen.
- Ein Endstand, der dem Ergebnis widerspricht (Sieg mit 0:174), wird verworfen statt gespeichert; ein Lese-Durchgang, der solche Werte lieferte, ist entfernt.
- Kacheln im Einsatzbericht (einzelne Ziffern wie „4“ Assists) werden mit zwei weiteren Durchgängen gelesen.

## [0.2.3] - 2026-10-25

### Hinzugefügt
- Lobby-Vergleich: Der Client liest auf dem End-Scoreboard, wie viele Spieler in der Lobby waren und auf welchem Platz du nach Punkten gelandet bist. Übertragen werden nur diese zwei Zahlen, keine Namen und keine Werte anderer Spieler. Die Match-Seite zeigt „Platz 3 von 12“, das Profil den Anteil der Lobby hinter dir (über alle Matches summiert). Bei unsicherer Lesung bleibt die Lobby leer, das Match zählt trotzdem.
- Top-Waffe und Waffenklasse aus dem Einsatzbericht (TOPLEISTUNG), auf der Match-Seite angezeigt. Gespeichert wird nur ein Name aus der Waffenliste des Erkennungsprofils; eine unbekannte Waffe bleibt leer und steht im Client-Log.
- Modus-Werte aus der Endtabelle: Bestätigt (Abschuss bestätigt), Zielzeit (Stellung), Platzierungen und Entschärfungen (Kollateral).
- Profil-Filter: Modus, Karte, Waffe, Waffenklasse und Ergebnis (Siege, Niederlagen, Verlassen), dazu der Zeitraum „Heute“. Alle Filter stehen in der Adresse und lassen sich teilen; die Auswahl zeigt nur, was der Spieler gespielt hat.
- Vergleich mit dem Zeitraum davor: K/D, Siegquote und Kills pro Match zeigen bei „Heute“, „7 Tage“ und „30 Tage“ die Veränderung gegenüber gestern bzw. dem Zeitraum davor.
- Profil-Tab „Waffen“: Siegquote und K/D je Top-Waffe und Waffenklasse.
- Profil-Tab „Sessions“: Spielabende automatisch gruppiert, mit Ergebnisreihe, K/D, bestem Match, Level-Ups und XP.
- Profil-Tab „Rekorde“: Bestwerte aus einem einzelnen Match (Kills, K/D, Punkte, Eliminierungen, Assists, XP und jeder Modus-Wert), jeweils mit Link zum Match.
- Modi-Tab mit Modus-Werten je Modus (z. B. Ø Bestätigt pro Match in Abschuss bestätigt), Karten-Tab mit Karte × Modus.
- Übersicht: Form-Kurve (K/D der letzten 10 Matches) neben der Gesamt-K/D, Level-Verlauf, „Wann du spielst“ nach Wochentag und Tageszeit, Lobby-Platz-Verteilung.
- Ein Klick auf einen Modus, eine Karte oder eine Waffe in den Tabellen filtert das Profil darauf.
- Ranglisten: Filter nach Karte und Zeitraum (7 und 30 Tage); Modi und Karten kommen aus den erfassten Matches.

### Behoben
- Assists fehlten in den meisten Modi: Ohne Assist-Spalte rechnet der Client sie aus Eliminierungen − Abschüsse. Passen die drei Werte nicht zusammen, kommt das Match zur Prüfung.
- Tode kommen aus der eigenen Tabellenzeile, wenn die Tabelle sie zeigt; die Leiste darunter zeigt manchmal einen anderen Spieler.
- Frei für alle: Ein Platz, der dem Ergebnis (Sieg = Top 3) oder dem Endstand im Einsatzbericht widerspricht, wird verworfen statt gespeichert; ist der Platz unlesbar, aber der Endstand eindeutig vorn, gilt Platz 1.
- Windows-Client: Startete er, während das Spiel noch sein kleines Startfenster zeigte, erkannte er danach nichts mehr (das Spiel öffnet ein neues Hauptfenster). Die Aufnahme wechselt jetzt auf das neue Fenster und startet nach 10 Sekunden ohne Bild neu.

## [0.2.2] - 2026-10-24

### Geändert
- Ein bestätigter Spielername gehört genau einem Profil (exakte Schreibweise; „Tabsi98“ und „TAbsi98“ sind wie im Spiel zwei Spieler): Ein zweites Profil (anderer PC, Neuinstallation mit gelöschten Daten) bekommt den Namen nicht, der Client verweist auf den Wiederherstellungsschlüssel. Bereits doppelte Namen: das ältere Profil behält ihn.
- Windows-Client: Beim Deinstallieren ist „Daten behalten“ die Vorauswahl; der Client zeigt seine Version in der Titelleiste.
- Download des Installers wird nie zwischengespeichert (sonst konnte nach einem Update die alte Version kommen).
- Windows-Client: Die Serveradresse ist fest https://codstats.tabelander.co.at und lässt sich nicht mehr einstellen (Einrichtung und Einstellungen ohne Adressfeld).

## [0.2.1] - 2026-10-24

### Behoben
- Webseite: Nach jedem Seitenwechsel blieb die Seite dunkel und leer (neuere Chrome-Versionen); Neuladen war nötig.
- Webseite: Eine fehlerhafte Seite zeigt jetzt eine Meldung mit „Neu laden“ statt einer leeren Fläche; nach einem Update lädt die Seite fehlende Dateien selbst neu, und die Startseite wird nicht mehr zwischengespeichert.

### Geändert
- Sichtbarkeit pro Match: Öffentlich schalten zeigt alle bisherigen Matches; privat schalten lässt bereits gezeigte Matches stehen und zeigt nur neue nicht mehr. Hochgeladen wird immer.
- Windows-Client: Bei der Einrichtung ist „Zustimmen, öffentlich zeigen“ die hervorgehobene Standardwahl; „nur für mich“ bleibt eine Taste daneben und ist jederzeit in den Einstellungen änderbar.

## [0.2.0] - 2026-10-24

### Hinzugefügt
- Server und Client speichern alle erkannten Werte: Status (beendet/verlassen), Eliminierungen, Endstand, XP, Level und Modus-Werte; ältere Clients laden weiter ohne sie hoch.
- Auswertung: Verlassen-Quote, längste Siegesserie, bestes Match, Ø Punkte, Ø XP, XP gesamt, Eliminierungen gesamt.
- Auswertung nach Modus und nach Karte (Matches, Siegquote, K/D, Ø Kills, Ø Punkte).
- Öffentlicher Endpunkt für ein einzelnes Match (`/api/v1/public/players/{id}/matches/{matchId}`) für die Match-Detailseite.
- Ranglisten lassen sich auf einen Modus begrenzen (`mode=`).
- Webseite: Match-Detailseite im Stil des Einsatzberichts (Ergebnis, Modus, Karte, Endstand, alle Werte, Modus-Werte, XP, Level, Vergleich mit dem eigenen Schnitt im selben Modus).
- Webseite: Profil mit Bilanz (K/D, Siegquote, Kills, Matches), Form der letzten 20 Matches, längster Serie, bester Runde, Verlassen-Quote und den Tabs Übersicht, Matches, Modi, Karten.
- Webseite: Ranglisten lassen sich nach Modus filtern; Startseite mit Top K/D und meisten Kills.

### Geändert
- Verlassene Matches gehen automatisch hoch, wenn der Matchbeginn erkannt wurde; sie zählen für K/D und Kills, nicht für die Siegquote.
- Windows-Client: Match-Liste zeigt „Verlassen“.
- Webseite: neues Design „Taktisches HUD“ (eigene Schriften, Eckwinkel-Rahmen wie im Einsatzbericht, Cyan = Sieg, Orange = Niederlage, Gelb = du), Animationen beim ersten Anzeigen; mit „Bewegung reduzieren“ im System bleibt alles ruhig.
- Webseite: Profil- und Match-Seiten laden ihre Diagramme erst bei Bedarf (kleinerer erster Download).

### Behoben
- Windows-Client: Wechselte das Spiel nach dem Start des Clients seine Auflösung (z. B. von 2560x1440 auf 3840x2160), wurde nur noch ein Bildausschnitt aufgenommen und kein Match mehr erkannt; die Aufnahme folgt jetzt jeder Größenänderung.

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

[Unreleased]: https://github.com/Tabsi1998/cod-stats-tracker/compare/v0.2.3...HEAD
[0.2.3]: https://github.com/Tabsi1998/cod-stats-tracker/compare/v0.2.2...v0.2.3
[0.2.2]: https://github.com/Tabsi1998/cod-stats-tracker/compare/v0.2.1...v0.2.2
[0.2.1]: https://github.com/Tabsi1998/cod-stats-tracker/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/Tabsi1998/cod-stats-tracker/compare/v0.1.2...v0.2.0
[0.1.2]: https://github.com/Tabsi1998/cod-stats-tracker/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/Tabsi1998/cod-stats-tracker/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/Tabsi1998/cod-stats-tracker/releases/tag/v0.1.0
