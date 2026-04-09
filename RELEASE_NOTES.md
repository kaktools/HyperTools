# HyperTool Release Notes

## v2.6.0

### Highlights

- Enhanced-Session-Verhalten für VM-Connect wurde vereinheitlicht: statt VM-spezifischer Abweichungen gilt jetzt eine zentrale globale UI-Einstellung.
- Die Option „Für diese VM immer mit Sitzungsbearbeitung öffnen“ wurde aus der VM-Übersicht entfernt, um widersprüchliche Zustände zu vermeiden.
- Der globale Schalter „Enhanced Sessios Modus“ wurde in die Schnelleinstellungen integriert.
- Das USB-Navigationssymbol im Host wurde von `🔌` auf `🔗` angepasst.

### Verbessert

- VM-Connect-Entscheidungslogik nutzt jetzt durchgängig den globalen Wert `UiOpenVmConnectWithSessionEdit`.
- Alte Fallback-/Override-Logik über VM-Metadaten wurde entfernt, wodurch Verhalten und Erwartung in Host-UI und Laufzeit identisch sind.
- Sidebar-Icon für den Bereich `USB-Share` wurde auf das Link-Emoji vereinheitlicht.

### Behoben

- Fälle, in denen VM-spezifische Session-Flags vom globalen UI-Zustand abweichen konnten, treten nicht mehr auf.
- Inkonsistentes Öffnungsverhalten von VM-Connect durch konkurrierende Einstellungen wurde beseitigt.

### Doku

- README auf `v2.6.0` aktualisiert (Release-Stand und Hinweise zum zentralen Enhanced-Session-Schalter).

## v2.5.9

### Highlights

- Legacy-USB-Discovery-Firewallregeln werden bei Host/Guest-Setup und beim App-Start aktiv bereinigt; neue Discovery-Regeln werden nicht mehr angelegt.
- Rechtsklick im Tray öffnet wieder das richtige Control Center; Linksdoppelklick blendet Host- und Guest-App ein bzw. aus.
- Das Guest-Control-Center bleibt stabil an der Taskleiste verankert und springt nicht mehr in problematischen Fällen an den oberen Desktop-Rand.
- Host-Tasktray und Haupt-App synchronisieren Netzwerk-Switch-Änderungen wieder deutlich konsistenter.

### Verbessert

- Firewall/Setup:
	- Host-Installer erstellt keine alten `HyperTool USB Discovery (UDP-In/UDP-Out)`-Regeln mehr.
	- Guest-Installer erstellt keine alte `HyperTool Guest USB Discovery (UDP-Out)`-Regel mehr.
	- Bestehende Legacy-Regeln werden bei Install/Upgrade aktiv entfernt.
	- Host bereinigt zusätzlich eingehende `usbipd`-Firewallregeln, die für den früheren USB-Discovery-/Netzwerkpfad nicht mehr benötigt werden.
	- Host und Guest führen dieselbe Bereinigung zusätzlich beim Start aus, damit auch Update-Pfade ohne vollständige Installer-Deinstallation aufgeräumt werden.
- Tray/Bedienung:
	- Rechtsklick öffnet wieder das jeweilige Control Center statt des klassischen Kontextmenüs.
	- Linksdoppelklick schaltet die Sichtbarkeit der Haupt-App um, ohne das Control Center zu öffnen.
	- Hilfe-/Infotexte für Host und Guest wurden auf das neue Klickverhalten angepasst.
- Guest Control Center:
	- Positionierung nahe der Taskleiste nutzt jetzt robuste Bounds-Prüfungen auch dann, wenn die verfügbare Workarea kleiner als das Panel ist.
	- Dadurch wird das Fenster nicht mehr fälschlich an eine unpassende Desktop-Position geklemmt.
- Host Tray-Sync:
	- Das Host-Control-Center reagiert jetzt direkt auf `TrayStateChanged` aus dem ViewModel.
	- VM-Adapter-/Switch-Zustände werden nach Änderungen im Hauptfenster oder im Tray wieder sauber gegeneinander nachgezogen.
	- Die ausgewählte Switch-Anzeige folgt wieder korrekt dem tatsächlichen Runtime-Zustand der ausgewählten VM bzw. des gewählten Adapters.

### Behoben

- Inno-Setup-Fehler durch PowerShell-Scriptblock-Syntax in `HyperTool.iss` wurden beseitigt.
- Host/Guest konnten nach dem Tray-Klick-Umbau zeitweise das falsche Menü statt des Control Centers öffnen.
- Das Guest-Control-Center konnte in bestimmten Taskleisten-/Auflösungs-Konstellationen oben auf dem Desktop erscheinen.
- Switch-Änderungen im Host konnten zwischen Tray und Hauptfenster sichtbar auseinanderlaufen.

### Doku

- README auf `v2.5.9` aktualisiert (Release-Stand, Firewall-Bereinigung und Tray-/Control-Center-Verhalten).

## v2.5.8

### Highlights

- USB-Stabilität zwischen Host und Guest wurde für Refresh-/Mode-Übergänge gezielt gehärtet.
- Ungewollte Auto-Detach-Kaskaden bei transienten Zuständen (VM-Refresh, USB-Refresh, Troll/Disco) wurden deutlich reduziert.
- Host-VM-Statusanzeige bleibt nach Shutdown nicht mehr fälschlich auf `Running` stehen.
- Guest-UX beim manuellen USB-Disconnect wurde erweitert: Auto-Connect kann direkt mit deaktiviert werden.

### Verbessert

- Host USB Auto-Detach:
	- VM-off-Detach berücksichtigt jetzt frische Guest-Heartbeats/Acks als Schutzsignal und detach’t in diesem Zeitfenster nicht aggressiv.
	- Dadurch bleiben Guest-Attachments bei Host-VM-Refresh und Host-USB-Refresh stabiler bestehen.
	- Schutz greift auch in intensiven UI-/Mode-Übergängen (z. B. Disco/Troll), in denen Runtime-States kurzzeitig schwanken können.
- Guest USB Signalpfad:
	- Automatische `usb-disconnected`-Signale werden in einem temporären Schutzfenster unterdrückt, wenn ein interaktiver USB-Refresh läuft.
	- Gleiches Schutzfenster greift beim Guest-Troll-Mode, um keine unnötigen Host-Detach-Folgen auszulösen.
	- Stale-Export-Disconnect-Signalisierung respektiert dieses Schutzfenster ebenfalls.
- Guest UI/Bedienung:
	- Nach erfolgreichem manuellem USB-Disconnect mit aktivem Auto-Connect erscheint ein Dialog:
		- `Auto-Connect deaktivieren`
		- `Behalten`
		- `Abbrechen`
	- Damit lässt sich das bisherige "direkt wieder verbinden" gezielt vermeiden.
- Host VM-Status-Sync:
	- Nach Runtime-Listen-Rebuild werden `SelectedVmState` und aktueller Switch-Status explizit synchronisiert.
	- Status-Chips zeigen dadurch den tatsächlichen Zustand sofort korrekt an.

### Behoben

- Guest-USB konnte bei Host-VM-Refresh ungewollt detacht werden.
- Guest-USB konnte bei Host-USB-Refresh ungewollt detacht werden.
- Troll-/Disco-Modi konnten indirekt USB-Disconnect/Detach-Ketten auslösen.
- Guest-seitiger Troll-Mode oder USB-Refresh konnte fälschlich ein `usb-disconnected` in Richtung Host triggern.
- Host-Statuschip blieb nach einfachem VM-Shutdown bis zum manuellen Refresh auf `Running`.
- Beim Guest-Disconnect fehlte eine direkte Entscheidungshilfe für gleichzeitiges Auto-Connect-Disable.

### Doku

- README auf `v2.5.8` aktualisiert (Release-Stand + USB/VM-Stabilitätsfixes + Guest-Disconnect-Dialog).

## v2.5.7

### Highlights

