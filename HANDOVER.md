# Übergabe: Etappen-Plan iPhoneMirror (Stand 2026-10-15)

## Redesign (Branch `redesign`, Stand 2026-10-19)

Mockup: `docs/redesign/mockup.html` (im Browser öffnen). Etappen: 0 Mockup · 1 Theme-System · 2 Seitenleiste mit Reitern · 3 Icons/Status-Pillen/Start-Stop · 4 klarere Texte.

| Etappe | Status |
|---|---|
| 0 – Mockup | ✅ von Fabian abgenommen |
| 1 – Theme-System | umgesetzt, kompiliert (Linux-Compile-Check), **Test unter Windows offen**. App settings: Theme-Liste mit Midnight/Nord/Sunset/Forest, Akzentfarbe (8 Farben + Hex), Stil Modern/Playful/Minimal. Vorschaurahmen nutzt standardmässig die Akzentfarbe (Preview settings → „Use the accent color“). |
| 2 – Seitenleiste mit Reitern | umgesetzt, kompiliert, **Test unter Windows offen**. Reiter Video & sound · Control · Wireless · App (gewählter Reiter wird gemerkt), aufklappbare Abschnitte mit Kurzinfo, AirPlay-Karte zusammengeführt, Shortcuts eigener Abschnitt, Bluetooth-Maus eingeklappt. Statistik unter der Vorschau einklappbar (Zustand wird gemerkt). |
| 3 – Status-Pillen, Aktionen | umgesetzt, kompiliert, **Test unter Windows offen**. Kopfzeile: Pillen Treiberstatus · Spiegelung · aktive Steuerung · AirPlay (Farbe = Status). „Exit app“ jetzt im ⋯-Menü (dazu About, Diagnostics) und weiter im Tray. Stop-Knopf in der Werkzeugleiste über der Vorschau, solange gespiegelt wird. Start/Stop-Knopf oben bleibt. |
| 4 – Texte | offen |

Testen: Theme × Stil durchschalten, eigene Hex-Farbe eingeben, Vorschaufenster öffnen (Rahmen folgt Akzent), App neu starten (alles bleibt gespeichert).

---

Branch **`feature/preview-frame`** (GitHub: fabiblian/iPhoneMirror). `main` = alte Version zum Vergleich.

## Auf dem anderen PC starten

```
git clone https://github.com/fabiblian/iPhoneMirror.git
cd iPhoneMirror
git checkout feature/preview-frame
```

Falls das Repo dort schon existiert: `git fetch origin` und `git checkout feature/preview-frame`, dann `git pull`.

Build: `./build.ps1 -Configuration Release -OmitUxPlayRuntime -SkipTests` (~5 Min.).
Testen über den Launcher (`tools/version-launcher`, Start mit `Start-iPhoneMirror.cmd`) → **Update** → `feature/preview-frame` starten.

## Stand

| Etappe | Status |
|---|---|
| 1 – Echte Uhrzeit + Preview-Shortcut | ✅ fertig, Commit `12ef0a5`, gepusht, von Fabian am iPhone getestet (funktioniert). |
| 2 – Schärferes Bild bei kleinem Fenster | umgesetzt (Mip-Zwei-Pass, LOD-Bias −0.5, ohne Nachschärfung), Build ok – **Test am iPhone offen**. Log zeigt `mode=mipmapped`. |
| 3 – Tippen mit Schweizer Layout | umgesetzt (`SwissKeyboardLayout.cs`, `MainWindow.KeyboardLayout.cs`), Logik-Tests ok, Build ok – **Test am iPhone offen**. Dabei prüfen: `~` (⌥N ist am Mac evtl. eine Tottaste) und ob `§`/`<` vertauscht sind. |
| 4 – Pfeiltasten wischen | umgesetzt (`ArrowKeySwipe.cs`, Dispatch in `MainWindow.xaml.cs`, Schalter in der Karte „Keyboard mapping“), Logik-Tests ok, Build ok – **Test am iPhone offen**. Nur ohne Modifier wird gewischt; Shift/Alt/Cmd+Pfeil bleiben normale Tasten. |
| 5 – Zwischenablage beide Richtungen, Text | umgesetzt: PC → iPhone über `WM_CLIPBOARDUPDATE` (`MainWindow.ClipboardSync.cs`) und den **vorhandenen** Bridge-Frame `write_clipboard` (kein neuer `set_clipboard` nötig, Bridge unverändert). Echo-Schutz: `ClipboardPushPolicy` (Hash + Sequenznummer). Logik-Tests ok, Build ok – **Test am iPhone offen**. |
| 6 – Bilder in der Zwischenablage | umgesetzt: Bridge liest `public.png/jpeg/tiff/heic` (Event `clipboard_image`, Capability `iphoneMirror.clipboard.image.v1`), neue Frames `write_clipboard_image`/`paste_image`; App schreibt Bilder als Bitmap+PNG nach Windows, schickt PC-Bilder (>2,8 MB → JPEG/verkleinert, Log `clipboard_image_reduced`), Ctrl+V fügt Bilder ein. Python- und Logik-Tests ok, Build ok – **Test am iPhone offen**. HEIC braucht unter Windows die HEIF-Erweiterung (sonst Log `clipboard_image_to_win_failed`). |

