# 🛠 INSTALLATION.md

## ASP.NET Core (.NET 9) Webanwendung – Installation als Dienst (Linux) & IIS (Windows)

---

## 📦 Voraussetzungen

### Allgemein
- .NET 9 SDK & Runtime installiert (nur für Windows-Installation oder Linux ohne Self-Contained Build)
- Zugriff auf die veröffentlichten Dateien der Webanwendung
- Konfigurierte appsettings.json oder Umgebungsvariablen

---

## 🐧 Installation unter Linux als Systemd-Dienst

### 1. Anwendung lokal veröffentlichen

Auf dem Entwicklungsrechner oder CI-Server:

dotnet publish -c Release -r linux-x64 --self-contained -o ./publish

💡 Mit --self-contained wird die .NET Runtime mitgeliefert. Ohne diesen Parameter muss .NET 9 auf dem Zielsystem installiert sein.

### 2. Dateien auf Linux übertragen

scp -r ./publish user@linuxserver:/var/www/myapp

### 3. Systemd-Dienst erstellen

Datei /etc/systemd/system/myapp.service:

[Unit]
Description=ASP.NET Core Web App (.NET 9)
After=network.target

[Service]
WorkingDirectory=/var/www/myapp
ExecStart=/var/www/myapp/MyApp
Restart=always
RestartSec=10
SyslogIdentifier=myapp
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target

🔁 Falls du kein Self-Contained Build nutzt:
ExecStart=/usr/bin/dotnet /var/www/myapp/MyApp.dll

### 4. Dienst aktivieren und starten

sudo systemctl daemon-reexec
sudo systemctl enable myapp
sudo systemctl start myapp

---

## 🪟 Installation unter Windows Server mit IIS

### 1. Anwendung veröffentlichen

dotnet publish -c Release -o "C:\inetpub\myapp"

### 2. IIS vorbereiten

- Rolle „Webserver (IIS)“ installieren
- Feature „ASP.NET Core Module“ aktivieren
- .NET Hosting Bundle für .NET 9 installieren

### 3. Neue Website in IIS erstellen

- Pfad: C:\inetpub\myapp
- Port: z. B. 8080 oder 80
- App-Pool: .NET CLR = „No Managed Code“, Startmodus = „Immer gestartet“

### 4. Berechtigungen setzen

icacls "C:\inetpub\myapp" /grant "IIS_IUSRS:(OI)(CI)RX"

### 5. Web.config prüfen

<configuration>
  <system.webServer>
    <handlers>
      <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified"/>
    </handlers>
    <aspNetCore processPath="dotnet" arguments=".\MyApp.dll" stdoutLogEnabled="false" stdoutLogFile=".\logs\stdout" hostingModel="inprocess"/>
  </system.webServer>
</configuration>

---

## ✅ Test & Troubleshooting

- Linux: sudo journalctl -u myapp -f
- Windows: Event Viewer → Windows Logs → Application
- Browser: http://localhost:5000 (Linux) oder http://localhost (Windows IIS)


