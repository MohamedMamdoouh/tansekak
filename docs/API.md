# Tansekak API

JSON is camelCase. Controller responses use this envelope. `GET /health` does not.

```json
{
  "success": true,
  "message": "Operation completed successfully.",
  "errorCode": null,
  "data": {},
  "errors": null
}
```

| Field       | Notes                                                                                         |
| ----------- | --------------------------------------------------------------------------------------------- |
| `success`   | `true` / `false`                                                                              |
| `message`   | Success default is English. Failures are Arabic (see [Error codes](#error-codes))             |
| `errorCode` | Present on failure                                                                            |
| `data`      | Payload on success; usually `null` on failure                                                 |
| `errors`    | Optional field/row details: `{ field, message, rowNumber?, errorCode? }`. `message` is Arabic |

## Auth

Cookie Identity (not JWT). Send `credentials: include`. Cookie name `.AspNetCore.Identity.Application`. No CSRF. Production: `Secure` + `SameSite=Lax`. CORS only when `Frontend:Origin` is set.

Admin routes need role `Administrator`. Password min 8 characters. Lockout: 5 failures, 15 minutes.

| Case                                     | HTTP  | `errorCode`           |
| ---------------------------------------- | ----- | --------------------- |
| Not signed in                            | `401` | `NOT_AUTHENTICATED`   |
| Signed in, not Administrator             | `403` | `FORBIDDEN`           |
| Bad login, lockout, or non-admin account | `401` | `INVALID_CREDENTIALS` |

Non-admin accounts are signed out immediately. `POST /api/admin/auth/logout` and `GET /api/admin/auth/me` require a cookie. `/me` returns `401` `NOT_AUTHENTICATED` if the user is not an Administrator.

## Public

Tracks: `Science`, `Mathematics`, `Literature`. Arabic labels are also accepted. Pagination default `page=1`, `pageSize=10`, max `100`.

### `GET /api/config`

Current published year.

`data`: `{ appName, currentYear, maximumScore, tracks, studentResultsAvailable }`

`studentResultsAvailable` is `true` when the current year has at least one `StudentResult` row.

No current year → `503` `NO_CURRENT_YEAR`.

### `POST /api/admission/predict`

Current year only. Body: `{ track, score, page?, pageSize? }`. Score ≥ 0 and ≤ that year’s `maximumScore`.

Eligible when `score >= cutoffScore`, track matches exactly, and faculty `allowedTracks` includes the track. Sorted by `abs(score − cutoffScore)` ascending.

```json
{
  "results": [
    {
      "university": { "nameAr": "جامعة القاهرة" },
      "faculty": { "nameAr": "طب" }
    }
  ],
  "hasMore": true,
  "totalCount": 142
}
```

`nameAr` is always Arabic. Score above max → `400` `SCORE_EXCEEDS_MAX`. Invalid track/paging → `400` `VALIDATION_FAILED` or `INVALID_TRACK`.

The Angular `/predict` page only collects input; the client calls this endpoint from `/results`.

### `GET /api/thanaweya-results/{seatingNo}`

Current year only. Used by `/thanaweya-result`. Rate limit: **60 requests/minute per IP** → `429` `RATE_LIMIT_EXCEEDED`.

`data`: `{ seatingNo, arabicName, totalDegree, year, maximumScore }`

Missing → `404` `STUDENT_RESULT_NOT_FOUND`. Empty seating number is treated as not found.

### `GET /health`

Unwrapped `{ "status": "healthy" }`.

## Admin

All routes except `POST /api/admin/auth/login` need a cookie. Most also need `Administrator`.

Catalog resources have no `DELETE`. Cutoffs and admission years do.

### Auth and dashboard

| Method | Path                     | Notes                                                                     |
| ------ | ------------------------ | ------------------------------------------------------------------------- |
| `POST` | `/api/admin/auth/login`  | `{ email, password }` → `{ email, role: "Administrator" }`                |
| `POST` | `/api/admin/auth/logout` | Message `"Logged out successfully."`                                      |
| `GET`  | `/api/admin/auth/me`     | `{ email, role }`                                                         |
| `GET`  | `/api/admin/dashboard`   | `{ governoratesCount, facultiesCount, studentResultsCount, currentYear }` |

`studentResultsCount` counts existing `StudentResults` rows for the current year. It does not imply an import API exists.

### Catalog (GET list / GET by id / POST / PUT)

| Path                              | Body / query                                                                                           | Item shape                                                       |
| --------------------------------- | ------------------------------------------------------------------------------------------------------ | ---------------------------------------------------------------- |
| `/api/admin/governorates`         | `{ nameAr }`                                                                                           | `{ id, nameAr }`                                                 |
| `/api/admin/universities`         | query `search`, `governorateId`, `type` (`Public`/`Institute`); body `{ nameAr, governorateId, type }` | `{ id, nameAr, governorateId, type, governorateName? }`          |
| `/api/admin/faculties`            | query `search`; body `{ nameAr, allowedTracks }` (at least one track)                                  | `{ id, nameAr, allowedTracks }`                                  |
| `/api/admin/university-faculties` | query `search`, `universityId`, `facultyId`; body `{ universityId, facultyId }`                        | `{ id, universityId, facultyId, universityName?, facultyName? }` |

Missing id → `404` `NOT_FOUND`. Names in `nameAr` are Arabic.

### Admission years

`{ id, year, maximumScore, isCurrent }`. Year 2000–2100. `maximumScore` > 0 and ≤ 1000. Only **one** year may exist.

| Method   | Path                                 | Notes                                                                                       |
| -------- | ------------------------------------ | ------------------------------------------------------------------------------------------- |
| `GET`    | `/api/admin/admission-years`         | Newest first                                                                                |
| `GET`    | `/api/admin/admission-years/current` | `404` `NOT_FOUND` if none                                                                   |
| `GET`    | `/api/admin/admission-years/{id}`    |                                                                                             |
| `POST`   | `/api/admin/admission-years`         | `{ year, maximumScore }`, `isCurrent: true`. Existing year → `ADMISSION_YEAR_LIMIT_REACHED` |
| `PUT`    | `/api/admin/admission-years/{id}`    | Year number and max score. Rejects `maximumScore` below highest stored student total → `MAXIMUM_SCORE_BELOW_RESULTS` |
| `DELETE` | `/api/admin/admission-years/{id}`    | Cascades cutoffs and student results                                                        |

### Cutoffs

Item: `{ id, admissionYearId, universityFacultyId, track, cutoffScore, universityName?, facultyName? }`.

| Method   | Path                                | Notes                                                                                                |
| -------- | ----------------------------------- | ---------------------------------------------------------------------------------------------------- |
| `GET`    | `/api/admin/admission-cutoffs`      | Query `yearId`, `search`, `track`, `page`, `pageSize`. Paged `{ items, totalCount, page, pageSize }` |
| `GET`    | `/api/admin/admission-cutoffs/{id}` |                                                                                                      |
| `POST`   | `/api/admin/admission-cutoffs`      | Duplicate → `CUTOFF_DUPLICATE`. Faculty must allow the track                                         |
| `PUT`    | `/api/admin/admission-cutoffs/{id}` | Same rules                                                                                           |
| `DELETE` | `/api/admin/admission-cutoffs/{id}` | Message `"Deleted successfully."`                                                                    |

### Imports

Markdown cutoff import replaces that **year+track**. Student Excel import **replaces all results for the year**. Failed import → `400` `VALIDATION_FAILED` with row `errors` (first 100 shown, total count in `message`). Success `data`: `{ success, message, importedCount?, errors? }`.

| Method | Path                                                              | Notes                                                                                    |
| ------ | ----------------------------------------------------------------- | ---------------------------------------------------------------------------------------- |
| `POST` | `/api/admin/admission-years/{yearId}/import`                      | multipart `file` (`.md`, max 10 MB) + `track`                                            |
| `GET`  | `/api/admin/admission-years/{yearId}/student-results/summary`     | `{ totalCount, latestEvent? }`                                                           |
| `POST` | `/api/admin/admission-years/{yearId}/student-results/import`      | multipart `file` (`.xlsx`, max 50 MB). Headers row 1: `seating_no`, `arabic_name`, `total_degree` |
| `DELETE` | `/api/admin/admission-years/{yearId}/student-results`           | Deletes all results for the year; writes a `Deleted` import event                          |

Concurrent import/delete → `409` `IMPORT_IN_PROGRESS`. Oversized file → `413` `FILE_TOO_LARGE`.

## Status codes

| HTTP  | When                        |
| ----- | --------------------------- |
| `400` | Validation / business rules |
| `401` | Not signed in or bad login  |
| `403` | Not Administrator           |
| `404` | Missing entity              |
| `500` | `INTERNAL_ERROR`            |
| `409` | `IMPORT_IN_PROGRESS`        |
| `413` | `FILE_TOO_LARGE`            |
| `429` | `RATE_LIMIT_EXCEEDED`       |
| `503` | `NO_CURRENT_YEAR`           |

## Error codes

Failure `message` is Arabic. Placeholders: `SCORE_EXCEEDS_MAX` (`{0}` max score), `UNRESOLVED_COLLEGE` (`{0}` college text). Unknown codes use `INTERNAL_ERROR`.

| Code                           | `message`                                                 |
| ------------------------------ | --------------------------------------------------------- |
| `INTERNAL_ERROR`               | حدث خطأ غير متوقع. حاول مرة أخرى لاحقًا.                  |
| `NOT_FOUND`                    | العنصر المطلوب غير موجود.                                 |
| `VALIDATION_FAILED`            | يرجى التحقق من البيانات المدخلة.                          |
| `INVALID_CREDENTIALS`          | بيانات الدخول غير صحيحة.                                  |
| `NOT_AUTHENTICATED`            | يجب تسجيل الدخول للمتابعة.                                |
| `FORBIDDEN`                    | ليس لديك صلاحية لتنفيذ هذا الإجراء.                       |
| `SERVICE_UNAVAILABLE`          | الخدمة غير متاحة حاليًا. حاول مرة أخرى لاحقًا.            |
| `NO_CURRENT_YEAR`              | لم يتم تعيين سنة قبول حالية.                              |
| `INVALID_TRACK`                | الشعبة غير صحيحة.                                         |
| `INVALID_UNIVERSITY_TYPE`      | نوع الجامعة غير صحيح.                                     |
| `SCORE_EXCEEDS_MAX`            | المجموع يتجاوز الحد الأقصى المسموح ({0}).                 |
| `CUTOFF_NEGATIVE`              | يجب أن يكون الحد الأدنى صفراً أو أكثر.                    |
| `CUTOFF_DUPLICATE`             | يوجد بالفعل حد قبول لهذه الكلية والشعبة في السنة المحددة. |
| `ADMISSION_YEAR_NOT_FOUND`     | سنة القبول غير موجودة.                                    |
| `ADMISSION_YEAR_DUPLICATE`     | هذه السنة موجودة بالفعل.                                  |
| `ADMISSION_YEAR_LIMIT_REACHED` | يوجد سنة قبول بالفعل. احذفها أولاً لإضافة سنة جديدة.      |
| `UNIVERSITY_FACULTY_NOT_FOUND` | كلية الجامعة غير موجودة.                                  |
| `STUDENT_RESULT_NOT_FOUND`     | لم يتم العثور على نتيجة لهذا الرقم.                       |
| `FILE_REQUIRED`                | الملف مطلوب.                                              |
| `TRACK_REQUIRED`               | الشعبة مطلوبة.                                            |
| `ONLY_MD_FILES`                | يُسمح فقط بملفات .md.                                     |
| `ALLOWED_TRACKS_REQUIRED`      | يجب تحديد شعبة واحدة على الأقل.                           |
| `FACULTY_TRACK_NOT_ALLOWED`    | الكلية غير متاحة للشعبة المحددة.                          |
| `REQUIRED`                     | هذا الحقل مطلوب.                                          |
| `INVALID`                      | قيمة غير صالحة.                                           |
| `EMPTY`                        | الملف فارغ أو لا يحتوي على بيانات.                        |
| `DUPLICATE`                    | قيمة مكررة.                                               |
| `INVALID_ROW`                  | تنسيق الصف غير صحيح.                                      |
| `MALFORMED`                    | تنسيق البيانات غير صحيح.                                  |
| `TRACK_NOT_ALLOWED`            | الكلية غير متاحة للشعبة المحددة.                          |
| `SCORE_MUST_BE_POSITIVE`       | يجب أن يكون الحد الأدنى أكبر من صفر.                      |
| `DUPLICATE_ROW`                | صف مكرر في الملف.                                         |
| `COLLEGE_CONTAINS_SCORE`       | عمود الكلية يحتوي على قيمة درجة.                          |
| `CUTOFF_MUST_BE_NUMBER`        | يجب أن يكون الحد الأدنى رقماً.                            |
| `FILE_NO_DATA_ROWS`            | الملف لا يحتوي على صفوف بيانات.                           |
| `UNRESOLVED_COLLEGE`           | تعذر مطابقة "{0}" مع جامعة/كلية في النظام.                |
| `ONLY_XLSX_FILES`              | يُسمح فقط بملفات Excel بصيغة .xlsx.                       |
| `FILE_TOO_LARGE`               | حجم الملف يتجاوز 50 ميجابايت.                             |
| `IMPORT_IN_PROGRESS`           | يوجد استيراد نتائج قيد التنفيذ. حاول مرة أخرى بعد قليل.   |
| `IMPORT_FILE_INVALID`          | ملف Excel غير صالح أو تالف.                               |
| `MISSING_COLUMN`                 | العمود المطلوب "{0}" غير موجود في الصف الأول.             |
| `TOTAL_DEGREE_INVALID`         | قيمة المجموع غير صالحة.                                   |
| `TOTAL_DEGREE_NEGATIVE`        | يجب أن يكون المجموع صفراً أو أكثر.                        |
| `FIELD_TOO_LONG`               | الحقل "{0}" يتجاوز الحد الأقصى ({1} حرف).                 |
| `SEATING_NO_INVALID`           | رقم الجلوس يجب أن يحتوي على أرقام فقط.                    |
| `RATE_LIMIT_EXCEEDED`          | طلبات كثيرة. حاول مرة أخرى بعد دقيقة.                     |
| `MAXIMUM_SCORE_BELOW_RESULTS`  | لا يمكن تخفيض الحد الأقصى للمجموع لأن هناك نتائج أعلى.    |

## OpenAPI

Development only: `/openapi/v1.json`. No Swagger UI. Not mapped in production.
