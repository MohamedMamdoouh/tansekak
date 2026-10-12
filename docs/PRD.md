# Tansekak — Product Specification

## 1. Purpose

Tansekak is an **admission eligibility checker** for Egyptian Thanaweya Amma graduates.

A student enters an academic track and a total score. The app compares those values to official cutoff scores for the **current published admission year** and lists university faculties the student is **eligible** for.

Students can also look up a stored Thanaweya result by seating number once an administrator has imported results for the current year.

Results are **indicative only**. Egypt’s official electronic coordination portal decides final placement. Tansekak does not register preferences, allocate seats, or issue admission decisions.

---

## 2. Users

| User                     | Goal                                                                                                                     |
| ------------------------ | ------------------------------------------------------------------------------------------------------------------------ |
| Student / parent         | Check which faculties a score is eligible for; look up a Thanaweya result; read a short coordination FAQ. |
| Operator / administrator | Publish the current admission year; maintain cutoffs (CRUD and Markdown import).                                         |
| Catalog maintainer       | Manage governorates, universities, faculties, and university–faculty links via API only (no admin UI).                   |

There is a single Identity role: **Administrator**. Public visitors are unauthenticated. Admin login is at `/admin/login` and is not linked from public pages until an administrator is already signed in (then the public header shows a dashboard link).

---

## 3. In scope

- Public RTL Arabic SPA for prediction, results, Thanaweya lookup, guide, and designer credits.
- Eligible-college prediction against the current published year.
- Admin cookie-authenticated UI for years, cutoffs, and cutoff import.
- Catalog JSON bootstrap, admission-year bootstrap, faculty `AllowedTracks` repair, missing-track cutoff bootstrap, and admin seed.
- Admin Excel import of student results (`.xlsx`, max 50 MB) for the current year.
- Markdown cutoff import for the current admission year.
- Read-only Thanaweya lookup against imported `StudentResult` rows. The page opens when the current year has results.
- Envelope API with stable `errorCode` values and Arabic failure messages.

---

## 4. Out of scope / non-goals

Tansekak does **not**:

- Replace the official coordination portal or submit student preferences.
- Guarantee placement, seats, or demand-adjusted cutoffs.
- Return ineligible faculties or extra eligibility bands.
- Store English names, slugs, or active/inactive flags on catalog entities.
- Overwrite existing cutoffs during startup bootstrap.
- Provide admin UI for governorates, universities, faculties, or university–faculties.
- Use bearer tokens for auth.
- Ship OpenAPI outside Development.
- Include E2E or Angular unit-test projects.
- Run async import jobs or use Cloudflare R2 for large uploads (removed).
- Track rank page or stored track/rank fields on `StudentResult` (removed).

---

## 5. Branding and UI

| Surface                           | Name         |
| --------------------------------- | ------------ |
| Public brand                      | **Tansekak** |
| API `appName` (`GET /api/config`) | `tansekak`   |
| Admin chrome                      | **Admin**    |

- UI is **RTL Arabic**.
- Catalog and result names are **NameAr only**. There is no `NameEn`, `Slug`, or `IsActive`.
- Admin sign-in is at `/admin/login`.

---

## 6. Public product

### 6.1 Routes

| Route               | Page                                       |
| ------------------- | ------------------------------------------ |
| `/`                 | Landing — overview and entry points        |
| `/predict`          | Track + score form                         |
| `/results`          | Eligible faculties (paginated, searchable) |
| `/thanaweya-result` | Seating-number result lookup               |
| `/guide`            | Static coordination FAQ                    |
| `/designer`         | Developer / credits page                   |

Unknown paths redirect to `/`.

### 6.2 Prediction

**Request:** academic track and total score. The API uses the **current published** admission year (`IsCurrent`). Score must not exceed that year’s `MaximumScore`.

The `/predict` page collects input and navigates to `/results?track=&score=`. Only `/results` calls `POST /api/admission/predict`.

**Eligibility (must all hold):**

1. Cutoff belongs to the current year.
2. Student score **≥** cutoff score (eligible only).
3. Cutoff track **equals** the student’s track exactly.
4. Faculty `AllowedTracks` includes the student’s track.

**Sort:** `abs(score − cutoff)` ascending (closest eligible cutoff first). Duplicate university–faculty rows are collapsed to one result.

**Pagination:**

| Layer    | Page size                                                             |
| -------- | --------------------------------------------------------------------- |
| Frontend | `pageSize` **20**, unlimited load-more (and show-all remaining pages) |
| API      | Default **10**, maximum **100**                                       |

**Search:** client-side filter of **already loaded** rows by university or faculty `NameAr`. Search does not query the server.

Until cutoffs exist for the current year, prediction returns an empty list.

### 6.3 Thanaweya lookup

`/thanaweya-result` calls `GET /api/thanaweya-results/{seatingNo}` for the **current** admission year.

When a row exists, the API returns seating number, Arabic name, total degree, year, and maximum score.

The page and the API are available only after the current year has imported results (`studentResultsAvailable` on `GET /api/config`). Lookup is limited to 60 requests per minute per IP. Lookup returns not found when the seating number is missing for the current year.

