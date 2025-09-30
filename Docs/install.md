# 🛠 INSTALLATION.md

## ASP.NET Core (.NET 9) Webanwendung – Installation als Dienst (Linux) & IIS (Windows)

---

## 📦 Voraussetzungen

### Allgemein
- .NET 9 SDK & Runtime installiert (nur für Windows-Installation oder Linux ohne Self-Contained Build)
- Zugriff auf die veröffentlichten Dateien der Webanwendung
- Konfigurierte appsettings.json oder Umgebungsvariablen

### Projektspezifisch (FinanceManager)
- Zwingend: `Jwt:Key` (geheimer Schlüssel zur Token-Signierung). Ohne diesen startet die App nicht.
- Zwingend: `AlphaVantage:ApiKey` (für Kursabruf). Ohne Schlüssel schlägt der Start fehl (Service wird bei DI erstellt).
- Optional: `Api:BaseAddress` (falls die App hinter einem Proxy läuft und der interne HttpClient absolute Basis benötigt).
- Für Reverse-Proxy/TLS-Termination: `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (damit `Request.IsHttps` korrekt erkannt wird → Secure-Cookie `fm_auth`).

Hinweise:
- Datenbankmigrationen werden beim Start automatisch ausgeführt (EF Core Migrate). Der Datenbankordner muss für den App-User schreibbar sein.
- Hintergrunddienste laufen im Prozess: Preis-Worker (stündlich), Monats‑Reminder‑Scheduler, BackgroundTaskRunner.

---

## 🐧 Installation unter Linux als Systemd-Dienst

### 1. Anwendung lokal veröffentlichen

Auf dem Entwicklungsrechner oder CI-Server:

```
dotnet publish -c Release -r linux-x64 --self-contained -o ./publish
```

💡 Mit `--self-contained` wird die .NET Runtime mitgeliefert. Ohne diesen Parameter muss .NET 9 auf dem Zielsystem installiert sein.

### 2. Dateien auf Linux übertragen

```
scp -r ./publish user@linuxserver:/var/www/financemanager
```

### 3. Systemd-Dienst erstellen

Datei `/etc/systemd/system/financemanager.service`:

```
[Unit]
Description=FinanceManager (.NET 9, Blazor Server)
After=network.target

[Service]
WorkingDirectory=/var/www/financemanager
ExecStart=/var/www/financemanager/FinanceManager.Web
Restart=always
RestartSec=10
SyslogIdentifier=financemanager
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
# URLs nur intern binden, wenn Reverse Proxy genutzt wird (z. B. Nginx)
Environment=ASPNETCORE_URLS=http://127.0.0.1:5000
# Erforderliche App-Settings
Environment=Jwt:Key=__SET_A_RANDOM_LONG_SECRET__
Environment=ConnectionStrings:Default=Data Source=/var/www/financemanager/app.db
Environment=AlphaVantage:ApiKey=__YOUR_ALPHA_VANTAGE_KEY__
# Forwarded Headers für TLS am Reverse Proxy (setzt Request.IsHttps richtig)
Environment=ASPNETCORE_FORWARDEDHEADERS_ENABLED=true

[Install]
WantedBy=multi-user.target
```

🔁 Falls du kein Self-Contained Build nutzt:

```
ExecStart=/usr/bin/dotnet /var/www/financemanager/FinanceManager.Web.dll
```

### 4. Dienst aktivieren und starten

```
sudo systemctl daemon-reexec
sudo systemctl enable financemanager
sudo systemctl start financemanager
```

### 5. Optional: Nginx als Reverse Proxy (für TLS & WebSockets)

Blazor Server benötigt WebSocket‑Weiterleitung. Beispiel `/etc/nginx/sites-available/financemanager`:

```
server {
    listen 80;
    server_name example.com;

    # TLS (optional, hier nur Redirect-beispiel)
    # return 301 https://$host$request_uri;
}

