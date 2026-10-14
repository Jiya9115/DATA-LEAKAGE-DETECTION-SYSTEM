# DLDS — Data Leakage Detection System

A cross-platform ASP.NET Core MVC web application that monitors selected folders in real
time, detects file creation/modification/deletion/rename events, scores each event for
risk, raises alerts on high-risk activity, and keeps a full audit trail — built to run on
macOS with Visual Studio / Visual Studio Code.

> **What this system is (and isn't):** DLDS surfaces *Potential Data Leakage Activity* and
> *Suspicious File Activity* for human review. It does not, and cannot, prove that an actual
> data leak occurred, and it does not guarantee 100% leak prevention.

---

## 1. Features

- Real-time folder monitoring via `FileSystemWatcher`, running inside a `BackgroundService`
  so it never blocks the web app
- Detects Created / Deleted / Modified / Renamed events, with debouncing of duplicate OS
  notifications and graceful handling of missing/inaccessible folders
- Configurable, explainable risk scoring (`RiskAssessmentService`) — administrator-defined
  `RiskRule`s first, then built-in heuristics (rapid deletions, large files, sensitive
  extensions, executables/scripts)
- Automatic alerts for High/Critical risk events
- Dashboard with live-refreshing stats and Chart.js charts (risk distribution, event type,
  7-day trend)
- File Activity Explorer with search, filtering (event type, risk level, folder, date), and
  pagination
- Alert Center grouped by severity, with read/unread state
- Risk Rule Builder for administrators
- Report Center with date-range presets, filtering, and CSV export
- Audit log timeline (logins, folder changes, rule edits, reports, alerts)
- ASP.NET Core Identity authentication with Administrator / User roles
- Dark/light theme (CSS variables, saved in `localStorage`), responsive layout,
  accessible risk badges (color **and** text label)
- Seeded demo data clearly flagged `IsSampleData = true`

---

## 2. Technology Stack

- ASP.NET Core MVC (.NET 8), C#
- Entity Framework Core + SQL Server
- ASP.NET Core Identity (password hashing, roles, cookie auth)
- Bootstrap 5, vanilla JavaScript, Chart.js (via CDN)
- `FileSystemWatcher` + `IHostedService`/`BackgroundService`

No Windows-only APIs, Windows Forms/WPF, IIS-specific features, or the Windows Registry are
used anywhere in this project — it is fully cross-platform.

---

## 3. Project Structure

```
DLDS/
├── Controllers/        HomeController, DashboardController, MonitoringController,
│                        FileEventsController, AlertsController, ReportsController,
│                        RiskRulesController, AuditLogsController, SettingsController,
│                        AccountController
├── Models/              FileEvent, MonitoredFolder, Alert, RiskRule, AuditLog,
│                        AppSetting, ApplicationUser
├── Data/                ApplicationDbContext, DbInitializer (seed data)
├── Services/            FileMonitoringService, RiskAssessmentService, AlertService,
│                        ReportService
├── ViewModels/          Dashboard/FileEvent/Monitoring/Report/RiskRule/Account/Settings VMs
├── Views/                Razor views, organized by controller, plus Views/Shared
├── wwwroot/             css/site.css (design system), js/site.js
├── Database/schema.sql  Reference SQL script for the DLDS-specific tables
├── Program.cs
├── appsettings.json / appsettings.Development.json
├── DLDS.csproj / DLDS.sln
└── .gitignore
```

---

## 4. Prerequisites

1. **.NET 8 SDK** — install from https://dotnet.microsoft.com/download (works natively on
   Apple Silicon and Intel Macs).
   ```bash
   dotnet --version   # should print 8.x
   ```
