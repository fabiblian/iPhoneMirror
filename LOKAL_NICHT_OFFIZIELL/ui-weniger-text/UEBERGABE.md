# Übergabe: App entrümpeln – weniger Text, „ⓘ“ statt Absätzen

**Stand: 22.10.2026, Branch `ui/less-text` (GitHub: fabiblian/iPhoneMirror), Basis `main` @ `fa6d3e8`.**
Persönliche Arbeitsnotiz in Fabians Fork, nicht Teil der offiziellen Doku. Vor dem Weitermachen den aktuellen Stand neu prüfen (`git log`, `git status`).

## Worum es geht

Fabian wollte die App „cleaner“: Statt langer grauer Erklärabsätze unter jeder Einstellung sollen kurze Texte hinter einem kleinen ⓘ stehen. Vor der Umsetzung hat er per Rückfragen festgelegt:

| Thema | Entscheidung |
|---|---|
| ⓘ-Verhalten | Hover zeigt Tooltip; Klick / Enter / Leertaste heftet ihn als Popover an; Esc oder Klick daneben schliesst |
| Look | Fluent-Symbol `Info16`, gedämpft, Hover/Fokus in Akzentfarbe |
| Platzierung | direkt hinter dem Label bzw. Abschnittstitel |
| Texte | auf 1–2 kurze Sätze kürzen und ins ⓘ; was nur Sichtbares wiederholt, ganz streichen |
| Statuszeilen | nur einmal, als graue Zeile im Abschnittskopf; technische Details ins ⓘ |
| Warnungen / Fehler / Anleitungen | bleiben sichtbar, aber gekürzt |
| Hauptfenster | Untertitel, Build-Zeile, Footer-Slogan, Panel-Intro, Leerzustands-Hinweise, Unterzeilen der Live-Daten weg; „Keep the Apple device unlocked“ bleibt |
| Untertitel in Fenstern | nur behalten, wenn sie Info tragen (Gerätename) |
| USB-Modus-Info | Popover statt eigenem Fenster |
| Erststart-Assistent | Zeile unter Seitentitel kürzen, Kartentexte ins ⓘ |
| Geräteprofil-Popup | auf 1–2 Sätze kürzen |
| Sprachen | **nur Englisch gekürzt**; Chinesisch (zh-CN/HK/TW) behält die alten Texte an der neuen Stelle |
| Extras | Duplikate + toter Code weg, lange Checkbox-Labels kürzen, Abschnitte merken auf/zu, Developer Tools mit |
| Nicht dabei | Treiber-Tool (`src/DriverInstaller`) |
| Abgabe | eigener Branch, pushen, Merge in `main` erst nach Fabians OK |

## Stand: umgesetzt und gepusht

| Commit | Inhalt |
|---|---|
| `a1641d8` | Vorschau-Look-Kacheln nehmen Farben aus dem Theme (behebt einen Logik-Test, der auf `main` schon scheiterte) |
| `a2abbfb` | Neues Steuerelement `InfoTip` (ⓘ) + Schlüssel `InfoTipName` in allen 4 Sprachen |
| `7b65935` | Einstellungs-Panel (4 Tabs) + Hauptfenster aufgeräumt, `InfoRow`, Abschnitte merken auf/zu |
| `122a86d` | USB-Modi: Popover statt Info-Fenster (Fenster gelöscht), Runtime-Test `--info-tip` |
| `465386f` | Vorschau-Einstellungen, Geräte-Binder, Tastatur-Mapping + Assistent, Ausgabe, About, Tastenkürzel |
| `d987455` | Kleine Fenster: Fensterstil, Bild, Erweitert, Tray, Update, Download, AirPlay-Auswahl, Bluetooth-Zuordnung, Passcode, Projektion |
| `b3fa2a7` | Hinweis- und Fehlerfenster, Fehlermeldungen, Bluetooth-Kopplung, Geräteprofil-Popup gekürzt |
| `0c59d92` | Erststart-Assistent |
| `9e8f292` | Developer Tools + 75 ungenutzte Sprach-Schlüssel gelöscht (1717 → 1611) |
| `cd8b79c` | Statuskarte: Beschreibung nur einmal (eingeklappt einzeilig, aufgeklappt voll in den Details) |

## Bausteine (für weitere Änderungen)