**Data source:** an administrator uploads one `.xlsx` at `/admin/import-results`. Each upload replaces every result for the current year.

---

## 7. Academic tracks

Public UI and `GET /api/config` expose **three** tracks: `Science`, `Mathematics`, `Literature`.

Each track is distinct end-to-end: prediction, cutoff import, and faculty eligibility use **exact track matching** (no bucketing). Student results are not stored by track.

---

## 8. Admin product

Authentication is **cookie-based ASP.NET Core Identity** with the **Administrator** role.

| Route            | Capability                                                                      |
| ---------------- | ------------------------------------------------------------------------------- |
| `/admin/login`   | Sign in                                                                         |
| `/admin`         | Dashboard                                                                       |
| `/admin/years`   | Create, edit, and delete the single admission year                              |
| `/admin/cutoffs` | CRUD for the **current year only** (API may filter by `yearId`)                 |
| `/admin/import`  | One `.md` per track (Science, Mathematics, Literature) for the **current year** |
| `/admin/import-results` | One `.xlsx` per upload; replaces all student results for the **current year** |

The dashboard links to `/admin/import-results`. The page shows the stored count, the latest import or delete, the upload, and a delete-all action that requires typing the confirmation word.

The cutoff import page shows a progress overlay. Navigation away is **blocked** until the import finishes or the operator confirms leaving.

### 8.1 Dashboard

Shows:

- governorates
- faculties (faculty types, not university–faculty rows)
- current year
- student results (count of existing rows)

The dashboard API returns exactly those four fields (`governoratesCount`, `facultiesCount`, `studentResultsCount`, `currentYear`). It does not return university, university–faculty, or cutoff counts.

### 8.2 Admission years

Only **one** admission year may exist at a time.

- Create when no year exists; new rows are marked `IsCurrent = true`.
- A second create while any year exists returns `ADMISSION_YEAR_LIMIT_REACHED`.
- Edit year number and maximum score (`IsCurrent` is unchanged, unless no current year exists and the update promotes it).
- Delete cascades cutoffs and student results for that year and may promote the newest remaining year.
- To switch cycles: delete the current year, then create the new one.
- Year range: **2000–2100**. Maximum score: **> 0 and ≤ 1000**, default **320**. Duplicate calendar year is rejected.
- Prediction, public lookup, and the admin import UI all use the current year (`IsCurrent`).

### 8.3 Cutoffs

Admin UI lists/creates/updates/deletes cutoffs for the current year. University and faculty dropdowns come from the existing catalog. Cutoffs are the only catalog-adjacent records the UI can delete.

### 8.4 API-only catalog (no UI)

These have admin API endpoints (GET/POST/PUT, no delete) and no SPA pages:

- Governorates
- Universities
- Faculties
- University–faculties (list is used internally by cutoff CRUD)

---

## 9. Data model (summary)

Business entities use **integer IDs** allocated by `EntityIdAllocator` / `EntityIdSequence`. ASP.NET Identity tables use **PostgreSQL identity columns**.

| Entity                | Role                                                                                  |
| --------------------- | ------------------------------------------------------------------------------------- |
| **Governorate**       | Egyptian governorate (`NameAr`)                                                       |
| **University**        | `NameAr`, `GovernorateId`, `Type` = `Public` (1) or `Institute` (2)                   |
| **Faculty**           | `NameAr`, `AllowedTracks` (JSON list of academic tracks)                              |
| **UniversityFaculty** | Links a university to a faculty; cutoffs hang off this pair                           |
| **AdmissionYear**     | Calendar year, `MaximumScore`, `IsCurrent`                                            |
| **AdmissionCutoff**   | Year + university–faculty + track + `CutoffScore`                                     |
| **StudentResult**     | Year + seating no, Arabic name, total degree                                          |
| **StudentResultImportEvent** | Import or delete: file name, row count, time, admin email                     |
| **EntityIdSequence**  | Next integer ID per entity name                                                       |

**Relationships (high level):**

```
Governorate 1──* University 1──* UniversityFaculty *──1 Faculty
AdmissionYear 1──* AdmissionCutoff *──1 UniversityFaculty
AdmissionYear 1──* StudentResult
AdmissionYear 1──* StudentResultImportEvent
```

Uniqueness that matters in operations: one calendar year value; cutoffs are replaced per year+track on Markdown import.

The former `ImportJobs` table was removed; async Excel import is not part of the current stage.

---

## 10. Bootstrap and seeding

On startup the API validates production configuration (non-Development), applies EF migrations, then:

1. **Catalog seed (once):** if `Governorates` is empty, load JSON from `SeededData/` (`Governorates`, `Universities`, `Faculties`, `UniversityFaculties`) and create year **2027**, max score **320**, `IsCurrent = true`. After that, the database is the source of truth for those catalog rows.
2. **Faculty repair (every startup):** sync `Faculties.AllowedTracks` from seed JSON when they diverge.
3. **Cutoff bootstrap (every startup):** import seed Markdown for any track that has **zero** cutoffs in the current year. Does not overwrite existing cutoffs.
4. **Admin user:** from `AdminSeed` (`AdminSeed__Email` / `AdminSeed__Password`). Development defaults are rejected at startup outside Development.

