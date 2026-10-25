# Selbst-Update des Windows-Clients

Der Client fragt eine Minute nach dem Start und danach alle 6 Stunden auf dem eigenen Server nach einer neueren Version. Gibt es eine, zeigt er sie im Dashboard („Jetzt aktualisieren“) und im Tray-Menü. **Installiert wird nur auf Klick des Spielers** und nie, solange Call of Duty läuft. Abschaltbar in den Einstellungen („Nach neuen Versionen suchen“).

## Ablauf

```
Release-Workflow (Tag v*)          Server (update.sh)                   Client
  Installer bauen                    Installer + CodStats-update.json     GET /downloads/CodStats-update.json(.sig)
  CodStats-update.json schreiben     + .sig nach deploy/downloads         Signatur prüfen (öffentlicher Schlüssel im Client)
  mit UPDATE_SIGNING_KEY signieren   (nur wenn SHA-256 zum Installer      neuer als die eigene Version? → anbieten
  → Release-Assets                    passt)                              Klick: Installer laden, SHA-256 prüfen,
                                                                          still installieren, neu starten
```

`CodStats-update.json`:

```json
{ "version": "0.3.0", "installer": "CodStats-Setup-win-x64.exe", "sha256": "<hex>" }
```

`CodStats-update.json.sig`: Base64 der DER-Signatur (ECDSA P-256, SHA-256) über genau die Bytes der JSON-Datei, wie `openssl dgst -sha256 -sign` sie schreibt.

Der Client nimmt nur an: gültige Signatur, Version `x.y.z` höher als die eigene, Installername ohne Ordner, SHA-256 des heruntergeladenen Installers wie in der signierten Datei. Alles andere wird verworfen (steht in `client.log`). Der Installer läuft mit `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPP=1`; `/RESTARTAPP=1` startet den Client danach wieder. Einstellungen, Matches und Geräteschlüssel bleiben, weil über die alte Version installiert wird.

**Warum signiert:** Ein Selbst-Update kann Programme auf jeden Spieler-PC bringen. Mit HTTPS und SHA-256 allein könnte das jeder, der den Server übernimmt. Der private Schlüssel liegt nur als GitHub-Secret vor, nicht auf dem Server.

## Schlüssel einmalig einrichten

Auf dem eigenen PC (Git Bash hat `openssl`), **nicht** im Repo-Ordner:

```bash
openssl ecparam -name prime256v1 -genkey -noout -out codstats-update-key.pem
openssl ec -in codstats-update-key.pem -pubout -outform DER | openssl base64 -A; echo
gh secret set UPDATE_SIGNING_KEY --repo Tabsi1998/cod-stats-tracker < codstats-update-key.pem
```

1. Die ausgegebene Base64-Zeile (öffentlicher Schlüssel) in `apps/desktop/CodStats.Desktop/Updates/UpdateKey.cs` als `PublicKey` eintragen. Der öffentliche Schlüssel ist kein Geheimnis.
2. `codstats-update-key.pem` offline sicher aufbewahren (Passwortmanager) und vom PC löschen. Geht er verloren, braucht es einen neuen Schlüssel und einen Client, den alle Spieler noch einmal von Hand installieren.
3. Ab dem nächsten Release signiert der Workflow die Update-Datei. Passt das Secret nicht zu `UpdateKey.cs`, bricht der Release ab; fehlt es, wird der Release ohne Selbst-Update gebaut (Warnung im Lauf).

Erst Clients mit eingetragenem Schlüssel suchen nach Updates. Die erste Version mit Schlüssel muss also noch jeder selbst installieren.
