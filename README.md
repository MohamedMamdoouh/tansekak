# Tansekak

**Vibe coded project** — built iteratively with AI-assisted development.

Tansekak is an admission eligibility checker for Egyptian Thanaweya Amma graduates. Students pick a track and total score; the app lists faculties they are eligible for against the **current published** admission year.

Students can also look up an imported Thanaweya result by seating number once the current year has results.

Results are **indicative only**. Egypt’s official coordination portal decides final placement.

**Current-stage docs**

| Document                   | Role                       |
| -------------------------- | -------------------------- |
| [docs/PRD.md](docs/PRD.md) | As-built product spec      |
| [docs/API.md](docs/API.md) | HTTP contract, error codes |
| [docs/DEPLOY.md](docs/DEPLOY.md) | Render + Neon checklist |

---

## Features

### Public (RTL Arabic)

Public chrome brand is **Tansekak**. API `appName` is `tansekak`. Admin chrome is **Admin**. A signed-in administrator also sees a dashboard link in the public header.

| Feature            | Route               | Description                                       |
| ------------------ | ------------------- | ------------------------------------------------- |
| Landing            | `/`                 | Overview and entry points                         |
| College prediction | `/predict`          | Track + score form (navigates to `/results`)      |
| Prediction results | `/results`          | Eligible faculties, client-side search, load-more |
| Thanaweya lookup   | `/thanaweya-result` | Result by seating number (current year)           |
| Coordination guide | `/guide`            | Static FAQ                                        |
| Developer profile  | `/designer`         | Credits                                           |

**Prediction**

- `/predict` collects track + score and navigates to `/results?track=&score=`.
- `/results` calls `POST /api/admission/predict` with `pageSize: 20`.
- Eligible only: `student score >= cutoff`.
- Current published year, matching track, and faculty `AllowedTracks`.
- Sorted closest cutoff first (`abs(score − cutoff)`).
- Unlimited load-more. API default page size is 10, max 100.
- Search filters **already loaded** university/faculty names (client-side).

**Tracks:** `Science`, `Mathematics`, `Literature`. Each is distinct for prediction, cutoffs, and faculty eligibility.

**Thanaweya lookup**

- Calls `GET /api/thanaweya-results/{seatingNo}` for the current year (60 requests/minute per IP).
- Public lookup opens when the current year has imported results (`studentResultsAvailable` on `GET /api/config`).
- Excel import: `.xlsx` up to 50 MB, columns `seating_no`, `arabic_name`, `total_degree` (row 1 headers, any order).

### Admin

Sign in at `/admin/login` (not linked from the public site until an administrator is already signed in).

| Page             | Route           | Description                                                                                          |
| ---------------- | --------------- | ---------------------------------------------------------------------------------------------------- |
| Dashboard        | `/admin`        | Current year plus governorate, faculty-type, and student-result counts                               |
| Admission years  | `/admin/years`  | Create, edit, delete the single admission year                                                       |
| Cutoffs          | `/admin/cutoffs`| CRUD for the **current** year                                                                        |
| Import cutoffs   | `/admin/import` | One Markdown file per track; each import replaces that track for the current year                    |
| Import results   | `/admin/import-results` | Excel (`.xlsx`, max 50 MB); replaces all student results for the current year              |

The cutoff import page blocks navigation with a progress overlay until the upload finishes or you confirm leaving.

**Development admin** (rejected in Production): `admin@tansekak.local` / `Admin@12345`. Production uses `AdminSeed__Email` and `AdminSeed__Password`.

**API-only** (no UI): governorates, universities, faculties, university–faculties.

---

## Tech stack

| Layer         | Technology                                                       |
| ------------- | ---------------------------------------------------------------- |
| Backend       | ASP.NET Core 10, EF Core 10, PostgreSQL (Npgsql), Identity cookies |
| Frontend      | Angular 19 standalone, RTL                                       |
| Validation    | FluentValidation                                                 |
| Tests         | xUnit (97 tests across 4 projects; Postgres tests need Docker)   |
| CI            | GitHub Actions (Angular production build + `dotnet test` solution) |
| Deploy        | Docker monolith on Render, Neon Postgres                         |

Clean Architecture: `Domain` → `Application` (services + DTOs, no MediatR, no repositories) → `Infrastructure` → `Api`. Integer business IDs use `EntityIdAllocator`; Identity tables use PostgreSQL identity columns.

**Removed from earlier stages:** async `ImportJob` queue, Cloudflare R2 storage, track-rank page.

---

## Project structure

```
Tansekak/
├── client/                      # Angular 19 SPA
├── SeededData/                  # Catalog JSON (copied to API output as SeedData/)
│   └── cutoffs/                 # Markdown used by startup bootstrap and admin import
├── docs/
├── tests/
│   ├── Tansekak.Application.Tests/
│   ├── Tansekak.Infrastructure.Tests/
│   └── Tansekak.Api.Tests/
├── src/Tansekak.{Api,Application,Domain,Infrastructure}/
├── Dockerfile
└── Tansekak.sln
```

---

## Prerequisites

**Local:** .NET 10 SDK, Node.js 20+, PostgreSQL 16+ (or Neon).