2. **A SQL Server instance reachable from your Mac** — see Section 5.
3. Visual Studio for Mac / VS Code (with the C# Dev Kit extension) / any editor.
4. The EF Core CLI tool:
   ```bash
   dotnet tool install --global dotnet-ef
   ```

---

## 5. SQL Server on macOS

SQL Server does not run natively on macOS, so use one of these two options.

### Option A — Docker (recommended for local development)

```bash
# Pull and run SQL Server 2022 in a container
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=YourStrong!Passw0rd" \
  -p 1433:1433 --name dlds-sql --hostname dlds-sql \
  -d mcr.microsoft.com/mssql/server:2022-latest

# Confirm it's running
docker ps
```

Your connection string host becomes `localhost,1433` with user `sa` and the password you set
above.

### Option B — Remote SQL Server instance

Point the connection string at any reachable SQL Server (Azure SQL, a shared department
server, etc.) using its host, port, database name, and credentials.

---

## 6. Configure the Connection String

**Never commit real credentials.** For local development, put your real connection string in
`appsettings.Development.json` (already listed in `.gitignore`) or, preferably, an
environment variable:

```bash
export ConnectionStrings__DefaultConnection="Server=localhost,1433;Database=DLDS;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;"
```

`appsettings.json` ships only with an obvious placeholder password so the repository never
contains a working secret.

---

## 7a. Alternative: Run Everything in Docker (app + SQL Server together)

If you'd rather not install the .NET SDK locally, `Dockerfile` and `docker-compose.yml` are
included so the whole stack runs in containers.

**Important — do this once on your host machine first**, because the container image has no
EF CLI tool installed and migrations must already exist before the image is built:

```bash
dotnet tool install --global dotnet-ef   # only if you haven't already
dotnet restore
dotnet ef migrations add InitialCreate
```

Then bring everything up:

```bash
docker compose up --build
```

This starts SQL Server and the web app together. `Program.cs` applies the migrations and
seeds demo data automatically on container startup — no extra `dotnet ef database update`
step needed in this path. Once it's up, open **http://localhost:8080**.

**Folder monitoring path note:** the web app runs inside a Linux container, not directly on
macOS, so it can't see arbitrary Mac paths like `/Users/yourname/Documents/...`. The compose
file mounts your Mac's `~/Documents` into the container at `/monitored/Documents`, so when
adding a monitored folder in the UI, use a path like `/monitored/Documents/SensitiveData`
(create that subfolder in Finder first) — file changes you make there in Finder/Terminal on
your Mac will be visible to the watcher running inside the container.

To stop everything: `docker compose down` (add `-v` to also delete the SQL Server data
volume and start fresh next time).

---

## 7. Restore Packages, Create Migrations, and Build


```bash
cd DLDS
dotnet restore
```

This repository does not ship a pre-generated `Migrations/` folder — EF Core migrations are
generated from the compiled model on your own machine (the standard EF workflow), so create
the initial migration once after restoring packages:

```bash
dotnet ef migrations add InitialCreate
```

Then create/update the database:

```bash
dotnet ef database update
```

This creates both the ASP.NET Core Identity tables (`AspNetUsers`, `AspNetRoles`, etc.) and
the DLDS tables (`FileEvents`, `MonitoredFolders`, `Alerts`, `RiskRules`, `AuditLogs`,
`AppSettings`). If you ever need to inspect or manually recreate just the DLDS-specific
tables without the EF CLI, `Database/schema.sql` is provided as a reference/fallback script
(e.g. `sqlcmd -S localhost,1433 -U sa -P '<password>' -i Database/schema.sql`) — Identity
tables are not duplicated there since EF creates them automatically.

Build:

```bash
dotnet build
```

---

## 8. Run the Application

```bash
dotnet run
```

The console will print the local URL (typically `https://localhost:5001` or similar). On
first run, `Program.cs` automatically applies pending migrations and seeds:

- Two demo accounts:
  - **Administrator** — `admin@dlds.local` / `Admin@12345`
  - **User (Security Analyst)** — `analyst@dlds.local` / `Analyst@12345`
- Sample risk rules, a sample monitored folder, sample file events/alerts, and sample audit
  log entries — all flagged `IsSampleData = true` so they're easy to identify and remove.

> Change or remove the demo credentials before any real deployment.

---

## 9. Add a Monitored Folder

1. Sign in and open **Monitored Folders** in the sidebar.
2. Click **Add Folder**, give it a name, and enter a real, existing macOS path, e.g.
   `/Users/yourname/Documents/SensitiveData`.
3. Leave **Start monitoring immediately** checked (or start it later from the folder card).

---

## 10. Test File Creation / Modification / Deletion / Renaming

With monitoring active on that folder, from Terminal:

```bash
cd ~/Documents/SensitiveData
echo "test" > report.txt        # Created
echo "more text" >> report.txt  # Modified
mv report.txt renamed.txt       # Renamed
rm renamed.txt                  # Deleted
```

Each action should appear within a few seconds in **File Events** and, if scored High/
Critical, in **Security Alerts**.

---

## 11. View Detected Events, Risk Analysis, and Alerts

- **Overview** (Dashboard) — summary cards, charts, and a live-refreshing recent-events feed
- **File Events** — full searchable/filterable/paginated table; click a row for a detailed
  risk breakdown and recommended action
- **Security Alerts** — grouped by severity (Critical/High/Medium/Low), with mark-as-read
- **Risk Rules** — create/edit/toggle/delete the rules the Risk Assessment Service evaluates

---

## 12. Generate Reports

Open **Reports**, choose a date range (Today / Last 7 days / Last 30 days / Custom) and
optional filters, click **Generate Report** for an on-screen summary, or **Export CSV** to
download the matching raw events.

---

## 13. How Risk Detection Works

1. When a file event fires, `FileMonitoringService` debounces duplicate OS notifications and
   hands the event to `RiskAssessmentService`.
2. Active `RiskRule`s are checked first — the most specific matching rule (by event type,
   extension, and minimum size) wins.
3. If no rule matches, built-in heuristics apply: rapid repeated deletions in the same
   folder, large files, sensitive extensions (`.pdf`, `.docx`, `.xlsx`, `.sql`, `.zip`,
   `.pem`, etc.), and executable/script files (`.exe`, `.sh`, `.ps1`, ...).
4. Every result includes a plain-language, explainable reason (never a bare score).
5. `AlertService` automatically raises an `Alert` for High/Critical results.

---

## 14. Project Demonstration Script (for a viva)

1. Log in as `admin@dlds.local`.
2. Open the Dashboard — review the seeded sample data and charts.
3. Go to **Monitored Folders** → **Add Folder**, pick a local test folder, start monitoring.
4. In Terminal: create → modify → rename → delete a test file in that folder (Section 10).
5. Return to the Dashboard / **File Events** and show the four recorded events.
6. Open one event's details to show its risk score and explainable reason.
7. Open **Security Alerts** to show any auto-generated alerts.
8. Open **Audit Logs** to show the timeline of actions taken during the demo.
9. Open **Reports**, generate a "Today" report, and export it as CSV.
10. Optionally open **Risk Rules** to show/edit a configurable rule and repeat steps 4–6 to
    demonstrate the rule taking effect.

---

## 15. Security Notes

- Passwords are always hashed by ASP.NET Core Identity — plain-text passwords are never
  stored.
- All state-changing actions (`Add`, `Start`, `Stop`, `Delete`, `Save`, `MarkRead`, etc.) are
  `[HttpPost]` + `[ValidateAntiForgeryToken]`.
- All database access goes through EF Core (parameterized queries) — no raw SQL string
  concatenation.
- Role-gated actions (deleting a folder, managing risk rules, changing settings) require the
  `Administrator` role via `[Authorize(Roles = ...)]`.
- Unhandled exceptions are routed to a friendly error page; stack traces are never shown to
  end users outside the Development environment.
- Secrets (connection strings, etc.) are read from configuration/environment variables, never
  hard-coded — `appsettings.json` ships only placeholder values.

---

## 16. Known Limitations / Future Enhancements

- Migrations are generated locally via the EF CLI rather than shipped pre-built, since a
  migration is tied to the exact compiler/tooling versions on the machine that creates it —
  this is the standard, supported EF Core workflow.
- The Live Monitor feed uses lightweight polling (`fetch` every few seconds) rather than
  SignalR, per the "keep it simple and reliable" guidance — SignalR could be swapped in later
  without changing the data model.
- PDF report export, per-user notification preferences, and a full global search across all
  entities are natural next additions.
- `FileSystemWatcher`'s `NotifyFilters.LastWrite` can occasionally fire more than one
  `Changed` event for a single save; this is mitigated by the debounce window in
  `FileMonitoringService` but extremely rapid successive saves could still coalesce.