**Alle 6 Etappen umgesetzt.** Offen ist nur noch der Gesamttest am iPhone (Etappen 2–6).

Veraltete Logik-Tests wurden in Commit `75421d0` repariert. Auf dem alten PC lag zuletzt noch eine **uncommittete** Änderung an `src/App.Runtime.Tests/ReverseControlCountdownTests.cs` aus der Session „stabilize flaky reverse“. Sie ist **nicht** auf GitHub. Bis das geklärt ist, weiter mit `-SkipTests` bauen.

**Test Etappe 1:** Mit Kabel spiegeln → echte Uhrzeit statt 9:41. App neu starten → Modus bleibt B. Direkt nach App-Start Ctrl+End → Preview startet; erneut Ctrl+End → aus/ein.

## Entscheidungen

- Pfeiltasten **wischen immer**, **Ctrl+Pfeil** = normale Pfeiltaste.
- iPhone-Hardware-Layout **Deutsch (Schweiz)**.
- Zwischenablage **beide Richtungen automatisch**, mit Bildern.
- ~~USB-Modus B als Standard~~ → **zurück auf A Demo als Standard** (16.10.): In B leiten Video-Apps (TikTok) das Video per AirPlay aus, im Spiegelbild bleibt nur der Ton. A zeigt dafür 9:41. Wahl pro Gerät bleibt gemerkt; B ist weiter wählbar.
- Shortcuts: Show app = Ctrl+Home, Show preview = Ctrl+End.

**Etappen-Regel:** Jede Etappe ist für sich fertig. Am Ende jeder Etappe:
1. Build mit `-SkipTests`
2. Gezielter Commit (nur eigene Dateien)
3. `git push origin feature/preview-frame`

Nach jeder Etappe kann gestoppt werden.

---

## Etappe 2 – Schärferes Bild bei kleinem Fenster

Betrifft nur `src/Core/src/Renderer/D3D11PreviewRenderer.cpp`.

- **Ursache:** Beim Verkleinern wird nur ein bilinearer Punkt pro Bildpixel gelesen, ohne Mipmaps.
- **Lösung:** Ist das Bild kleiner als die Quelle, den Zwei-Pass-Pfad nutzen (~1857–1936):
  1. NV12 → RGB in eine Textur in Quellauflösung mit Mip-Kette (`MipLevels = 0`, `GENERATE_MIPS`), dann `GenerateMips`.
  2. Kopie mit einem neuen trilinearen Sampler und leicht negativem LOD-Bias.
- **Wiederverwenden:** `ensure_render_texture` (~1659–1690), Sampler-Setup (~806–812), `copyMain`.
- Wirkt die Schrift danach noch weich: leichte Nachschärfung im Kopier-Shader.
- Großes Fenster, Bildanpassungen und Rahmen bleiben unverändert.
- **Test:** Fenster klein ziehen, Text auf dem Home-Screen und in Mail lesen. Im Launcher mit `main` vergleichen.

## Etappe 3 – Tippen mit Schweizer Layout

Betrifft `src/App/Services/DeviceKeyMap.cs` und `MainWindow.xaml.cs` (`DispatchControlKeyboardInput`, ~1440–1533).