- USB-Detach/Refresh im Host wurde für Guest-Disconnect-Fälle deutlich robuster und reaktiver gemacht.
- Multi-VM-USB-Zuordnung (`Connected By`/`Busy`) ist stabilisiert, auch bei schnellen Disconnect/Connect-Folgen.
- Guest-Start wurde gegen sporadische Concurrent-Collection-Fehler im USB-Refresh gehärtet.
- Konfigurationsmigration bereinigt bestehende Installationen stärker und setzt neue USB-Defaults konsistent.
- Guest USB-Transport ist jetzt vollständig Hyper-V-only; IP-Mode/IP-Fallback und zugehörige UI-Artefakte wurden entfernt.
- Hyper-V Socket Selbsttest schreibt bei erfolgreicher Prüfung jetzt gezielte Stabilitäts-Logs im Guest und eine klare Bestätigung im Host.

### Verbessert

- Host USB Detach/Refresh:
	- Explizite `usb-disconnected`-Events aus dem Guest lösen den Host-Detach immer aus (auch wenn der Auto-Detach-Config-Schalter deaktiviert ist).
	- Debounce für Guest-Disconnect-getriggerten Host-Detach reduziert, damit der Host merklich schneller reagiert.
	- Für explizite Guest-Disconnects entfällt die zusätzliche lange Grace-Wartezeit; nach Debounce erfolgt der Detach zeitnah.
	- Während des Disconnect-Pfads blockiert die Grace-Phase keine regulären USB-Refreshes mehr.
	- Physisch entfernte Geräte (`Attached` ohne `Connected`) werden beim Host-Refresh automatisch detacht.
	- `usbipd`-Fehler `There is no device with busid ...` wird als No-Op behandelt.
	- Hyper-V-Socket-Host-Listener (File, Diagnostics, Resource-Monitor, Host-Identity, Shared-Folder-Katalog, USB-Change-Notification) nutzen jetzt ein Concurrency-Gating, um ungebremste Handler-/Socket-Fächerung unter Last zu verhindern.
- Multi-VM USB-Zuordnung:
	- `usb-disconnected` für Gerät A entfernt nicht mehr versehentlich die `Connected By`-Zuordnung von Gerät B mit ähnlichem Hardware-Profil.
	- Ack-Identity priorisiert `busid` vor `hardware`, um Kollisionen zu reduzieren.
	- Guest-Share-Liste hält Geräte mit Host-Attachment-Hinweis weiter als `Busy`, auch wenn `Connected By` kurzzeitig noch leer ist.
- Guest Stabilität:
	- Host-getriggerte USB-Change-Pushes umgehen im Guest das Refresh-Rate-Limit, damit Disconnect/Detach-/Re-Share-Zustände direkt sichtbar werden.
	- Nach erfolgreichem Guest-Disconnect wird zusätzlich ein erzwungener USB-Refresh gestartet, um kurzzeitig hängende Busy-Zustände schneller aufzulösen.
	- USB-Host-Caches wurden auf thread-safe Collections umgestellt; der sporadische Fehler `Operations that change non-concurrent collections...` nach Guest-Neustart ist behoben.
	- Hyper-V-Socket-Clientpfade (File-Service, Shared-Folder-Katalog, Host-Identity, Diagnostics, Resource-Monitor) wurden auf ein gemeinsames Concurrency-Gating umgestellt, um Socket-Stürme (`NoBufferSpaceAvailable` / volle Warteschlangen) unter Last zu vermeiden.
	- `NoBufferSpaceAvailable` wird beim Shared-Folder-Katalog jetzt als transient behandelt, damit Retry/Backoff greift statt sofortigem Abbruch.
	- Neue Shared-Folder/Socket-Diagnostik in Guest-Logs: bei `sharedfolders.catalog.fetch_failed` und `sharedfolders.apply.catalog_failed` werden jetzt Gate-/Queue-Metriken (inflight, waiters, peak, average wait, slow acquires), Reconnect-Gate-Status und Config-Save-Rate mitgeloggt, um Überlaufursachen gezielt zu erkennen.
	- WinFsp-Create-Integration im Guest korrigiert: bestehende Dateien werden bei `Create` nicht mehr implizit mit `truncate` angefasst; das verhindert Dateikorruption bei Copy/Paste im selben Shared-Folder.
	- WinFsp-Overwrite-Integration im Guest ergänzt: Explorer-Flow `Datei ersetzen` funktioniert jetzt korrekt und bricht nicht mehr mit `Ungültige MS-DOS-Funktion` ab.
	- USB-Kommentar-Sync Host→Guest gehärtet: nach erfolgreichem USB-Refresh wird Host-Identity mit Cooldown gezielt nachgezogen, und Fetch-Fehler werden rate-limited mit Socket-Details geloggt statt still geschluckt.
	- Hyper-V-Socket-Monitoring im Guest erweitert: periodischer Debug-Heartbeat (`hyperv.monitor.heartbeat`) mit Transportstatus, Task-/Loop-Status, Gate-Metriken sowie Kanalzählern (Host-Identity, Shared-Folder-Katalog, ACKs, Hostauflösung) inkl. letzter Erfolgs-/Fehlerzustände.
	- Guest transportiert USB ausschließlich über Hyper-V Socket; Discovery/Fallback über IP ist deaktiviert.
	- Guest-UI bereinigt: kein IP-Mode-Toggle, kein Host-IP-Eingabefeld, Transport-Diagnose ohne Fallback-Zeile.
	- USB-Header-Chip auf klare Zustände umgestellt: aktiv (grün), inaktiv (grau), Fehler (rot).
	- Guest-Transporttest loggt bei `Hyper-V Socket + Registry = OK` zusätzlich `usb.transport.hyperv.test.stable` sowie nach Ack `usb.transport.hyperv.test.stable.host_ack`.
	- Host protokolliert erfolgreiche Guest-Diagnosen zusätzlich als `Hyper-V socket stability confirmed by guest diagnostics`.
- Konfigurationsmigration/Setup-Hygiene:
	- Bestehende Konfigurationen werden schema-basiert bereinigt und einmalig neu geschrieben.
	- Legacy-/ungültige USB-Identity-Keys in `usb.autoShareDeviceKeys` und `usb.deviceMetadata` werden entfernt.
	- Neuer Standardwert für `usb.autoDetachGracePeriodSeconds` ist `5` Sekunden.
	- Bei Update-Installationen wird dieser Wert beim nächsten Laden einmalig auf `5` migriert; danach weiterhin frei konfigurierbar.
	- Host: Defekte `HyperTool.config.json` wird beim Laden automatisch als `.corrupt.*` gesichert; anschließend Start mit Standardkonfiguration statt Abbruch.
	- Guest: Defekte `HyperTool.Guest.json` wird beim Laden automatisch als `.corrupt.*` gesichert; anschließend Safe-Start mit Standardwerten.

### Doku

- README auf `v2.5.7` aktualisiert (USB-Defaults, Config-Migration und Guest-Transportabgrenzung).

## v2.5.4

### Highlights

- Snapshot-Beschreibungen bleiben jetzt auch nach App-Neustart und Reload zuverlässig erhalten.
- USB-Auto-Detach im Host wurde auf einen klaren, deterministischen Ablauf reduziert: nur bei Guest-Disconnect-Event oder wenn die zugehörige VM (per VM-ID) mindestens 10 Sekunden nicht läuft.
- USB-UI im Host ist eindeutiger: eigener `Detach`-Button direkt neben `Share` und `Unshare`; der Auto-Detach-Schalter wurde aus der Oberfläche entfernt.
- Detach im Host (manuell + Auto-Detach) läuft ohne zusätzliche UAC-Elevation.

### Verbessert

- Snapshot-Persistenz:
	- Checkpoint-Beschreibungs-Overrides werden in der Host-Konfiguration gespeichert und beim Laden korrekt wiederhergestellt.
	- Das verhindert das bisherige "Beschreibung verschwindet nach Neustart"-Verhalten.