- **`src/App/Controls/InfoTip.cs`** – Button mit `Text`; Tooltip beim Hover, Popover per Klick/Enter/Leertaste, Esc schliesst, Screenreader-Name `InfoTipName` („More info“), `HelpText` = Text.
- **`src/App/Controls/InfoTip.xaml`** – Aussehen (Fluent `Info16`, Popover im Tooltip-Look), in `src/App/App.xaml` eingebunden.
- **`src/App/Controls/InfoRow.cs`** – Panel: Label (1. Kind) bekommt die Breite minus ⓘ und darf umbrechen, das ⓘ (2. Kind) folgt direkt dahinter. Immer für „Label + ⓘ“ verwenden, nicht ein horizontales `StackPanel` (dort bricht lange Schrift nicht um und das ⓘ verschwindet).
- Muster in XAML:
  ```xml
  <controls:InfoRow>
      <CheckBox Content="{DynamicResource Xyz}" .../>
      <controls:InfoTip Text="{DynamicResource XyzHelp}"/>
  </controls:InfoRow>
  ```
- Dynamische Texte: `Text="{Binding …}"`, z. B. `BluetoothMouseDirectionInfo`, `WirelessResolutionInfo` (MainViewModel), `UsbProjectionModeOption.InfoText`.
- Abschnitte merken auf/zu: `UpdateSettings.SettingsSectionsExpanded` (auch in `ForkSettings`), Logik in `MainWindow.SettingsTabs.cs`; jeder Settings-Expander hat einen `x:Name` `SettingsSection…`.

## Regeln für Texte

- Jeder Schlüssel muss in **allen 4** `src/App/Localization/Strings.*.xaml` existieren (`scripts/verify_localization.ps1` prüft gleiche Schlüsselmengen). Gelöscht wird immer in allen vieren.
- Manche Schlüssel werden im Code zusammengesetzt (`"SetupHint" + step`, `"ControlStage" + …`, `"WizardHint" + …`). Vor dem Löschen prüfen; `tools/unused_keys.py` berücksichtigt das.
- `CaptureNoPingRecovery` und `CaptureUsbConfigurationRecovery` nicht löschen (Logik-Tests lesen sie), ebenso die in `LocalizationAuditTests.cs` genannten Schlüssel.
- Einige Einträge nutzen das Präfix `sys:` statt `system:`; die Hilfsskripte können beides.

## Offen / nicht geprüft