server {
    listen 443 ssl http2;
    server_name example.com;

    ssl_certificate     /etc/letsencrypt/live/example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/example.com/privkey.pem;

    location / {
        proxy_pass         http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header   Upgrade $http_upgrade;
        proxy_set_header   Connection "upgrade";
        proxy_set_header   Host $host;
        proxy_set_header   X-Forwarded-Proto $scheme;
        proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_read_timeout 60m; # für langlebige Blazor-Verbindungen
    }
}
```

Aktivieren und neu laden:

```
sudo ln -s /etc/nginx/sites-available/financemanager /etc/nginx/sites-enabled/
sudo nginx -t && sudo systemctl reload nginx
```

Dateirechte: Stelle sicher, dass `/var/www/financemanager` für `www-data` lesbar ist und der DB‑Ordner schreibbar (z. B. `chown -R www-data:www-data /var/www/financemanager`).

---

## 🪟 Installation unter Windows Server mit IIS

### 1. Anwendung veröffentlichen

```
dotnet publish -c Release -o "C:\inetpub\financemanager"
```

### 2. IIS vorbereiten

- Rolle „Webserver (IIS)“ installieren
- Feature „ASP.NET Core Module“ aktivieren
- .NET Hosting Bundle für .NET 9 installieren
- WebSocket‑Protokoll aktivieren (Blazor Server erfordert WebSockets)

### 3. Neue Website in IIS erstellen

- Pfad: `C:\inetpub\financemanager`
- Port: z. B. 8080 oder 80
- App-Pool: .NET CLR = „No Managed Code“, Startmodus = „Immer gestartet“

### 4. Berechtigungen setzen

```
icacls "C:\inetpub\financemanager" /grant "IIS_IUSRS:(OI)(CI)M"
```

(M = Modify, damit die SQLite‑DB geschrieben werden kann.)

### 5. Web.config prüfen

```
<configuration>
  <system.webServer>
    <handlers>
      <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified"/>
    </handlers>
    <aspNetCore processPath="dotnet" arguments=".\FinanceManager.Web.dll" stdoutLogEnabled="false" stdoutLogFile=".\logs\stdout" hostingModel="inprocess"/>
  </system.webServer>
  <system.web>
    <serverRuntime uploadReadAheadSize="1048576" />
    <httpRuntime maxRequestLength="1048576" />
  </system.web>
</configuration>
```

Hinweise:
- Große Uploads: Die App erlaubt bis 1 GB (`MultipartBodyLengthLimit`). In IIS ggf. zusätzlich Request‑Limits erhöhen (Request Filtering → `maxAllowedContentLength`).
- Umgebung/Secrets in IIS setzen:
  - Systemweite Umgebungsvariablen (empfohlen): `Jwt:Key`, `ConnectionStrings:Default`, `AlphaVantage:ApiKey`.
  - Oder im App‑Pool/Website unter „Konfiguration bearbeiten“ → Umgebungsvariablen.

---

## 🔧 AppSettings – Beispiele (Production)

### Umgebungsvariablen (Linux systemd)

```
Environment=Jwt:Key=__SET_A_RANDOM_LONG_SECRET__
Environment=ConnectionStrings:Default=Data Source=/var/www/financemanager/app.db
Environment=AlphaVantage:ApiKey=__YOUR_ALPHA_VANTAGE_KEY__
Environment=ASPNETCORE_URLS=http://127.0.0.1:5000
Environment=ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
```

### appsettings.Production.json (optional)

```json
{
  "Logging": {
    "LogLevel": { "Default": "Information", "Microsoft": "Warning" }
  },
  "FileLogging": {
    "Enabled": true,
    "Path": "logs/app-.log",
    "Rolling": "Day"
  },
  "Jwt": { "Key": "__SET_A_RANDOM_LONG_SECRET__" },
  "ConnectionStrings": { "Default": "Data Source=/var/www/financemanager/app.db" },
  "AlphaVantage": { "ApiKey": "__YOUR_ALPHA_VANTAGE_KEY__" }
}
```

---

## ✅ Test & Troubleshooting

- Linux Service‑Logs: `sudo journalctl -u financemanager -f`
- Nginx Logs: `/var/log/nginx/error.log`
- Windows: Event Viewer → Windows Logs → Application
- Browser: http://localhost:5000 (Kestrel) oder https://example.com (über Nginx/IIS)
- Datenbank: Beim ersten Start wird die SQLite‑DB erstellt und migriert. Schreibrechte prüfen, falls Fehler.
- AlphaVantage Limits: Bei API‑Limitierungen pausiert der Preis‑Worker automatisch (Log‑Warnung). Gültigen API‑Key sicherstellen.

---

## ℹ️ Ergänzende Projekthinweise

- Authentifizierung: JWT im HttpOnly‑Cookie `fm_auth`. Hinter Reverse Proxy `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` setzen, damit `Secure` korrekt angewendet wird.
- Internationalisierung: de/en; Benutzerkultur aus Profil/JWT.
- Hintergrundprozesse: Scheduler (Monats‑Reminder), Kurs‑Worker (stündlich), BackgroundTaskRunner.
- Uploads: Standardlimit für Multipart auf 1 GB erhöht.