- Host USB Detach-Policy:
	- Zyklische Guest-ACK-/Liveness-basierte Auto-Detach-Heuristiken wurden entfernt.
	- Auto-Detach ist auf explizite Trigger beschränkt (`usb-disconnected` oder VM-ID nicht Running >= 10s).
	- Bei `usb-disconnected` wartet der Host jetzt nicht nur die Grace-Phase ab, sondern prüft währenddessen aktiv auf frische Reconnect-/Heartbeat-Aktivität und überspringt den Detach, wenn sich der Guest stabil zurückmeldet.
	- Bei fehlgeschlagenem Auto-Detach bleibt der manuelle Weg (`Detach`/`Unshare`) als kontrollierter Fallback erhalten.
	- Für diese Trigger nutzt der Host wieder konfigurierbare Retry/Grace/Delay-Werte (`usb.autoDetachRetryAttempts`, `usb.autoDetachGracePeriodSeconds`, `usb.autoDetachRetryDelayMs`).
- Host USB UX:
	- Neuer `Detach`-Button in der Aktionsleiste (neben `Share`/`Unshare`).
	- Einstellung `Automatisches Detach nach Disconnect` ist nicht mehr per UI umschaltbar und wird nur noch über die Config gesteuert.
- Guest Stale-Export-Recovery:
	- Bei wiederholtem `already exported` + lokal `not attached` sendet der Guest ein gezieltes `usb-disconnected` Signal an den Host.
	- Dadurch kann der Host den stale Attach per Auto-Detach-Retry-Kette lösen, ohne alte zyklische Liveness-Checks wieder einzuführen.

### Behoben

- Snapshot-Beschreibungen gingen nach Reload/Neustart verloren, obwohl der Snapshot selbst vorhanden war.
- USB-Stale-Recovery reagierte in Grenzfällen zu aggressiv durch zyklische Liveness-Logik; die Detach-Entscheidung folgt jetzt nur noch den klar definierten Triggern.
- UAC-Anforderung für Host-Detach entfällt; Detach wird ohne zusätzliche Elevation ausgeführt.

### Doku

- README auf `v2.5.4` aktualisiert (Release-Stand, USB-Detach-Verhalten, Config-Hinweis).

## v2.5.2

### Highlights

- USB-Transport in der Guest-App wurde gegen kurzzeitige Hyper-V-Socket-Abbrüche deutlich robuster gemacht, damit nicht unnötig auf IP-Fallback gewechselt wird.
- Host und Guest nutzen jetzt konsistente Session-Logdateien mit Zeitstempel; bei aktiviertem Debug-Modus wird zusätzlich ein klarer Dateinamens-Suffix verwendet.
- Log-Retention wurde gehärtet: Dateien älter als 3 Tage werden zuverlässiger entfernt, auch wenn sie schreibgeschützt sind.

### Verbessert

- Guest USB Transport-Stabilität:
	- Transiente Hyper-V-Transportfehler werden breiter erkannt (u. a. Connection reset/aborted/timed out/refused).
	- Vor einem Fallback auf IP werden mehrere gestaffelte Hyper-V-Retries ausgeführt.
	- Das reduziert Flattern zwischen Hyper-V und IP-Fallback bei kurzen Verbindungsstörungen.
- Logging Host/Guest:
	- Pro App-Start wird eine neue Session-Datei mit Zeitstempel erstellt.
	- Bei aktivem Debug-Logging wird der Dateiname mit `-Debug` gekennzeichnet.
	- Dateinamen bleiben damit eindeutig und besser filterbar.

### Behoben

- Guest konnte trotz aktivem Hyper-V-Socket-Tunnel in einen unnötigen IP-Fallback-Zustand kippen, wenn kurzzeitig ein Remote-Reset auftrat.
- Sehr alte Logdateien konnten in Einzelfällen liegen bleiben; die Bereinigung behandelt jetzt auch ReadOnly-Dateien robuster.

### Doku

- README auf `v2.5.2` aktualisiert (Release-Stand + Build-Beispiele).

## v2.5.0

### Highlights

- USB Multi-VM Verhalten wurde end-to-end stabilisiert: Guest kann jetzt verlässlich erkennen, wenn ein USB-Gerät in einer anderen VM attached ist (`Busy`).
- Snapshot-Ansicht in der Host-App ist wieder voll nutzbar mit sichtbaren Zeileninhalten (Name/Beschreibung/Datum) und klarerer `Aktuell`-Markierung.
- Guest-Benachrichtigungen unten in der UI wurden auf kompakte, lesbare Zeilen reduziert, ohne Verlust der Detailinfos in den Datei-Logs.
- USB-Reset-Migration beim Guest ist jetzt als sauberer First-Start-Flow für Update-Installationen umgesetzt.

### Verbessert

- Guest USB Status/Konsistenz:
	- Host-Identity-Refresh läuft robuster in den USB-Refresh-Pfaden.
	- Host-Payload enthält jetzt `usbDeviceAttachments`, damit Guest den echten VM-übergreifenden Attach-Zustand kennt.
	- Statusabbildung zeigt bei Attach in anderer VM `Busy` statt irreführend `Available`/`Attached`.
- Host USB Refresh/UI:
	- USB-Liste wird bei unveränderter Identitätsreihenfolge in-place aktualisiert.
	- Sichtbares Flackern bei periodischen Refreshes deutlich reduziert.
- Snapshot UX:
	- Baumzeilen-Template korrigiert, Inhalte wieder sichtbar.
	- `Aktuell`-Badge/Highlighting klarer positioniert.
	- Beschreibungen aus Snapshot-Create-Flow werden zuverlässiger gehalten (inkl. Fallback, falls Hyper-V Notes nicht direkt persistieren).
- Host Detach Recovery:
	- Auto-Detach für stale USB-Attachments robuster bei Hard-Off/unklarem ACK-Zustand.
	- Loopback-/guest-gemanagte Sonderfälle konservativer behandelt, um unnötiges Churn zu vermeiden.
- Guest Logging/Notification UX:
	- Datei-Logging auf stabile Einzeldatei konsolidiert (kein Session-Datei-Spam).
	- Notification-Panel entfernt für Logger-Zeilen zusätzliche `(event=...)` und JSON-Payload-Suffixe für bessere Lesbarkeit.

### Behoben

- Snapshot-Zeilen konnten nach vorherigen UI-Änderungen ohne sichtbaren Text erscheinen.
- Guest konnte USB-Geräte in Multi-VM-Szenarien teilweise als verfügbar anzeigen, obwohl sie in anderer VM attached waren.
- Host stale-detach konnte nach unsauberem VM-Shutdown zu lange ausbleiben.
- Guest Notification-Bereich zeigte zu viele technische Detailanhänge statt kurzer Statusmeldungen.
- USB-Migrationshinweis konnte unpassend auch bei frischer Neuinstallation erscheinen; jetzt nur noch fuer Update-Pfade.

### Doku

- README auf `v2.5.0` aktualisiert (Release-Stand + Build-Beispiele + aktuelle USB/Snapshot/Notification-Verbesserungen).

## v2.4.8

### Highlights

- USB-Dongle-Share fuer produktive Umgebungen weiter gehaertet (Guest + Host), mit Fokus auf Stabilitaet bei lang laufenden Sessions.
- Guest behandelt `already exported` jetzt robuster, damit stale Exports auf dem Host nicht kuenstlich am Leben gehalten werden.
- Hyper-V-Socket Self-Heal im Guest deutlich konservativer gemacht und bei aktiv attached USB ausgesetzt.
- Host-Resource-Monitoring gegen WinUI-Threading-Fehler gehaertet (COMException `0x8001010E` behoben).

### Verbessert

- Guest USB Attach/Recovery:
	- Bei `already exported` wird ein Heartbeat nur noch gesendet, wenn das Geraet nach Refresh lokal wirklich als `Attached` sichtbar ist.
	- Neues Diagnose-Event fuer den Problemfall `already exported + lokal nicht attached` hinzugefuegt.
- Guest Transport-Stabilitaet:
	- Self-Heal-Threshold fuer Hyper-V-Socket-Probes angehoben.
	- Restart-Backoff verlaengert.
	- Self-Heal-Restart wird uebersprungen, solange USB im Guest aktiv attached ist.
