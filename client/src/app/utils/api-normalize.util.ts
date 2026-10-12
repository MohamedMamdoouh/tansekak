import {
  AdmissionCutoff,
  AdmissionResult,
  ApiResponse,
  Config,
  ImportResult,
  PagedCutoffs,
  PredictResponse,
  StudentResult,
} from '../models';

export function normalizeConfig(data: Config | null | undefined): Config {
  const raw = (data ?? {}) as Config & {
    StudentResultsAvailable?: boolean;
  };
  return {
    appName: raw.appName ?? '',
    currentYear: raw.currentYear ?? 0,
    maximumScore: raw.maximumScore ?? 0,
    tracks: raw.tracks ?? [],
    studentResultsAvailable:
      raw.studentResultsAvailable ?? raw.StudentResultsAvailable ?? false,
  };
}

export function normalizeStudentResult(
  data: StudentResult | null | undefined,
): StudentResult {
  if (!data) {
    return {
      seatingNo: '',
      arabicName: '',
      totalDegree: 0,
      year: 0,
      maximumScore: 0,
    };
  }

  const raw = data as StudentResult & {
    SeatingNo?: string;
    ArabicName?: string;
    TotalDegree?: number;
    Year?: number;
    MaximumScore?: number;
  };

  return {
    seatingNo: raw.seatingNo ?? raw.SeatingNo ?? '',
    arabicName: raw.arabicName ?? raw.ArabicName ?? '',
    totalDegree: raw.totalDegree ?? raw.TotalDegree ?? 0,
    year: raw.year ?? raw.Year ?? 0,
    maximumScore: raw.maximumScore ?? raw.MaximumScore ?? 0,
  };
}

export function normalizePredictResponse(
  data: PredictResponse | null | undefined,
): PredictResponse {
  if (!data) {
    return { results: [], hasMore: false, totalCount: 0 };
  }

  const raw = data as PredictResponse & {
    Results?: AdmissionResult[];
    HasMore?: boolean;
    TotalCount?: number;
  };

  return {
    results: raw.results ?? raw.Results ?? [],
    hasMore: raw.hasMore ?? raw.HasMore ?? false,
    totalCount: raw.totalCount ?? raw.TotalCount ?? raw.results?.length ?? 0,
  };
}

export function normalizePagedCutoffs(
  data: PagedCutoffs | AdmissionCutoff[] | null | undefined,
  page: number,
  pageSize: number,
): PagedCutoffs {
  if (!data) {
    return { items: [], totalCount: 0, page, pageSize };
  }

  if (Array.isArray(data)) {
    const start = (page - 1) * pageSize;
    return {
      items: data.slice(start, start + pageSize),
      totalCount: data.length,
      page,
      pageSize,
    };
  }

  const raw = data as PagedCutoffs & {
    Items?: AdmissionCutoff[];
    TotalCount?: number;
    Page?: number;
    PageSize?: number;
  };

  return {
    items: raw.items ?? raw.Items ?? [],
    totalCount: raw.totalCount ?? raw.TotalCount ?? 0,
    page: raw.page ?? raw.Page ?? page,
    pageSize: raw.pageSize ?? raw.PageSize ?? pageSize,
  };
}

export function unwrapApiData<T>(
  response: ApiResponse<T> & { Data?: T },
): T | undefined {
  return response.data ?? response.Data;
}
