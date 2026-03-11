# HyperTool

HyperTool ist ein WinUI-3 Toolset für Hyper-V-Host und Windows-Guest mit Fokus auf schnelle VM-/Netzwerkaktionen und USB/IP-Workflows.

## Aktueller Release-Stand

- Version: **v2.4.5**
- Resource-Monitor Host-Layout überarbeitet: Prozessor- und Arbeitsspeicher-KPI sind zentriert über ihren jeweiligen Trends ausgerichtet.
- Resource-Monitor VM-Ansicht priorisiert verbundene VMs und nutzt horizontales Scrolling für zusätzliche Karten.
- Resource-Monitor ist bei Snapshot-/Refresh-Aussetzern robuster (letzte gültige Werte bleiben sichtbar, kein Leerzustand mehr).
- Bezeichnungen im Monitor wurden vereinheitlicht (`Ressourcenmonitor`, `Prozessor`, `Arbeitsspeicher-Auslastung`, `Verlauf`).
- USB-Konfigurationsmigration zeigt in Host und Guest eine sichtbare Einmal-Info (nicht nur Feed-Notification).

## Projekte

- HyperTool (Host): Hyper-V Steuerung, Netzwerk, Snapshots, USB-Share und Shared-Folder-Katalog.
- HyperTool Guest: USB-Client sowie Shared-Folder-Mounting gegen Host-Freigaben.
- HyperTool Guestx86 (Legacy WPF): x86-Guest-App für ältere Windows-Systeme, inklusive USB, Shared Folder, Theme und Tray-Menü.
- Gemeinsame Basis in HyperTool.Core.

## Funktionen

### Host (HyperTool.exe)

- VM-Aktionen: Start, Stop, Hard Off, Restart, Konsole.
- VM löschen: per VM-Menü/Rechtsklick mit Bestätigungsdialog; entfernt die VM aus Hyper-V.
- Netzwerk: adaptergenaues Switch-Handling (auch Multi-NIC).
- Host-Network-Details: klare Status-Chips für `Gateway` (grün) und `Default Switch` (orange), dark/light lesbar.
- Snapshots: Baumdarstellung mit Restore/Delete/Create.
- USB: Refresh, Share, Unshare über usbipd.
- Tray Control Center: usbipd-Dienststatus (grün/rot), kompakter USB-Bereich und Installationsbutton bei fehlendem usbipd-win.
- Tray + Control Center mit Schnellaktionen.
- In-App Updatecheck und Installer-Update.
  
<img width="1381" height="939" alt="hypertool-host" src="https://github.com/user-attachments/assets/5417ec17-1716-41f0-8679-61214dd6fcf7" />

### Guest (HyperTool.Guest.exe)

- USB-Geräte vom Host laden, Connect/Disconnect.
- USB-Host-Sektion mit sichtbaren Transportmodus-Chips (Hyper-V Socket / IP-Mode) und modeabhängiger Aktivierung des Host-IP-Felds.
- Tray Control Center: usbip-win2-Status (grün/rot), kompakter USB-Bereich, Installationsbutton bei fehlendem Client und direkte Modusanzeige (Hyper-V Socket/IP).
- Shared Folder: Host-Katalog laden, Laufwerkszuordnungen anwenden und Mount-Status überwachen (WinFsp-basiert).
- Start mit Windows, Start minimiert, Minimize-to-Tray.
- Guest Control Center im Tray mit USB-Aktionen.
- Wenn Tasktray-Menü deaktiviert ist: nur Ein-/Ausblenden und Beenden.
- Theme-Unterstützung (Dark/Light) und Single-Instance-Verhalten.
- Theme-Neustart erhält die aktuell gewählte Menüseite in der Guest-App.
  
<img width="1041" height="825" alt="hypertool-guest" src="https://github.com/user-attachments/assets/3ef274c2-591c-4364-bdcb-a8f3ba1db74d" />

## Externe Runtime-Repositories (wichtig)

HyperTool vendort diese Projekte nicht als Produktabhängigkeit in die App, sondern nutzt installierte Laufzeiten:

- Host USB Runtime: dorssel/usbipd-win
  - Repository: https://github.com/dorssel/usbipd-win
- Guest USB Runtime: vadimgrn/usbip-win2
  - Repository: https://github.com/vadimgrn/usbip-win2
- Guest Shared-Folder Runtime: winfsp/winfsp
  - Repository: https://github.com/winfsp/winfsp

Hinweise:

- Alle Runtimes werden über deren eigene Releases/Lizenzen bezogen.
- Die HyperTool-Installer bieten optionale Online-Installation dieser Abhängigkeiten.
- Wenn eine Runtime fehlt, werden USB-Funktionen in der UI deaktiviert und mit Hinweis dargestellt.
- Für Shared-Folder-Mounts im Guest wird zusätzlich WinFsp benötigt; fehlt WinFsp, bleibt der Shared-Folder-Runtime-Status auf „Nicht installiert“.
- Für `HyperTool.Guestx86` gelten die Runtime-Voraussetzungen ebenfalls (`usbip-win2`, optional `winfsp` je nach Mapping-Modus).