- Guest Refresh-Verhalten:
	- Adaptive USB-Auto-Refresh-Intervalle (schnell/langsam je nach Zustand).
	- Hintergrund-Refresh und Host-Identity-Abfragen bei bereits attached USB zusaetzlich gedrosselt.
	- Debug-Log-Spam aus stillen Auto-Refresh-Zyklen reduziert.
- Host Monitoring/Logging:
	- Resource-Monitor-Loop mit besserer Fehlerisolation (Teilschritte separat abgesichert).
	- Failure-Logging rate-limitiert mit Suppressed-Count statt Dauer-Feuer.
	- Updates in den Monitoring-Pfaden auf UI-Thread marshalled, um WinRT/COM-Threading-Probleme zu vermeiden.
- Host USB Stale-Detach:
	- Konservativere Grace-/Retry-Parameter fuer loopback-nahe bzw. guest-gemanagte Attachments.
	- Wiederholte Debug-Logs fuer ACK-Tracking deutlich reduziert.

### Behoben

- `Resource monitor loop cycle failed` durch `COMException (0x8001010E)` im Host-Monitorpfad beseitigt (UI-thread-sicheres Update).
- Risiko reduziert, dass produktive Dongle-Sessions bei transienten Transport- oder ACK-Stoerungen unnoetig getrennt werden.
- Dauerhafte `already exported`-Schleifen im Guest-Connect-Pfad entschraerft, wenn lokal kein valider Attach-Zustand vorliegt.

### Doku

- README auf `v2.4.8` aktualisiert (Release-Stand, Build-Beispiele, Stabilitaets-Hinweise).

## v2.4.7

### Highlights

- VM-Ressourcenmonitor arbeitet jetzt mit Dual-Quelle pro VM: Guest-Agent bevorzugt, Host-Fallback automatisch.
- VM-Zuordnung in Diagnostics/Monitoring wurde auf `VmId` erweitert (inkl. Hyper-V-Socket-Quell-ID), wodurch Zuordnungen deutlich stabiler sind.
- Guest-USB-Transport hat eine Self-Heal-Strategie fuer den Hyper-V-Socket-Proxy bei wiederholten Erreichbarkeitsfehlern.
- Build-/Installer-Dokumentation auf `2.4.7` angehoben.

### Verbessert

- Resource-Monitor VM:
	- Host sammelt pro VM CPU/RAM via Hyper-V (`Get-VM`) und liefert Fallback-Werte bei Guest-Aussetzern.
	- Snapshot-Struktur fuehrt jetzt Guest- und Host-Metriken getrennt (`ActiveSource`, Guest/Host-CPU/RAM), inklusive sauberer Source-Anzeige in der UI.
	- VM-Status-Refresh nutzt gezielte Einzel-VM-Abfrage (`GetVmAsync`) statt Voll-Refresh und vermeidet parallele Refresh-Ueberlappungen.
- Monitoring-Zuordnung:
	- VM-Modelle und Runtime-Pakete enthalten `VmId`, wodurch Name-only-Kollisionen reduziert werden.
	- Socket-Listener reichern eingehende Pakete/Acks mit der tatsaechlichen Remote-VM-ID an, falls diese im Payload fehlt.
- USB Refresh-Stabilitaet:
	- Host und Guest nutzen Gate + kurze Drosselung gegen Event-Stuerme und redundante Parallel-Refreshes.
	- Host triggert keinen zusaetzlichen Refresh mehr auf reine `usb-heartbeat` Events.

### Behoben

- VM-Monitorwerte konnten bei fehlenden Guest-Daten oder instabiler Namenszuordnung auf `nicht erreichbar` fallen, obwohl Host-Daten verfuegbar waren.
- Wiederholte USB-Diagnoseevents konnten unnoetig viele Refreshes starten und so UI/Tray-Aktualisierung belasten.
- Guest-Hyper-V-Socket-Proxy blieb nach mehrfachen Probe-Fehlern teils im fehlerhaften Zustand, statt sich kontrolliert zu erholen.

### Doku

- README auf `v2.4.7` aktualisiert (Release-Stand + Build-Beispiele + aktuelle Monitoring-/USB-Verbesserungen).

## v2.4.6

### Highlights

- Host-Ressourcenmonitor aktualisiert CPU/RAM im offenen Fenster wieder zuverlässig im eingestellten Intervall (Live-Monitoring).
- Host-/Guest-USB-Auswahl im Tasktray/Control-Center wurde stabilisiert (kein Zurueckspringen auf das erste Geraet bei Connect/Disconnect).
- USB-Kommentare werden im Host-Tasktray jetzt ohne Neustart korrekt und sofort aktualisiert.
- Build-/Installer-Artefakte fuer Host und Guest auf `2.4.6` erstellt.

### Verbessert

- Resource-Monitor:
	- Refresh-Loop im Fenster robuster gemacht.
	- Snapshot-Pfad gegen parallele VM-Listen-Aenderungen gehaertet.
	- Host-Sampling-Fallback ergaenzt, damit Host-Werte auch bei stoerenden Zwischenzustaenden weiterlaufen.
	- UI-Refresh-Blocker durch wiederverwendetes Button-Element beseitigt.
- CPU-Sampling Host:
	- `% Processor Utility` als primaere Quelle integriert (naeher an Task-Manager-Interpretation).
	- Fallback-Kette auf WMI/GetSystemTimes bleibt aktiv.
- VM-Monitorstatus:
	- Standard-/Fallbacktexte auf `Guest nicht erreichbar` vereinheitlicht.
	- Flackern in VM-Chips reduziert, indem Monitortexte beim Runtime-State-Rebuild erhalten bleiben.
- USB-Auswahl/Labels:
	- Host SelectionKey-Logik an Core-Identitaetslogik angeglichen (GUID/Instance/Hardware/BusID).
	- Guest-Tray-Auswahl priorisiert jetzt die echte Tray-Selektion vor Main-Window-Fokus.
	- Label-/Kommentar-Renderkeys im Host-Control-Center erweitert, damit Kommentar-Updates sofort sichtbar sind.

### Behoben

- Host-Ressourcenmonitor zeigte teils nur den Oeffnungszustand und aktualisierte danach nicht mehr.
- Host-Tasktray-Kommentar erschien bei neu kommentierten USB-Geraeten erst nach App-Neustart.
- Guest-Tasktray nutzte bei Connect/Disconnect teils nicht das ausgewaehlte USB-Geraet.
- Host-Tasktray/Control-Center uebernahm USB-Auswahl teils erst beim zweiten Klick.

### Doku

- README auf `v2.4.6` aktualisiert (Version + Build-Beispiele + aktueller Release-Stand).

## v2.4.5

### Highlights

- USB Disconnect/Detach zwischen Guest und Host wurde end-to-end nachgeschärft (Sofort-Detach + Fallback mit Grace/Retry).
- Host-Diagnostics-Ack-Verarbeitung ist jetzt fehlertolerant, damit Disconnect-Erkennung nicht mehr durch einzelne Callback-Fehler verloren geht.
- USB Share verwendet wieder durchgängig normales `bind --busid` ohne `--force`.
- Resource-Monitor wurde visuell und funktional überarbeitet (stabilere Host-Darstellung, sauberere KPI-Ausrichtung, konsistente Benennungen).
  
### Verbessert

- Host Auto-Detach:
	- Sofortversuche nach `usb-disconnected` Event mit mehreren Retries.
	- Zusätzliche Zustandsprüfung und beschleunigter Fallback für unerreichbare Guests.
	- Tracking für guest-verwaltete Attachments robuster gemacht, auch wenn Ack-/Transportpfade schwanken.
- Host Diagnostics:
	- Ack-Pipeline zentralisiert und in Teilschritte mit isoliertem Fehlerhandling aufgeteilt.
	- USB-Disconnect- und Refresh-Trigger bleiben aktiv, auch wenn ein anderer Ack-Teilpfad fehlschlägt.
- Guest/Host USB-Listen:
	- Unnötige Full-Rebuilds reduziert, wodurch sporadisches Flackern bei Auto-Refresh/Identity-Updates deutlich abnimmt.