- Zuordnung über den **Scan-Code** (physische Taste → HID-Usage) statt über den Windows-Tastencode. Damit stimmen y/z, ä/ö/ü, Ziffern und Shift-Zeichen.
- Fehlende Tasten ergänzen: ISO `<>` (0x64), F1/F2, Win → Command.
- **AltGr-Zeichen** über eine Tabelle für Mac/iOS Deutsch (Schweiz):

  | Zeichen | Tasten am iPhone |
  |---|---|
  | @ | ⌥G |
  | € | ⌥E |
  | # | ⌥3 |
  | \| | ⌥7 |
  | [ ] | ⌥5 / ⌥6 |
  | { } | ⌥8 / ⌥9 |
  | \ | ⌥⇧7 |
  | ~ | ⌥N |

  Zeichen, die nicht in der Tabelle stehen, gehen über die vorhandene Text-Einfügen-Funktion.
- PC-Kürzel übersetzen: Ctrl+A/X/Z → Cmd, Ctrl+Y → Cmd+Shift+Z.
- **Test:** In Notizen `Zürich yz äöü @ € # { }` tippen. Neue Logik-Tests für die Tabellen schreiben.

## Etappe 4 – Pfeiltasten wischen

Betrifft `MainWindow.KeyboardRouting.cs`, `RouteKeyboardEvent` (~170–215), vor dem Direct-Pfad.

- Pfeiltaste ohne Ctrl → Swipe in der Bildmitte über den vorhandenen `KeyboardMappingExecutor` (`src/App/Services/KeyboardMappingExecutor.cs`), ca. 35 % Strecke, ca. 250 ms.
- Richtungen:
  - ↓ = Finger nach oben (nächstes Video), ↑ = Finger nach unten
  - → = Finger nach links (nächste Seite), ← = Finger nach rechts
- Ctrl+Pfeil sendet eine normale Pfeiltaste. Ein Tastendruck löst genau einen Swipe aus.
- Gilt nur bei Wired/Wireless Control. Bluetooth kennt keine Touch-Gesten.
- Einstellung „Pfeiltasten wischen“ (an/aus) in den Tastatur-Einstellungen, mit Texten in allen 4 `Strings.*.xaml`.
- **Test:** TikTok ↓/↑, Bücher →/←, Ctrl+← im Textfeld.

## Etappe 5 – Zwischenablage automatisch in beide Richtungen (Text)

- **PC → iPhone:**
  - `AddClipboardFormatListener` auf dem Hauptfenster-HWND (`WM_CLIPBOARDUPDATE`).
  - Bei jeder Änderung den Text per neuem Bridge-Frame `set_clipboard` setzen, **ohne** einzufügen.
  - Dateien: Bridge `tools/usb_touch_bridge.py`, App `DirectUsbInputBridge.cs`.
- **iPhone → PC** läuft schon (`_poll_device_pasteboard`, `ClipboardSyncState.cs`).
- Echo-Schutz gegen Hin-und-her: Inhalts-Hash plus die vorhandene Sequenznummer-Logik.
- Gilt nur bei Wired/Wireless Control. Bluetooth hat keine Zwischenablage, das erklärt der Hinweistext.
- **Test:** Am PC kopieren → am iPhone einsetzen. Am iPhone kopieren → am PC einsetzen. Kein Flackern.

## Etappe 6 – Bilder in der Zwischenablage

- **Bridge:**
  - Snapshot liest zusätzlich `public.png/jpeg/heic/tiff`, mit `get(data_policy=allResolved)` (pymobiledevice3 `pasteboard_service.py`).
  - Neues Event `clipboard_image` (Base64).
  - `set_clipboard` und Einfügen auch für Bilder, über `data_item(uti, bytes)`.
  - Änderungen beim Polling werden per Hash erkannt.
- **App:**
  - `ClipboardSyncState` und `MainViewModel` schreiben Bilder als PNG in die Windows-Zwischenablage.
  - Der Listener aus Etappe 5 schickt auch Bilder ans iPhone.
  - Ctrl+V fügt auch Bilder ein.
  - Bilder über 4 MB vor dem Senden verkleinern oder als JPEG komprimieren, mit Log-Eintrag.
- **Test:** Screenshot am PC kopieren → in Notizen einsetzen. Foto am iPhone kopieren → in Paint einfügen.

---

## Prompt für Claude auf dem neuen PC

> Repo iPhoneMirror, Branch `feature/preview-frame`. Lies `HANDOVER.md` im Repo-Root. Setze **nur Etappe N** um. Danach `./build.ps1 -Configuration Release -OmitUxPlayRuntime -SkipTests`, gezielt committen und `git push origin feature/preview-frame`. Antworte auf Deutsch, arbeite zielgerichtet (Kontingent sparen).
