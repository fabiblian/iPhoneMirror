# Persönliche Übergabe: iPhoneMirror kabellos testen

**Stand: 14.10.2026.** Diese Datei ist eine persönliche Testnotiz in Fabians Fork. Sie ist keine Dokumentation des Upstream-Projekts und keine allgemeine Aussage über alle Geräte oder Netzwerke. Vor weiteren Schritten den aktuellen Zustand erneut prüfen. Beim bisherigen Troubleshooting wurde kein Quellcode geändert.

## Wichtigster Befund

**„Wireless control: Control connected“ bedeutet nur, dass Eingaben kabellos übertragen werden.** Es bedeutet nicht, dass das Bild über AirPlay läuft. Beim letzten Test zeigte die App einen funktionierenden USB-Bildstream (`kind=wired`, `state=Streaming`) und eine funktionierende kabellose Steuerung mit bestätigten Touch- und Tastatureingaben. Beim Abziehen des Kabels verschwand die USB-Quelle; es war keine AirPlay-Bildquelle vorhanden (`wireless=0`).

Für ein Bild ohne Kabel muss auf dem iPhone unter **Control Center → Screen Mirroring** der AirPlay-Empfänger der App gewählt werden. Erst wenn in der linken Quellenliste eine **Wireless-/AirPlay-Quelle** erscheint und darüber ein Bild läuft, kann das Kabel entfernt werden. Die Schaltfläche **Wireless control** konvertiert den USB-Bildstream nicht in AirPlay.

## Was zuletzt beobachtet wurde

- Die lokale App startete den AirPlay-Empfänger und meldete ihn als bereit. Die Auswahl unter **Screen Mirroring** scheiterte zuvor jedoch mit **„Unable to Connect“**; eine erfolgreiche AirPlay-Bildsession wurde nicht nachgewiesen.
- Auf dem getesteten Windows-PC blockierte das aktive Firewallprofil eingehende Verbindungen. Es gab keine passende AirPlay-Regel für die portable Projekt-App; lokale Firewallregeln wurden laut Profilanzeige nicht mit der verwalteten Richtlinie zusammengeführt. Das ist ein konkreter möglicher Verbindungsblocker. Auch eine Trennung von WLAN-Geräten durch das Netzwerk ist noch nicht ausgeschlossen.
- Die früheren temporären Firewall-Testregeln wurden entfernt. Keine dauerhafte Freigabe und keine Codeänderung waren Teil der Diagnose.

## Zu Hause Schritt für Schritt

1. Den tatsächlichen Checkout und `git status --short --branch` prüfen. Ein GitHub-Checkout enthält **keine** fertigen `outputs/`-Dateien, weil der Ordner ignoriert wird. Für den lokalen Build `./build.ps1 -Configuration Release` gemäß [Entwicklungsanleitung](../docs/DEVELOPMENT.md) ausführen; das Programm liegt danach unter `outputs/iPhoneMirror/iPhoneMirror.exe`. Alternativ eine passende veröffentlichte Version bewusst getrennt vom lokalen Build testen.
2. PC und iPhone mit demselben privaten WLAN verbinden. Für einen eindeutigen Test zusätzliche LAN-, Hotspot- und VPN-Verbindungen nach Möglichkeit trennen. Auf einem verwalteten Firmen-PC können Firewallrichtlinien auch zu Hause gelten.
3. Die lokale App starten. Unter **Wireless AirPlay** prüfen, ob der Empfänger bereit ist, und seinen angezeigten Namen merken.
4. Auf dem iPhone **Control Center → Screen Mirroring** öffnen und genau diesen Empfänger auswählen. Das USB-Kabel darf bei diesem Schritt noch angeschlossen sein.
5. Erst wenn links eine **Wireless-/AirPlay-Quelle** erscheint und deren Bild läuft, das Kabel abziehen. **Wireless control** bei Bedarf zusätzlich einschalten.
6. Falls **„Unable to Connect“** erscheint: aktive Netzwerkadresse, Firewallprofil, **wirksame** Programmeingangsregel und mögliche WLAN-Client-Isolation prüfen. Bei verwalteter Firewall eine eng auf die App und das lokale Netz begrenzte Freigabe durch die zuständige IT veranlassen. AirPlay handelt neben festen Steuerports dynamische Medienports aus.

## Hinweise für die nächste KI

Die aktuellen Logs unter `%LOCALAPPDATA%\iPhoneMirror\Logs\` prüfen: `application.log`, `capture.log`, `reverse-control.log`. In `application.log` zwischen `source_selected kind=wired` und einer echten kabellosen Quelle unterscheiden; `device_refresh_complete` zeigt `wired` und `wireless`. In `capture.log` nach AirPlay-Verbindungsversuchen zur angegebenen Uhrzeit suchen. Wenn kein AirPlay-Aufruf ankommt, zuerst Netzwerk und wirksame Firewallrichtlinie prüfen. Wenn ein Aufruf ankommt, den konkreten Handshake- oder Medienfehler untersuchen. Die [Projektanleitung zu Wireless AirPlay](../README.en.md#wireless-airplay) beschreibt die erwartete Quellenanzeige und die vom Installer vorgesehene Firewallregel. Keine globale Firewall-Abschaltung als Test verwenden.