- VM-Verwaltung:
	- `VM entfernen` im VM-Menü und per Rechtsklick mit Bestätigungsdialog ergänzt.
	- Entfernen löscht die VM jetzt tatsächlich aus Hyper-V (`Remove-VM`) statt nur aus der lokalen Konfiguration.
- Resource-Monitor UI:
	- Begriffe vereinheitlicht (`Ressourcenmonitor`, `Prozessor`, `Arbeitsspeicher-Auslastung`, `Verlauf`).
	- Host-Metriken zentriert über den jeweiligen Trends ausgerichtet.
	- VM-Darstellung priorisiert verbundene VMs und nutzt horizontales Scrolling für zusätzliche Karten.
	- Fenstermaße und Abstände für 2-VM-Sichtbarkeit mehrfach feinjustiert.
- Resource-Monitor Stabilität:
	- Refresh-/Snapshot-Pfad fehlertoleranter gemacht, inklusive Fallback auf letzte gültige Daten.
	- Rendering atomarisiert, um temporäre Leerzustände im Host-Bereich zu vermeiden.

### Behoben

- Regression im USB-Freigabezyklus adressiert, bei der Geräte nach Guest-Disconnect im Host als `Attached` hängenbleiben konnten.
- Host/Guest-Namensanzeige für USB-Geräte wieder konsistent, da Host-Identity-Payload die Beschreibungen zuverlässig überträgt.
- VM-Remove-Flow korrigiert: vorher nur Konfigurationsentfernung (`aus Konfiguration entfernt`), jetzt echte Hyper-V-Löschung.

### Doku

- README auf `v2.4.5` aktualisiert (Release-Stand + Build-Beispiele).

## v2.4.2

### Highlights

- Resource Monitor zeigt 2 VM-Karten jetzt stabil ohne horizontale Scrollbar; Scrollen beginnt erst bei mehr als 2 VMs.
- Host-Sidebar wurde um weitere 10 px verschlankt und die Navigationsbuttons sind vertikal gleichmäßig verteilt.
- VM-Chips reagieren zuverlässiger auf verzögert eintreffende Agent-/Monitoring-Daten.

### Verbessert

- Resource-Monitor-Fensterbreite von `1120` auf `1125` erhöht, damit das 2-VM-Layout sauber passt.
- Sidebar-Layout über ein gleichmäßig verteiltes Raster umgesetzt (gleiche Abstände zwischen Schaltflächen sowie zu oberem/unterem Rand).
- Zusätzlicher periodischer VM-Chip-Refresh im Host ergänzt, als Fallback neben dem eventbasierten `ResourceMonitorVersion`-Refresh.

### Doku

- README auf `v2.4.2` aktualisiert und fehlerhafte Umlaut-Darstellung bereinigt.

## v2.4.0

### Highlights

- Branding vollständig auf `KaKTools` umgestellt (Host, Guest, Doku, Installer, Lizenz).
- Update-Flow in Host und Guest verwendet jetzt standardmäßig `KaKTools/HyperTool`.
- Build-/Installer-Defaults auf Version `2.4.0` angehoben.

### Neu

- GitHub Release-Links, Owner-Anzeigen und Fallback-Release-URLs in Host/Guest aktualisiert.
- Copyright-Anzeigen in Start-/Reload-/Exit-Screens sowie Info-Bereichen auf `KaKTools` umgestellt.

### Doku

- README, Release Notes und LICENSE auf `KaKTools` + `v2.4.0` angepasst.

## v2.3.8

### Highlights

- Host-Netzprofil-Workflow ist jetzt direkt in der UI bedienbar (VM-View + Host-Network pro Adapter).
- Netzprofil-Änderungen nutzen UAC-Elevation, damit die Funktion auch ohne als Admin gestartete App nutzbar ist.
- Neuer optionaler NumLock-Wächter im Host, steuerbar per Checkbox, mit konfigurierbarem Hintergrund-Intervall.

### Neu

- Host VM-Ansicht:
	- Sichtbarer Host-Netzprofilstatus im Footer (`Öffentlich` / `Privat` / `Domäne`).
	- Direkter Aktionsbutton mit Gegenzustand (z. B. `Auf Privat umstellen` bei `Öffentlich`).
	- Bei `Domäne` ist die Aktion bewusst gesperrt.
- Host-Network Fenster:
	- Profil-Chips pro Adapter (`Privat`/`Öffentlich`/`Domäne`) ergänzt.
	- Direkte Profil-Umstellung pro Adapter per Aktionsbutton.
- Config:
	- Neue Option `ui.restoreNumLockAfterVmStart` (Checkbox in der Oberfläche).
	- Erweiterte Option `ui.numLockWatcherIntervalSeconds` (nur Config-Datei, Default `30`).

### Verbessert

- Netzprofil-Fehlerhandling:
	- Klare Meldungen für UAC-Abbruch, fehlende Rechte, Domain-Profil-Sperre und GPO-Blockierung.
	- UI-Zustände bleiben konsistent bei fehlgeschlagenen Umschaltungen.
- Import-Flow:
	- Zielordner-Handling für `copy/register/restore` präzisiert.
	- Import-Hinweise in UI und Konfiguration klarer strukturiert.
- Snapshot-Flow:
	- `Create` per Dialog (Name/Beschreibung).
	- `Restore/Delete` mit Bestätigungsdialogen.
- Guest USB Auto-Connect:
	- Auto-Connect versucht Verbindungen nur noch bei Host-seitig tatsächlich freigegebenen Geräten (`Shared`).
	- Wiederholte Attach-Fehler laufen nicht mehr in eine aggressive Dauerschleife (Backoff statt Dauer-Retry).

### Behoben

- Host USB-UI: robustere Selection-Synchronisierung gegen Index/State-Race in WinRT-ListView-Brücken.
- Host USB Status-Konsistenz:
	- Stale `Attached`-Zustände nach verpasstem Guest-Disconnect-Event werden im Host nach Grace-Periode automatisch bereinigt (Auto-Detach).
- Troll-Overlay Host/Guest: Shake/Wobble/Warp wiederhergestellt und Reset/Centering stabilisiert.
- Update-Sicherheit: Konfigurationsdateien werden bei Updates nicht mehr unbeabsichtigt überschrieben (Host/Guest Installer + Laufzeitpfade).
- Guest Start-Stabilität:
	- Startup-Splash hat jetzt einen Failsafe, damit die Oberfläche auch bei Fehlern im Startup-Flow nicht im Overlay hängenbleibt.

### Doku

- README auf `v2.3.8` aktualisiert.

## v2.3.7

### Highlights

- Host- und Guest-Oberflächen wurden für den täglichen VM-Workflow sichtbar entschlackt und stärker vereinheitlicht.
- VM-Chips, Header-Status und Kontextaktionen sind im Host präziser und schneller bedienbar.
- Ungespeicherte Konfigurationsänderungen reagieren in Host und Guest konsistent beim Wechseln/Neu-Laden.

### Neu

- Host VM-Kontextaktionen:
	- `Als Default-VM setzen` direkt über VM-Chip-/VM-Menü verfügbar.
	- `Schnellstart-Verknüpfung erstellen` direkt über VM-Chip-/VM-Menü verfügbar.
- Info-Menü (Host/Guest):
	- Neuer gelber `Buy Me a Coffee`-Button mit Kaffee-Icon und Direktlink: `https://buymeacoffee.com/kaktools`.
- Guest USB:
	- Option zum automatischen USB-Disconnect beim Beenden ergänzt.
	- USB-Refresh beim Guest-Start verbessert, damit Device-Status früher konsistent ist.
- Header-Status (Host):
	- `Selected VM` zeigt den aktuellen State als farbigen Status-Chip (Running grün, Off rot), theme-sensitiv für Dark/Light.
- Host/Guest Easteregg:
	- Ein neues, verstecktes Easteregg wurde eingebaut - ohne Spoiler, nur so viel: Es lohnt sich, die UI aufmerksam zu erkunden.


### Verbessert

