# 🛡️ MonitoringSystem — Система моніторингу активності користувачів

Корпоративна платформа для моніторингу та аналізу активності користувачів у мережі в реальному часі.

## 📋 Зміст

* [Архітектура](#архітектура)
* [Компоненти](#компоненти)
* [Вимоги та встановлення](#вимоги-та-встановлення)
* [Запуск на Windows](#запуск-на-windows)
* [Запуск на Linux](#запуск-на-linux)
* [Агент для звичайних користувачів](#агент-для-звичайних-користувачів)
* [Мережевий доступ](#мережевий-доступ)
* [Тестування у VirtualBox](#тестування-у-virtualbox)
* [Конфігурація](#конфігурація)
* [Тестові акаунти](#тестові-акаунти)
* [Git](#git)

\---

## Архітектура

```
                        ┌─────────────────────────────────┐
                        │     ASP.NET Core API :5000       │
                        │   JWT + SignalR + MongoDB         │
                        └──────────────┬──────────────────┘
                                       │ HTTP / WebSocket
              ┌────────────────────────┼────────────────────────┐
              ▼                        ▼                        ▼
   ┌──────────────────┐   ┌──────────────────────┐  ┌─────────────────────┐
   │  Blazor Web :5001│   │  MAUI Agent (Windows) │  │ Console Agent (Linux)│
   │  Адмін-панель    │   │  GUI + моніторинг     │  │ Terminal + моніторинг│
   └──────────────────┘   └──────────────────────┘  └─────────────────────┘
```

|Компонент|Технологія|Платформа|Порт|
|-|-|-|-|
|API|ASP.NET Core 10 + JWT + SignalR|Windows / Linux|5000|
|Веб-портал (адмін)|Blazor Server 10|Windows / Linux (браузер)|5001|
|Агент з GUI|**.NET MAUI 10**|**Windows**|—|
|Агент консольний|**.NET Console 10**|**Linux**|—|
|База даних|MongoDB 7.0|Windows / Linux|27017|

\---

## Компоненти

### 1\. MonitoringSystem.Api

Серверна частина. Обробляє всі запити від агентів та веб-порталу.

* JWT авторизація (Admin / User ролі)
* SignalR Hub для real-time сповіщень
* MongoDB: users, devices, activity\_logs, monitoring\_rules, rule\_violations

### 2\. MonitoringSystem.Web

Адмін-панель у браузері.

* **Admin**: Dashboard, Статистика, Користувачі, Пристрої, Правила, Порушення, Логи
* **User**: тільки /profile (своя статистика + зміна пароля)
* Real-time оновлення через SignalR

### 3\. MonitoringSystem.Maui *(Windows)*

GUI агент для Windows. Відкривається як звичайне вікно.

* Логін/пароль при кожному запуску
* Фоновий моніторинг кожні 30 секунд:

  * Заблоковані процеси (BlockedProcess правила)
  * Мережеві TCP з'єднання (порти 80/443)
  * Активні вкладки Chrome/Edge (через History SQLite)
* При порушенні: червоний банер + автоматичне розгортання вікна (Win32)
* Сторінка деталей: активні вкладки / процеси / з'єднання

### 4\. MonitoringSystem.Agent *(Linux)*

Консольний агент для Linux. Запускається в терміналі або як systemd сервіс.

* Той самий функціонал що і MAUI агент
* Логін у терміналі, при порушенні — кольоровий вивід + звуковий сигнал
* Підключення до того ж API через HTTP + SignalR



\---

## Вимоги та встановлення

### Сервер (API + Web) — Windows і Linux

|Компонент|Посилання|Версія|
|-|-|-|
|.NET 10 SDK|[dotnet.microsoft.com/download](https://dotnet.microsoft.com/download/dotnet/10.0)|10.0+|
|MongoDB Community|[mongodb.com/try/download/community](https://www.mongodb.com/try/download/community)|7.0+|

### Агент MAUI — тільки Windows

|Компонент|Команда|
|-|-|
|MAUI Workload|`dotnet workload install maui-windows`|

### Агент консольний — Linux (нічого додаткового)

Потрібен лише .NET 10 SDK, який вже встановлений для сервера.

\---

## Запуск на Windows

```powershell
# 1. Клонуй репозиторій
git clone https://github.com/YOUR\\\_USERNAME/MonitoringSystem.git
cd MonitoringSystem

# 2. MAUI workload (тільки для агента)
dotnet workload install maui-windows

# 3. Термінал 1 — API
cd MonitoringSystem.Api \\\&\\\& dotnet run

# 4. Термінал 2 — Веб-портал
cd MonitoringSystem.Web \\\&\\\& dotnet run
# Відкрий: http://localhost:5001

# 5. Термінал 3 — MAUI агент
cd MonitoringSystem.Maui
dotnet run -f net10.0-windows10.0.19041.0
```

\---

## Запуск на Linux

### Ubuntu / Debian

```bash
# 1. Встанови .NET 10 SDK
sudo apt update
sudo apt install -y dotnet-sdk-10.0

# 2. Встанови MongoDB
curl -fsSL https://www.mongodb.org/static/pgp/server-7.0.asc | \\\\
  sudo gpg -o /usr/share/keyrings/mongodb-server-7.0.gpg --dearmor
echo "deb \\\[ arch=amd64 signed-by=/usr/share/keyrings/mongodb-server-7.0.gpg ] \\\\
  https://repo.mongodb.org/apt/ubuntu noble/mongodb-org/7.0 multiverse" | \\\\
  sudo tee /etc/apt/sources.list.d/mongodb-org-7.0.list
sudo apt update \\\&\\\& sudo apt install -y mongodb-org
sudo systemctl start mongod \\\&\\\& sudo systemctl enable mongod

# 3. Клонуй
git clone https://github.com/YOUR\\\_USERNAME/MonitoringSystem.git
cd MonitoringSystem

# 4. Термінал 1 — API
cd MonitoringSystem.Api \\\&\\\& dotnet run

# 5. Термінал 2 — Web
cd MonitoringSystem.Web \\\&\\\& dotnet run
# Відкрий браузер: http://localhost:5001

# 6. Термінал 3 — Консольний агент
cd MonitoringSystem.Agent
echo '{"ApiUrl":"http://localhost:5000"}' > appsettings.json
dotnet run
# Введи логін: john.doe / User@123
```

### Fedora / RHEL

```bash
sudo dnf install -y dotnet-sdk-10.0
sudo dnf install -y mongodb-org
sudo systemctl start mongod
# решта команд — як Ubuntu
```

### Запуск як systemd сервіс (автозапуск)

```bash
sudo nano /etc/systemd/system/monitoring-agent.service
```

```ini
\\\[Unit]
Description=Monitoring System Agent
After=network.target

\\\[Service]
WorkingDirectory=/opt/MonitoringSystem/MonitoringSystem.Agent
ExecStart=/usr/bin/dotnet run
Environment=MONITORING\\\_API\\\_URL=http://localhost:5000
Restart=on-failure
User=monitoring

\\\[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl enable monitoring-agent
sudo systemctl start monitoring-agent
```

\---

## Агент для звичайних користувачів

### Windows (MAUI агент з GUI)

```powershell
# Встанови .NET 10 SDK + MAUI workload
dotnet workload install maui-windows

git clone --depth=1 https://github.com/YOUR\\\_USERNAME/MonitoringSystem.git
cd MonitoringSystem/MonitoringSystem.Maui

# Вкажи адресу сервера
# Відредагуй appsettings.json: { "ApiUrl": "http://IP\\\_СЕРВЕРА:5000" }

dotnet run -f net10.0-windows10.0.19041.0
```

### Linux (консольний агент)

```bash
# Встанови .NET 10 SDK
sudo apt install -y dotnet-sdk-10.0

git clone --depth=1 https://github.com/YOUR\\\_USERNAME/MonitoringSystem.git
cd MonitoringSystem/MonitoringSystem.Agent

echo '{"ApiUrl":"http://IP\\\_СЕРВЕРА:5000"}' > appsettings.json
dotnet run
```

\---

## Мережевий доступ

### Варіант A — Radmin VPN

1. Встанови [Radmin VPN](https://www.radmin-vpn.com/) на обидва пристрої
2. Сервер: **Network → Create Network** → запиши IP (наприклад `26.14.x.x`)
3. Клієнт: **Network → Join Network**
4. Відкрий порти на сервері:

```powershell
netsh advfirewall firewall add rule name="MonAPI" dir=in action=allow protocol=TCP localport=5000
netsh advfirewall firewall add rule name="MonWeb" dir=in action=allow protocol=TCP localport=5001
```

5. Використовуй Radmin IP замість `localhost`

### Варіант В — ngrok

```powershell
winget install ngrok
ngrok config add-authtoken ТВІЙ\\\_ТОКЕН  # dashboard.ngrok.com
ngrok http 5001  # Web
ngrok http 5000  # API (окремий термінал)
```

\---

\---

## Конфігурація

### `MonitoringSystem.Api/appsettings.json`

```json
{
  "MongoDB": {
    "ConnectionString": "mongodb://localhost:27017",
    "DatabaseName": "monitoring\\\_system"
  },
  "Jwt": {
    "Key": "YourSuperSecretKeyMinimum32Characters!@#",
    "Issuer": "MonitoringSystem",
    "Audience": "MonitoringClients"
  },
  "Urls": "http://0.0.0.0:5000"
}
```

### `MonitoringSystem.Web/appsettings.json`

```json
{
  "ApiBaseUrl": "http://localhost:5000/",
  "SignalRHubUrl": "http://localhost:5000/hubs/activity",
  "Urls": "http://0.0.0.0:5001"
}
```

> Для мережевого доступу замінити `localhost` на IP сервера.

### `MonitoringSystem.Maui/appsettings.json` та `MonitoringSystem.Agent/appsettings.json`

```json
{
  "ApiUrl": "http://localhost:5000"
}
```

> Замінити `localhost` на IP сервера (Radmin/Tailscale/ngrok).

\---

## Тестові акаунти

|Логін|Пароль|Роль|Відділ|
|-|-|-|-|
|admin|Admin@123|Admin|IT|
|john.doe|User@123|User|Marketing|
|jane.smith|User@123|User|HR|
|bob.wilson|User@123|User|Sales|

> ⚠️ Змінити паролі після першого запуску через веб-портал!

\---

## Ліцензія

MIT