**Production:** Neon Postgres, Render web service.

---

## Quick start (local)

### 1. Database

Default connection in `src/Tansekak.Api/appsettings.Development.json`:

```
Host=localhost;Port=5432;Database=Tansekak;Username=postgres;Password=postgres
```

### 2. API

```powershell
cd src/Tansekak.Api
dotnet run
```

- `http://localhost:5080`
- Health: `/health` → `{ "status": "healthy" }`
- OpenAPI (Development only): `/openapi/v1.json` (no Swagger UI)
- Startup (non-test): production guards, EF migrations, catalog seed if empty, faculty `AllowedTracks` repair, Mathematics track repair on existing student rows, cutoff bootstrap for empty tracks, bootstrap year **2027** (max 320, current), admin user

### 3. Frontend

```powershell
cd client
npm install
npm start
```

- `http://localhost:4200` (proxies `/api` and `/health` to `:5080`)

### 4. Cutoffs

On every startup, tracks with **zero** cutoffs in the current year are imported from `SeededData/cutoffs/` (build output `SeedData/cutoffs/`). Existing cutoffs are never overwritten.

Local `dotnet run` copies those files, so a fresh database typically has Science, Mathematics, and Literature cutoffs after first boot.

| File | Track |
| --- | --- |
| `SeededData/cutoffs/science-2026.md` | Science |
| `SeededData/cutoffs/mathematics-2026.md` | Mathematics |
| `SeededData/cutoffs/literature-2026.md` | Literature |

`2026` is the official source cycle. Rows attach to the **current published year** (bootstrap **2027**). Re-import or replace a track from `/admin/import`.

Docker images may omit these Markdown files (`.dockerignore` excludes `*.md`). See [docs/DEPLOY.md](docs/DEPLOY.md).

---

## Production

See [docs/DEPLOY.md](docs/DEPLOY.md).

| Layer     | Provider                                                        |
| --------- | --------------------------------------------------------------- |
| App + SPA | Render Docker web service (`0.0.0.0:$PORT`)                     |
| Database  | Neon (`ConnectionStrings__DefaultConnection` or `DATABASE_URL`) |

Do **not** set `Frontend__Origin` in production — SPA and API share one origin.

---

## Configuration

| Key                                   | Purpose                                               |
| ------------------------------------- | ----------------------------------------------------- |
| `ConnectionStrings:DefaultConnection` | Npgsql keyword string, or use `DATABASE_URL`          |
| `Tansekak:AppName`                    | Returned by `/api/config`                             |
| `AdminSeed:Email` / `Password`        | First-run admin                                       |
| `Frontend:Origin`                     | CORS for local Angular (`http://localhost:4200`) only |
| `Testing:SkipStartupSeed`             | Test host only — skips startup seeding                |
| `Testing:UseSqlite`                   | Test host only — in-memory SQLite instead of Npgsql   |

Production rejects localhost connection strings and the dev admin credentials. No object-storage env vars are required.

---

## Database

Seeded once when `Governorates` is empty: `Governorates.json`, `Universities.json`, `Faculties.json`, `UniversityFaculties.json`, plus admission year **2027**. After that the database is the source of truth for catalog rows.

Every startup still:

- Repairs `Faculties.AllowedTracks` from seed JSON when they diverge.
- Imports seed Markdown for any current-year track that has zero cutoffs.

Entities: Governorate, University (`Public` / `Institute`), Faculty (`AllowedTracks`), UniversityFaculty, AdmissionYear, AdmissionCutoff, StudentResult, EntityIdSequence.

EF migrations apply automatically on startup. Latest migrations include `CollapseMathematicsIntoScience` (data repair) and `DropImportJobs` (removes unused import-job table).

Student results are **not seeded**; admins import them at `/admin/import-results`.

---

## API and import

Envelope: `{ success, message, errorCode?, data, errors? }`. Failures use a stable `errorCode` and Arabic `message`. Success default message is English. `/health` is not wrapped.

Auth is **cookie Identity**, role `Administrator`.

**Import:** Markdown cutoffs — `POST /api/admin/admission-years/{yearId}/import`. Student results — `POST /api/admin/admission-years/{yearId}/student-results/import`.

Full contract: [docs/API.md](docs/API.md).

---

## Testing

```powershell
dotnet test Tansekak.sln --configuration Release
```

CI runs an Angular production build, then that command. **97 tests** across Application, Infrastructure, Infrastructure integration (Postgres, needs Docker), and Api. There are no E2E or Angular unit-test projects.

---

## Building for production manually

```powershell
docker build -t tansekak .
docker run -p 8080:8080 `
  -e ConnectionStrings__DefaultConnection="Host=...;SSL Mode=Require" `
  -e AdminSeed__Email="admin@example.com" `
  -e AdminSeed__Password="your-strong-password" `
  tansekak
```

Or without Docker: `npm ci` + production Angular build, `dotnet publish` the API, copy `client/dist/client/browser/*` into `publish/wwwroot`.

---

## Disclaimer

Tansekak uses official cutoff lists to estimate eligibility. It does not replace Egypt’s electronic coordination portal. Actual admission depends on demand, seats, preference order, and official rules for the current cycle.