- Host VM-Chips:
	- Chip-Breiten verhalten sich stärker inhaltsbasiert (kurze VM-Namen wirken nicht mehr unnötig breit).
	- PC-Icon und Default-Stern wurden visuell vergrößert und entquetscht, ohne die Chip-Größe aufzublähen.
	- Default-Markierung als Badge/Overlay optisch präzisiert.
- Guest Header:
	- VM-Chips stabil unter dem Titel platziert, um Resize-Jitter und inkonsistente Rückwechsel zu vermeiden.
- Layout/UX (Host/Guest):
	- System-/Update-Bereiche und Abstände in mehreren Ansichten nachgeschliffen.
	- Checkbox-/Header-Abstände konsistenter für bessere Lesbarkeit.
	- Optionale Runtime-Aufgaben im Installer sichtbarer gemacht, damit notwendige Komponenten    	schneller erkennbar sind.


### Behoben

- Host:
	- Potenzieller UI-Freeze beim Verwerfen (`Nein`) ungespeicherter Änderungen adressiert.
	- Reload-Pfad nach `Nein` auf konsistentes Snapshot-Reload umgestellt.
- Guest:
	- `Nein`-/Reload-Verhalten beim Verwerfen ungespeicherter Änderungen robuster gemacht.
	- Menüwechsel-Prompt ergänzt, damit Änderungen nicht still verworfen werden.
	- USB/IP-Client-Erkennung robuster gemacht, damit "Nicht installiert"-/"Verfügbar"-Status nach Installation/Deinstallation konsistent ist.

### Doku

- README auf `v2.3.7` aktualisiert (Release-Stand, Feature-Hinweise, Build-Beispiele).

## v2.3.5

### Highlights

- Shared Folder wurde vollständig in Host und Guest integriert (Katalog, Mapping, Runtime-Status, Diagnose, UI-Gating).
- Host/Guest-Runtime-UX ist jetzt durchgängig konsistent: Status, Installationsbuttons, Neustart-Buttons und Reload-basierter Tool-Neustart.
- Guest-Info und Hilfe wurden inhaltlich erweitert (inkl. WinFsp als externe Quelle) und visuell präzisiert.

### Neu

- Shared Folder (Host/Guest):
	- Host verwaltet Freigaben zentral, Guest mappt über `hypertool-file`.
	- Guest nutzt WinFsp als Mount-Runtime; fehlende Runtime wird explizit angezeigt.
	- Host-/Guest-Feature-Gating für Shared Folder inkl. klarer Aktiv/Inaktiv-Status und Overlay-Hinweise.
- Info/Hilfe Guest:
	- Neue externe Quelle `winfsp/winfsp` im Info-Menü inkl. direktem Quellen-Link.
	- Externe Quellenkarten (`usbip-win2`, `winfsp`) nebeneinander im 50/50-Layout.
	- Kartenlayout mit identischer Mindesthöhe und abgestimmter vertikaler Ausrichtung.
- Config UX:
	- `Tool neu starten` zusätzlich in Host- und Guest-Config-Headern neben `Speichern` und `Neu laden`.

### Verbessert

- Neustartverhalten:
	- Host-`Tool neu starten` entspricht jetzt exakt dem Theme-Wechsel-Ablauf (kurzer Reload-Screen, danach Reopen).
	- Guest-`Tool neu starten` nutzt denselben Reload-Flow über den bestehenden Theme-Reopen-Mechanismus.
	- Einheitliches Icon/Label-Schema für alle `Tool neu starten` Buttons.
- Runtime-Status/UI:
	- Installations-/Neustart-Buttons werden bei erfüllten Runtime-Abhängigkeiten automatisch ausgeblendet.
	- Guest-Status priorisiert fehlenden USB-Client klar als „Nicht installiert“.
	- Guest-Fensterhöhe moderat reduziert für die 4-Menü-Struktur (USB, Share, Einstellungen, Info), ohne Layout-Bruch.
- Installer/Uninstaller:
	- Optionale Runtime-Deinstallation robuster durch Registry-basierte Erkennung und Fallback-Uninstall-Aufrufe.
	- Host/Guest-Installertexte und Defaults konsolidiert; Startverhalten nach Installation präzisiert.

### Doku / Lizenz / Cleanup

- README auf `v2.3.5` aktualisiert (Features, Build-Beispiele, Runtime-/Lizenzhinweise).
- LICENSE um Third-Party-Runtime-Notice (`usbipd-win`, `usbip-win2`, `winfsp`) ergänzt und präzisiert.
- Hilfe-Texte in Host/Guest um Shared-Folder-/WinFsp-/Tool-Neustart-Kontext erweitert.

## v2.1.7

### Highlights

- Host und Guest nutzen jetzt konsistenten `Tool neu starten`-Flow mit kurzem Reload-Screen (analog Theme-Wechsel).
- Config-Bereiche in Host und Guest wurden um `Tool neu starten` ergänzt (neben `Speichern` / `Neu laden`).
- Guest-Info dokumentiert nun zusätzlich die externe Shared-Folder-Runtime `WinFsp` inkl. direktem Quellen-Link.

### Neu

- Guest Info:
	- Neue Karte `Externe Shared-Folder Runtime` mit Quelle `winfsp/winfsp`.
	- Neuer Button `WinFsp Quelle` im Info-Aktionsbereich.
- UI Konsistenz:
	- Einheitliches Icon/Label-Schema für alle `Tool neu starten` Buttons in Host und Guest.

### Verbessert

- Hilfe Host:
	- Config/Info-Beschreibung enthält jetzt den Reload-basierten `Tool neu starten` Ablauf.
- Hilfe Guest:
	- Shared-Folder-Hinweis enthält explizit die WinFsp-Abhängigkeit.
	- Einstellungs-Hinweis ergänzt um `Tool neu starten` mit Reload-Screen.

### Doku / Lizenzhinweise

- README um WinFsp-Abhängigkeit und konsolidierte Drittanbieter-/Lizenzhinweise erweitert.
- Lizenzdatei um klare Hinweise zu externen Runtimes (`usbipd-win`, `usbip-win2`, `winfsp`) ergänzt.

## v2.1.6

### Highlights

- Host- und Guest-Tray-Control-Center zeigen den USB-Runtime-Status klar über Farbpunkt und Statuszeile (grün/rot).
- Host-Network und Guest-USB wurden auf konsistente, moderne Status-Chips umgestellt.
- Guest-Info-Diagnose ist kompakter, mit sauber rechts platziertem Test-Button ohne Einfluss auf Zeilenabstände.

### Neu

- Host Tray USB:
	- Runtime-Statusanzeige für usbipd-Dienst (aktiv/inaktiv/nicht installiert).
	- Installationsbutton bei fehlendem usbipd-win mit Download aus dem offiziellen GitHub-Release.
- Host Network:
	- Adapter-Detailansicht mit Status-Chips für `Gateway` und `Default Switch`.
	- Badge-Farblogik für klare Semantik: `Gateway` grün, `Default Switch` orange.
- Guest Tray USB:
	- Runtime-Statusanzeige für usbip-win2 Client.
	- Installationsbutton bei fehlendem usbip-win2 mit Download aus dem offiziellen GitHub-Release.
	- Modusanzeige im USB-Bereich (Hyper-V Socket / IP-Fallback) als klickbare Status-Chips.
	- Modusabhängige Aktivierung/Anzeige des Host-IP-Eingabefelds.

### Verbessert

- USB-Bereich in Host und Guest kompakter aufgebaut (geringere Abstände, klarere Informationsdichte).
- USB-Aktionszustände orientieren sich stärker am Runtime-Status.
- Guest aktualisiert die Transportmodus-Anzeige nach Socket-Umschaltung unmittelbarer im UI.
- Guest-Themewechsel erhält die aktuell gewählte Menüseite nach dem Neustart der Oberfläche.
- Info-/Diagnosebereich im Guest auf reduzierte Textdichte und konsistente Abstände optimiert.

### Doku / Hilfe

- Host-Hilfe um aktuelle Hinweise zu Host-Network-Status-Chips ergänzt.
- Guest-Hilfe um aktuelle Hinweise zu Transport-Status-Chips und Live-Diagnose ergänzt.
- README auf v2.1.6 mit finalem Feature-Stand (Host/Guest UI, Build-Aufruf, Runtime-Hinweise) aktualisiert.