## Lizenz & Drittanbieter-Hinweise

- HyperTool selbst steht unter der MIT-Lizenz (siehe `LICENSE`).
- Externe Runtimes (`usbipd-win`, `usbip-win2`, `winfsp`) sind eigenständige Projekte mit eigenen Lizenzen.
- In Host-/Guest-Info und in der Hilfe sind die jeweiligen Quellen verlinkt; verbindlich sind immer die Lizenztexte der Original-Repositories.

## Support

Wenn dir HyperTool hilft und du das Projekt unterstützen möchtest:

- Buy Me a Coffee: https://buymeacoffee.com/kaktools

## Voraussetzungen

- Windows 10/11 x64
- Für Host: Hyper-V aktiviert
- Für Entwicklung: .NET SDK 8.x
- Für Installer-Build: Inno Setup 6 (ISCC)

Legacy-Hinweis für Guestx86:

- `HyperTool.Guestx86` ist als WPF/.NET Framework 4.8 Variante für x86 ausgelegt.
- Ziel ist Kompatibilität mit älteren Windows-Versionen (inkl. Win7), abhängig von den installierten Drittanbieter-Runtimes.

## Repository-Struktur

- HyperTool.sln
- src/HyperTool.Core
- src/HyperTool.WinUI
- src/HyperTool.Guest
- src/HyperTool.Guestx86
- installer/HyperTool.iss
- installer/HyperTool.Guest.iss
- build-host.bat
- build-installer-host.bat
- build-guest.bat
- build-guestx86.bat
- build_guestx86.bat
- build_installer_guestx86.bat
- build-installer-guest.bat
- build-all.bat

## Build

### Host

- build-host.bat
- build-installer-host.bat version=2.4.5

### Guest

- build-guest.bat
- build-installer-guest.bat version=2.4.5

### Guestx86 (Legacy WPF)

- build-guestx86.bat
- build_guestx86.bat
- build_installer_guestx86.bat version=2.4.5

### Komplett

- build-all.bat
- build-all.bat version=2.4.5 host guest host-installer guest-installer no-pause

Ausgaben:

- dist/HyperTool.WinUI
- dist/HyperTool.Guest
- dist/HyperTool.Guestx86
- dist/installer-winui
- dist/installer-guest
- dist/installer-guestx86

## Konfiguration

Host-Konfigurationsdatei:

- HyperTool.config.json

Guest-Konfigurationsdatei:

- %ProgramData%/HyperTool/HyperTool.Guest.json

Relevante UI-Schalter:

- ui.enableTrayMenu (Host Tray-Menü erweitern/reduzieren)
- ui.MinimizeToTray bzw. Tasktray-Menü aktiv (Guest Control Center Verhalten)
- ui.startMinimized
- ui.theme
- ui.restoreNumLockAfterVmStart

Versteckte/erweiterte Option (nur per `HyperTool.config.json`):

- ui.numLockWatcherIntervalSeconds (Default: `30`, Bereich: `5..600`)

### Shared-Folder Transport (Guest)

Der Guest nutzt ausschließlich den Transport `hypertool-file` (Hyper-V Socket File Service).

Für `hypertool-file` wird zusätzlich eine installierte WinFsp Runtime im Guest benötigt.

Konfigurationsblock in `%ProgramData%/HyperTool/HyperTool.Guest.json`:

```json
"fileService": {
  "enabled": true,
  "mappingMode": "hypertool-file",
  "preferHyperVSocket": true
}
```

Hinweis zur Zuständigkeit:

- Host-seitige Freigaben/Optionen werden weiterhin über HyperTool Host verwaltet.
- Guest-seitiges Mapping/Transport wird über HyperTool Guest gesteuert.
- HyperTool.Guest mountet die Laufwerksbuchstaben direkt per WinFsp (Explorer sichtbar, ohne klassische SMB-Logon-Abhängigkeit).

## Update-Flow

- Updates basieren auf GitHub Releases (`KaKTools/HyperTool`).
- Asset-Auswahl für Host/Guest ist auf gemeinsame Releases abgestimmt.
- Installer werden nach %TEMP%/HyperTool/updates heruntergeladen und gestartet.

## Logging

- Host: %LOCALAPPDATA%/HyperTool/logs (Fallback je nach Startkontext)
- Guest: %ProgramData%/HyperTool/logs

## Rechtehinweis

- Nicht alle Funktionen benötigen Adminrechte.
- Hyper-V- und USB-Operationen können erhöhte Rechte/UAC erfordern.


