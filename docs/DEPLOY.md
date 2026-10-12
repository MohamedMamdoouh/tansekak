# Deploy

Deploy Tansekak to **Render** (Docker monolith) with **Neon Postgres**.

Related: [API.md](./API.md)

---

## Stack

| Layer | Provider | Notes |
| --- | --- | --- |
| App + SPA | Render | One container serves `/api/*` and the Angular app from `wwwroot` |
| Database | Neon | Pooled PostgreSQL connection string |

No CORS setup on Render — API and SPA share one origin. Do **not** set `Frontend__Origin`.

No object storage (R2/S3) is required. Student results are imported via `/admin/import-results` (`.xlsx`, max 50 MB).

There is no `render.yaml`. Create the web service in the Render dashboard.

---

## 1. Neon

1. Create a project at [neon.tech](https://neon.tech).
2. Copy the **pooled** connection string.
3. Set it on Render as `ConnectionStrings__DefaultConnection` (or `DATABASE_URL`).

Npgsql keyword format or a `postgresql://…` URI both work. The app rejects `localhost` / `127.0.0.1` in production.

---

## 2. Render

1. Connect the GitHub repo at [render.com](https://render.com).
2. Create a **Web Service** → **Build from Dockerfile** (`./Dockerfile`, repo root).
3. Health check: `/health`.
4. Set environment variables in Render Dashboard → **Environment**:

   | Variable | Required | Notes |
   | --- | --- | --- |
   | `ConnectionStrings__DefaultConnection` | Yes* | Neon pooled connection string |
   | `DATABASE_URL` | Alternative | Neon `postgresql://…` URI if the connection string above is unset |
   | `AdminSeed__Email` | Yes | Not `admin@tansekak.local` |
   | `AdminSeed__Password` | Yes | Not `Admin@12345` |

   \*Required unless `DATABASE_URL` is set.

5. Push to `main` (auto-deploy) or trigger a manual deploy.

`PORT` is set by Render. The app binds to `0.0.0.0:$PORT`.

---

## 3. After first deploy

On boot the app:

1. Rejects localhost DB strings and the development admin credentials.
2. Runs EF migrations (including `DropImportJobs`, which removes the unused import-job table).
3. Seeds the catalog from JSON if `Governorates` is empty, and creates admission year **2027** (max 320, current).
4. Repairs `Faculties.AllowedTracks` from seed JSON when they diverge.
5. Imports seed Markdown for any current-year track that has **zero** cutoffs (does not overwrite existing rows).
6. Creates the admin user from `AdminSeed__*`.

Catalog JSON is copied into the image as `SeedData/*.json`. Cutoff Markdown is copied by the API project when those files are in the Docker build context (`SeededData/cutoffs/*.md` → `SeedData/cutoffs/`). `.dockerignore` excludes `*.md` globally, so production images often skip cutoff bootstrap and log that seed cutoff files were not found.

If prediction is empty after first boot, sign in at `/admin/login`, confirm the year on `/admin/years`, then upload from a local clone at `/admin/import`:

| Track | Seed file |
| --- | --- |
| `Science` | `SeededData/cutoffs/science-2026.md` |
| `Mathematics` | `SeededData/cutoffs/mathematics-2026.md` |
| `Literature` | `SeededData/cutoffs/literature-2026.md` |

The `2026` in filenames is the official source cycle; rows attach to the current year (**2027** on bootstrap). Each import replaces that track only.

### Student results

Student results are **never seeded**. After first deploy, sign in and upload at `/admin/import-results`.

- `/thanaweya-result` opens when the current year has results (`studentResultsAvailable` on `/api/config`).
- Large imports run synchronously in one request; avoid deploying while an import is running. A synthetic 800,000-row file imported in about 2.3 minutes on a container limited to 512 MB and 0.1 CPU, peaking at about 211 MB. A synthetic 800,000-row file (16 MB) finished in about 2.3 minutes inside Docker limited to 512 MB and 0.1 CPU, peaking at about 250 MB.
- Deleting the admission year cascades student results and import history for that year.

---

## 4. Smoke test

| Check | Expected |
| --- | --- |
| `GET /health` | `{ "status": "healthy" }` |
| `/predict` | SPA loads; submitting navigates to `/results` |
| `/admin/login` → sign in | Cookie auth works |
| `GET /api/config` | Current year, max score, tracks `Science` / `Mathematics` / `Literature` |
| Predict after cutoffs exist | Science vs Mathematics with the same score return different faculty lists |
| `/admin/import` | Markdown upload works for one track |
| `/thanaweya-result` | Returns `404` / not-found UI when no row exists; shows data when rows exist |