## v2.1.4

### Highlights

- Hyper-V Socket Diagnosepfad zwischen Guest und Host erweitert und robuster gemacht.
- Info-Bereiche in Host/Guest kompakter ausgerichtet; relevante Diagnoseanzeigen gezielt vereinfacht.
- Guest-Option „Beim Start auf Updates prüfen“ vollständig an Config und Startup-Verhalten angebunden.

### Neu

- Guest Info:
	- Neuer Diagnose-Button „Hyper-V Socket testen“ inkl. Ergebnisrückmeldung.
	- Diagnose-Button im Info-Bereich rechts ausgerichtet.
- Host Diagnose:
	- Host-seitiger Listener für Guest-Diagnose-Ack aus Hyper-V Socket Testpfad.
	- Zusätzliche Telemetrie-/Logfelder für Transportpfad (`hyperv` / `ip-fallback`).

### Verbessert

- Transport/Logging:
	- Transportpfad wird in Erfolg- und Fehlerfällen explizit protokolliert.
	- Hyper-V-first Verhalten mit klarerem IP-Fallback-Verhalten stabilisiert.
- UI/UX:
	- Info-Kopfzeilen in Host und Guest kompakter (Info + Version in einer Zeile).
	- Host-Info zeigt keinen überflüssigen Text „Fallback auf IP aktiv“ mehr.
	- Disconnect-Refresh im Guest bewusst verzögert (3 Sekunden) für stabilere Gerätezustände.
- Installer/Abhängigkeiten:
	- Host-App versucht fehlende usbipd-Runtime nicht mehr in-app nachzuinstallieren.
	- Optionaler Installer-Flow für USB-Runtimes klarer abgegrenzt.

### Behoben

- Guest Settings:
	- Checkbox „Beim Start auf Updates prüfen“ war deaktiviert und nicht gespeichert; jetzt persistiert und wirksam.
	- Startup-Updatecheck läuft nur noch, wenn die Option aktiv ist.
- Control Center:
	- Visuelles „Zappeln“ beim Öffnen/Positionieren reduziert.
- Diagnosepfad:
	- Mehrere Stabilitätsprobleme im Hyper-V Socket Testablauf und bei Fallback-Übergängen adressiert.

## v2.1.1

### Highlights

- Host und Guest nutzen jetzt ein konsistentes externes USB-Runtime-Modell mit optionaler Online-Installation im Setup.
- USB-Bereiche in UI und Control Center reagieren robuster auf fehlende Abhängigkeiten und zeigen klare Hinweise.
- Guest Control Center wurde für den USB-Bereich und das kompakte Tray-Verhalten sichtbar überarbeitet.

### Neu

- Host Installer (`HyperTool.iss`):
	- Optionale Aufgabe zur Installation von usbipd-win aus dem offiziellen Release-Feed.
	- Kein erzwungener Installationsabbruch, wenn die optionale Runtime nicht installiert wird.
- Guest Installer (`HyperTool.Guest.iss`):
	- Optionale Aufgabe zur Installation von usbip-win2 aus dem offiziellen Release-Feed.
	- Silent-Install mit expliziter Komponentenwahl ohne GUI-Komponente.
- In-App Update:
	- Verbesserte Asset-Auswahl für kombinierte Host/Guest-Releases über Installer-Hints.

### Verbessert

- Host USB:
	- Laufzeitprüfung und Deaktivierung der USB-Aktionen bei fehlendem usbipd.
	- Klarer Laufzeit-Hinweis im USB-Bereich.
	- Info-Bereich mit separatem Hinweis auf externe Quelle/Lizenzkontext.
- Guest USB:
	- Stabilere Statusdarstellung für verbundene Geräte.
	- Refresh/Connect/Disconnect im Guest Control Center sauber nebeneinander.
	- Dynamische Control-Center-Höhe abhängig vom Tray-Menü-Modus.
	- Bei deaktiviertem Tasktray-Menü nur Ein-/Ausblenden und Beenden.
- Guest Notifications:
	- Copy/Clear-Handling analog zum Host ergänzt.

### Behoben

- Guest Attach-Flow nutzt keine nicht unterstützte usbip-Option mehr.
- Mehrere Probleme bei USB-Status-Refresh direkt nach Disconnect reduziert.
- Control-Center-Button-Layout in Host/Guest konsistenter umgesetzt.

### Externe Abhängigkeiten

- Host USB Runtime: dorssel/usbipd-win
- Guest USB Runtime: vadimgrn/usbip-win2

Hinweis: HyperTool verweist auf diese externen Projekte und deren Lizenzen; Installationen erfolgen optional über die jeweiligen offiziellen Releases.

## v2.0.0

### Highlights

- Major Release: HyperTool ist jetzt vollständig auf WinUI 3 umgestellt.
- Modernisierte Oberfläche mit konsistentem Dark/Light Theme und verbessertem Window-/Tray-Verhalten.
- Build- und Release-Prozess auf WinUI-only vereinheitlicht (App + Installer über BAT-Skripte).
- Installer-Flow für self-contained WinUI vereinfacht (keine separate Runtime-Abfrage im Setup).

### Neu

- WinUI-3 App als neue Hauptanwendung (`HyperTool.Core` + `HyperTool.WinUI`).
- Überarbeiteter Theme-Flow mit sauberem Übergang und robustem Rebuild der Hauptansicht.
- Inno-Setup-Installer für self-contained WinUI-Auslieferung ohne zusätzliche Runtime-Installation.
- WinUI-Build-/Installer-Pipeline:
	- `build-host.bat`
	- `build-installer-host.bat`

### Verbessert

- Export-Statusanzeige überarbeitet:
	- Fortschritt wird nur angezeigt, wenn Hyper-V verlässliche Werte liefert.
	- Irreführende Sprünge/Flicker in Prozentanzeige und Progressbar reduziert.
	- Monotones Fortschrittsverhalten (kein Zurückspringen im Balken).
- Fehlerrobustheit bei Hyper-V Aktionen verbessert (klarere Meldungen für Berechtigung/PowerShell-Fehler).
- Repository auf WinUI-only bereinigt (Legacy-WPF-Struktur entfernt).

### Behoben

- Fataler Fehler beim VM-Export in der Speicherplatzprüfung (PowerShell-RegEx/UNC-Parsing) behoben.
- Export-Fehlerpfad stabilisiert: Ausnahmen führen nicht mehr zu hartem App-Abbruch.
- Mehrere Probleme im Theme-Wechsel-/Rebuild-Flow behoben.

### Kompatibilität

- Windows 10/11
- Hyper-V aktiviert
- Keine separate .NET Desktop Runtime-Installation über den HyperTool-Installer erforderlich (self-contained Build).

## v1.3.4

### Highlights

- App-/Tray-Icon überarbeitet: saubere Transparenz außen herum, ohne den alten HyperTool-Iconstil zu verlieren.
- Fensteroptik modernisiert: echte abgerundete App-Ecken und weicheres Gesamtbild.
- „Modern clean“-Feintuning für Buttons, VM-Chips und Hauptpanels umgesetzt.
- Dark- und Light-Theme weiterhin vollständig unterstützt und konsistent gehalten.

### Neu

- Fenster-Rundung technisch ergänzt:
	- Abgerundete Fensterecken werden jetzt aktiv auf Window-Ebene angewendet.
	- Rundung wird bei Größenänderung/Fensterstatus sauber aktualisiert.
	- Bei maximiertem Fenster wird korrekt auf rechteckige Darstellung zurückgeschaltet.
- Theme-Interaktion erweitert:
	- Neue Theme-Brushes für Button `Hover` und `Pressed` (Dark/Light).

### Verbessert

- Icons:
	- Altes `HyperTool.ico` bleibt Basis.
	- Äußerer Hintergrund/Verlauf wurde transparent gemacht für sauberere Darstellung in `.exe` und Tasktray.
	- Tray nutzt dediziertes Icon-Fallback, dadurch konsistenteres Erscheinungsbild bei kleinen Größen.