**Seed cutoff files:**

- `SeededData/cutoffs/science-2026.md`
- `SeededData/cutoffs/mathematics-2026.md`
- `SeededData/cutoffs/literature-2026.md`

**2026** is the official source cycle in those files. Rows attach to the **current** year (bootstrap default **2027**). Local `dotnet run` copies them to `SeedData/cutoffs/`. Docker may omit them because `.dockerignore` excludes `*.md`; then the operator uploads from a local clone at `/admin/import`.

Student workbooks are not stored in the repo and are not imported at startup.

---

## 11. Import

Admin imports target the **current** admission year only. There is no preview step: a file is validated, then applied in full or rejected in full.

- **Cutoffs:** One Markdown (`.md`, max 10 MB) per track (`Science`, `Mathematics`, `Literature`). Pipe table with college name and minimum cutoff columns. Each import replaces that track for the current year only.
- **Student results:** one `.xlsx` (max 50 MB). Row 1 must contain `seating_no`, `arabic_name`, and `total_degree` (any order; extra columns ignored). Seating numbers are digits only and unique. Scores run from 0 to the year's maximum, with at most 2 decimal places. Any error rejects the whole file. The response returns the first 100 errors and states the total count. A successful import replaces every result for that year inside one database transaction.

Any validation error rejects the entire file. Import endpoints and error codes: **[docs/API.md](API.md)**.

---

## 12. API contract

JSON responses use a standard envelope (`success`, `message`, `errorCode`, `data`, `errors`). Failures return Arabic messages and stable error codes. `GET /health` is unwrapped. Admin auth is cookie-based Identity (role `Administrator`).

Public surface: `GET /api/config`, `POST /api/admission/predict`, `GET /api/thanaweya-results/{seatingNo}`. Admin covers dashboard, catalog CRUD (no delete except cutoffs and years), admission year CRUD (single year), cutoffs, and cutoff import.

Full HTTP contract, status codes, and error catalog: **[docs/API.md](API.md)**.

In production the API serves the Angular build from `wwwroot/` and falls back to `index.html` for client routes. Local Development uses CORS to `http://localhost:4200`. Production SPA and API share one origin.

---

## 13. Stack and architecture

| Layer      | Technology                     |
| ---------- | ------------------------------ |
| API        | ASP.NET Core 10                |
| Data       | EF Core 10, PostgreSQL, Npgsql |
| Frontend   | Angular 19 standalone, RTL     |
| Validation | FluentValidation               |
| Tests      | xUnit                              |

**Clean Architecture** projects: `Tansekak.Domain`, `Tansekak.Application`, `Tansekak.Infrastructure`, `Tansekak.Api`.

Application layer is **service interfaces + DTOs**. There is **no MediatR** and **no repository layer**. Infrastructure services use `AppDbContext` directly. Domain entities are anemic POCOs.

---

## 14. Deployment

One Docker monolith on Render (API + SPA), Neon PostgreSQL. GitHub Actions runs an Angular production build plus `dotnet test Tansekak.sln`; Render auto-deploys on push to `main`.

Checklist, env vars, and first-boot sequence: **[docs/DEPLOY.md](DEPLOY.md)**.

---

## 15. Tests

| Project                         | In `Tansekak.sln` / CI | Coverage                                                                                                                   |
| ------------------------------- | ---------------------- | -------------------------------------------------------------------------------------------------------------------------- |
| `Tansekak.Application.Tests`    | Yes                    | Track rules, Arabic error catalog, results query helpers                                                    |
| `Tansekak.Infrastructure.Tests` | Yes                    | Prediction, admission year rules, Thanaweya lookup, Excel reader, seeded Markdown parse, connection resolution |
| `Tansekak.Infrastructure.IntegrationTests` | Yes         | Postgres import: replace, rollback, cancel, concurrent lock, delete, year guards                           |
| `Tansekak.Api.Tests`            | Yes                    | HTTP integration (config, predict, Thanaweya lookup, auth, student-result import) and global exception handler |

There are no E2E or Angular unit-test projects. The Postgres project needs Docker.

---

## 16. Success criteria (current stage)

The product is successful for this stage when:

- A student can get an **eligible-only** faculty list for the published year, filtered by track and `AllowedTracks`, sorted by closest cutoff.
- A student can look up an imported result by seating number when the current year has results.
- An operator can manage the admission year, import Science/Mathematics/Literature Markdown, CRUD current-year cutoffs, and replace or delete the current year's student results from Excel.
- Failures return Arabic catalog messages and stable error codes; health is a raw probe; auth is cookies.
- Fresh local databases bootstrap catalog + 2027 current year + admin + missing-track cutoffs from seed Markdown, so prediction works without a manual first import when those files are on disk.

---

## 17. Related documents

| Document                    | Role                            |
| --------------------------- | ------------------------------- |
| [README.md](../README.md)   | Developer and operator guide    |
| [docs/API.md](API.md)       | Public and admin HTTP contract  |
| [docs/DEPLOY.md](DEPLOY.md) | Production deploy, Render, Neon |