1. **Am iPhone ansehen** (in Tests nicht sichtbar, weil nur mit Gerät): Bildqualität, Ton, USB-Modus-Liste mit Popover, Bluetooth-Maus-Abschnitt. Hover, Klick-Popover, Tab/Enter/Esc am ⓘ, Auf/Zu der Abschnitte nach Neustart.
2. **Merge in `main`** erst nach Fabians OK.
3. **Build-Blocker auf Fabians Arbeits-PC (nicht durch diesen Branch):** Seit 22.10.2026 13:54 steht in `%APPDATA%\NuGet\NuGet.Config` `globalPackagesFolder = D:\Caches\NuGet\packages` **ohne** abschliessenden Backslash. Dadurch löst `$(NuGetPackageRoot)wpf-ui\4.3.0\…` in den csproj nicht auf, die WPF-UI-Lizenzdateien fehlen und `build.ps1` bricht am Ende mit „Driver installer output contains an unexpected licensing payload.“ ab (die App selbst baut). Der Launcher-Build von `main` um 13:45 lief noch durch, betroffen ist also auch `main`. Lösungen (noch nicht umgesetzt, Fabian entscheidet):
   - im Projekt: in `src/App/iPhoneMirror.App.csproj` und `src/DriverInstaller/iPhoneMirror.DriverInstaller.csproj` `$(NuGetPackageRoot)` durch `$([MSBuild]::EnsureTrailingSlash('$(NuGetPackageRoot)'))` ersetzen, oder
   - in der NuGet.Config einen `\` an den Pfad hängen.
   Auf einem anderen Gerät tritt das nur auf, wenn dort dieselbe NuGet.Config gilt.
4. Mögliche Folgearbeiten (nicht beauftragt): chinesische Texte ebenfalls kürzen; Treiber-Tool aufräumen.

## Tests – Ergebnis und bekannte Altlasten

Grün auf dem Branch: Build, `verify_localization.ps1` (alle 4 Sprachen 1611 Schlüssel), UI-Audit über alle 4 Sprachen × hell/dunkel (0 Fehler, 0 Binding-Warnungen), `--info-tip`, `--theme-content`, `--first-run-setup` (368 Layouts), `--interaction-regression`, `--localization-audit`, `--taiwan-localization`, `--adaptive-toolbar`, `--button-borders`, `--tray-mode`, `--component-download-ui`, `--control-states`, `--reverse-control-countdown`, `--workspace-regression`, `--shortcut-search`, `--audio-persistence`, `--volume-controls`.

Scheitern **gleich auch auf `main`** (Altlasten, nicht von diesem Branch):
- Logik-Tests: „entering full screen cancels pending workspace panel animations“ und „independent previews start non-topmost …“ (der Runner bricht beim ersten Fehler ab).
- `--language-display-audit`: Parameter-Anzahl in `TestBridgeOutputDisplay`.
- `--preview-shell-ui-audit`: „zh-TW-Dark-media-shell: Boss key must close the settings panel too.“
- `--keyboard-focus`: „Foreground Ctrl+A was dropped.“
- `--control-binding`: „Missing prerequisite prompt.“
- `scripts/audit_localization.py`: 2 Fehler „non-Taiwan term 快捷鍵“ (ShortcutFilterPlaceholder/Empty).
- UI-Audit bei 200 %: „ancestor clips ScrollViewer SettingsScrollViewer“.
- `--ui-regression` endet **zufällig** nach „passed“ mit Absturzcode 0xC0000409/0xC0000005 beim Beenden des Testprozesses – auf `main` ebenso beobachtet (nativer Fehler beim Herunterfahren, nicht weiter untersucht).

## Weitermachen auf einem anderen Gerät

```
git clone https://github.com/fabiblian/iPhoneMirror.git
cd iPhoneMirror
git checkout ui/less-text
```

Bereits geklont: `git fetch origin`, `git checkout ui/less-text`, `git pull`.

- Voller Build: `./build.ps1 -Configuration Release -OmitUxPlayRuntime -SkipTests` (siehe Punkt 3 oben, falls er am Ende scheitert).
- Schneller Compile-Check der App ohne Native-Build: `dotnet build src/App/iPhoneMirror.App.csproj -c Debug "-p:UsbBridgeRoot=<Ordner mit iUsbBridge.exe, _internal, iUsbBridge.runtime.json>"` – z. B. der `tools`-Ordner einer installierten Version.
- Runtime-Tests brauchen `src/App/native` (aus einem vollen Build): `dotnet src/App.Runtime.Tests/bin/Release/net10.0-windows10.0.19041.0/win-x64/IPhoneMirror.App.Runtime.Tests.dll --ui-audit <out> [surface,…] [--culture en-US]`. `IPHONE_MIRROR_UI_AUDIT_QUICK=1` = nur Englisch/hell. Für `workspace-settings` rendert das Audit jetzt alle 4 Tabs als PNG.
- Logik-Tests: `dotnet src/App.Logic.Tests/bin/Release/net10.0-windows10.0.19041.0/IPhoneMirror.App.Logic.Tests.dll` (nicht `dotnet run`, sonst scheitert ein Updater-Test an `DOTNET_ROOT_X64`).
- Testen über den Launcher: `Start-iPhoneMirror.cmd` → Branch `ui/less-text` bauen/starten.

## Hilfsskripte (`tools/` in diesem Ordner)

| Skript | Zweck |
|---|---|
| `strings.py <Localization-Ordner> <ops.json>` | Sprach-Einträge in allen 4 Dateien setzen / hinzufügen / löschen / zusammenführen (`set`, `add`, `delete`, `merge`; `"raw": true` für Werte mit `&#xA;`) |
| `rep.py <Datei> <spec.txt>` | exakte, eindeutige Textersetzungen (Blöcke `alt` / `-----` / `neu`, getrennt durch `=====`) |
| `unused_keys.py <Repo-Wurzel>` | Sprach-Schlüssel finden, die keine Quelldatei mehr nennt (zusammengesetzte Schlüssel werden ausgenommen) |
| `run_runtime.py <Runtime-Test-DLL> <Ausgabeordner> [--switch …]` | mehrere Runtime-Tests nacheinander, mit Exit-Code und letzter Zeile (mit `python -X utf8` starten) |