- UI/Design:
	- Rahmenfarben in Dark/Light subtil entschärft (weniger harte Kanten).
	- `ActionButton` mit modernerem Verhalten (Hover/Pressed/Disabled, besseres Padding, Hand-Cursor).
	- VM-Auswahl-Chips leicht modernisiert (Padding/Interaktionsfeedback), ohne Funktionsänderung.
	- Hauptpanel-Abstände und Card-Padding dezent erhöht für luftigere, ruhigere Oberfläche.

### Behoben

- Fensterkanten wirkten trotz vorheriger Anpassung teils noch eckig; jetzt echte Rundung der App-Form.
- Uneinheitliche Button-/Chip-Anmutung wurde vereinheitlicht, ohne bestehende Abläufe zu verändern.

### Kompatibilität

- Windows 10/11
- Hyper-V aktiviert
- .NET 8

## v1.3.3

### Highlights

- Netzwerkverwaltung auf Multi-NIC erweitert: adaptergenaues Verbinden/Trennen statt pauschal pro VM.
- Host-Network Popup deutlich ausgebaut: alle gefundenen Host-Adapter inkl. Netzwerkdetails und Badge-Infos.
- Snapshot-Bereich auf Baumansicht mit Status-Badges umgestellt (neuester/aktueller Stand).
- Export/Import-Workflow robuster: Prozent-Fortschritt, Speicherplatzprüfung, sicherer Import als neue VM.
- Tray-Verhalten feiner steuerbar: Show/Hide/Exit immer verfügbar, Fachmenüs optional ausblendbar.

### Neu

- Network-Tab:
	- Auswahl von VM-Netzwerkadaptern (pro Adapter eigener Switch-Connect/Disconnect).
	- `Host Network`-Button mit Detailfenster für Host-Adapter.
- Host-Network Fenster:
	- Anzeige von Adapter, Beschreibung, IP, Subnetz, Gateway und DNS.
	- Gateway-Badge für Adapter mit Gateway.
	- Default-Switch-Badge für Hyper-V Default Switch (ICS).
- Snapshots:
	- Tree-View (Parent/Child) statt flacher Liste.
	- Kennzeichnung `Neueste` und `Jetzt`.
- Config/VM:
	- Tray-Adapter pro VM konfigurierbar.
	- Umbenennen von VM-Adaptern inkl. Validierung (leer/ungültige Zeichen/Duplikate/identischer Name).
- Tray:
	- Neue Option `Tasktray-Menü aktiv`.
	- Bei deaktivierter Option bleiben `Show`, `Hide`, `Exit` sichtbar; `VM Aktionen`, `Switch umstellen`, `Aktualisieren` werden ausgeblendet.
- Easter Egg:
	- Klick auf Logo startet Rotation und spielt optionalen custom WAV-Sound.

### Verbessert

- Dropdown-Usability: Aufklappen über Klick auf die gesamte ComboBox-Fläche.
- Host-Adapter-Erkennung robuster inklusive Default-Switch-Fallbacks.
- Sortierung im Host-Network Fenster verbessert (Gateway-relevante Adapter zuerst).
- Dunkelmodus-/Kontextmenü-Darstellung in VM-Chips überarbeitet.
- Host-Network Fenstergröße angepasst, um unnötige Scrollbars zu reduzieren.

### Export/Import

- Export:
	- Fortschrittsanzeige in Prozent.
	- Vorabprüfung auf ausreichend freien Speicher im Zielpfad.
- Import:
	- Immer als neue VM (`-Copy -GenerateNewId`).
	- Zielpfad wird abgefragt.
	- Namenskonflikte werden automatisch mit Suffix aufgelöst.

### Behoben

- Default Switch (ICS) wurde im Host-Network Popup teilweise ohne Details angezeigt.
- Tray-Switching bei Multi-NIC ist jetzt auf konfigurierbaren Adapter begrenzbar.
- Mehrere UI-Konsistenzprobleme im Netzwerk-/Tray-/Darkmode-Bereich.

### Kompatibilität

- Windows 10/11
- Hyper-V aktiviert
- .NET 8

## v1.3.0

### Highlights

- Dark/Light Theme vollständig integriert und live umschaltbar.
- VM-Backup-Workflow erweitert: Export und Import direkt in der App.
- Snapshot-/Checkpoint-Handling deutlich robuster gemacht (inkl. Sonderzeichen-Fixes).
- Config- und Info-Bereiche visuell bereinigt und klarer strukturiert.
- Notification/Log-Bereich überarbeitet: dynamische Größe und direkter Zugriff auf Logdatei.

### Neu

- Theme-Umschaltung (`Dark` / `Light`) im Config-Bereich mit Live-Anwendung.
- VM Export/Import in der UI integriert.
- Config-Tab: Export arbeitet auf der aktuell ausgewählten VM; Default-VM wird separat gesetzt.
- Notification-Bereich: neuer Button `Logdatei öffnen`.

### Verbessert

- Config-UX überarbeitet (klarere Trennung von VM-Auswahl, Default-VM und Export-Flow).
- Network-Tab aufgeräumt (doppelte/irritierende Statuszeilen entfernt).
- Info-Tab „Links“-Bereich cleaner dargestellt.
- Notification-Bereich verhält sich beim Ein-/Ausklappen kontrollierter.
- Snapshot-Bezeichnungen konsistenter (`Restore` statt `Apply` in der UI).

### Behoben

- Snapshot-Create-Button blieb in bestimmten Zuständen fälschlich deaktiviert.
- Snapshot-Sektion konnte durch globales Busy-State blockiert werden; Checkpoint-Laden entkoppelt.
- Checkpoint-Erstellung robuster bei Production-Checkpoint-Problemen (Fallback-Handling).
- Checkpoint Restore/Delete mit Sonderzeichen im Namen funktioniert zuverlässig.
- Mehrere kleinere UI-Layout-Probleme (u. a. horizontale Scroll-Irritationen im Config-Bereich).

### Kompatibilität

- Windows 10/11
- Hyper-V aktiviert
- .NET 8

---

## v1.2.0

### Highlights

- Stabilitätsupdate für Startverhalten und Bindings
- Verbesserte Tray-Funktionen für VM-Alltag
- Update-/Installer-Flow für einfachere Aktualisierung aus der App
- Überarbeitete Dokumentation und Build-Prozess

### Neu

- Tasktray: neuer Menüpunkt "Konsole öffnen" pro VM
- Tasktray: "VM starten" öffnet direkt danach automatisch die VM-Konsole
- Tasktray: Menü schließt sich nach Aktionen zuverlässig
- Tasktray: Menüpunkt heißt jetzt "Aktualisieren" und lädt die Konfiguration neu
- Config: `ui.trayVmNames` erlaubt die Auswahl, welche VMs im Tray angezeigt werden
- Config/UI: neue Option `ui.startMinimized` inkl. Checkbox in der App
- Update: Default-Repo auf `KaKTools/HyperTool` umgestellt
- Update: semantischer Versionsvergleich (inkl. `v`-Prefix und Prerelease)
- Update: Installer-Asset-Erkennung in GitHub Releases (`.exe`/`.msi`)
- Info-Tab: neuer Button "Update installieren" (Download + Start des Installers)
- Build: neuer Installer-Workflow über `build-installer-host.bat` und Inno Setup Script (`installer/HyperTool.iss`)

### Verbessert

- Release-Prozess erweitert: `build-host.bat installer version=x.y.z` erstellt App + Setup
- README um Installer/Update-Prozess und neue Config-Felder ergänzt

### Behoben

- Tray-Usability verbessert: Menü bleibt nicht mehr offen nach VM-Aktion

### Kompatibilität

- Windows 10/11
- Hyper-V aktiviert
- .NET 8

### Hinweis zum Update

Für zukünftige Releases empfiehlt sich ein GitHub-Release mit angehängtem Installer-Asset (`HyperTool-Setup-<version>.exe`), damit die In-App-Funktion "Update installieren" automatisch genutzt werden kann.
