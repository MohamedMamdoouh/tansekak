import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import {
  normalizeConfig,
  normalizePagedCutoffs,
  normalizePredictResponse,
  normalizeStudentResult,
  unwrapApiData,
} from '../utils/api-normalize.util';
import {
  ImportUploadProgress,
  uploadImportFile,
} from './import-file-upload';
import {
  AdmissionCutoff,
  AdmissionYear,
  ApiResponse,
  AuthUser,
  Config,
  Dashboard,
  ImportResult,
  DeleteStudentResultsResponse,
  StudentResultsSummary,
  PagedCutoffs,
  PredictRequest,
  PredictResponse,
  StudentResult,
  UniversityFaculty,
} from '../models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  constructor(private http: HttpClient) {}

  getConfig(): Observable<Config> {
    return this.http
      .get<ApiResponse<Config>>('/api/config')
      .pipe(map((r) => normalizeConfig(r.data)));
  }

  predict(body: PredictRequest): Observable<PredictResponse> {
    return this.http
      .post<ApiResponse<PredictResponse>>('/api/admission/predict', body)
      .pipe(map((r) => normalizePredictResponse(r.data)));
  }

  getThanaweyaResult(seatingNo: string): Observable<StudentResult> {
    return this.http
      .get<ApiResponse<StudentResult>>(
        `/api/thanaweya-results/${encodeURIComponent(seatingNo)}`,
      )
      .pipe(
        map((r) =>
          normalizeStudentResult(
            unwrapApiData(r as ApiResponse<StudentResult> & { Data?: StudentResult }),
          ),
        ),
      );
  }

  login(email: string, password: string): Observable<AuthUser> {
    return this.http
      .post<ApiResponse<AuthUser>>('/api/admin/auth/login', { email, password })
      .pipe(map((r) => r.data));
  }

  logout(): Observable<void> {
    return this.http
      .post<ApiResponse<unknown>>('/api/admin/auth/logout', {})
      .pipe(map(() => undefined));
  }

  me(): Observable<AuthUser | null> {
    return this.http
      .get<ApiResponse<AuthUser>>('/api/admin/auth/me')
      .pipe(map((r) => (r.success ? r.data : null)));
  }

  getDashboard(): Observable<Dashboard> {
    return this.http
      .get<ApiResponse<Dashboard>>('/api/admin/dashboard')
      .pipe(map((r) => r.data));
  }

  checkHealth(): Observable<boolean> {
    return this.http.get<{ status: string }>('/health').pipe(
      map((response) => response.status === 'healthy'),
      catchError(() => of(false)),
    );
  }

  getUniversityFaculties(search = ''): Observable<UniversityFaculty[]> {
    const q = search ? `?search=${encodeURIComponent(search)}` : '';
    return this.http
      .get<
        ApiResponse<UniversityFaculty[]>
      >(`/api/admin/university-faculties${q}`)
      .pipe(map((r) => r.data));
  }

  getAdmissionYears(): Observable<AdmissionYear[]> {
    return this.http
      .get<ApiResponse<AdmissionYear[]>>('/api/admin/admission-years')
      .pipe(map((r) => r.data));
  }

  getCurrentAdmissionYear(): Observable<AdmissionYear> {
    return this.http
      .get<ApiResponse<AdmissionYear>>('/api/admin/admission-years/current')
      .pipe(map((r) => r.data));
  }

  createAdmissionYear(payload: {
    year: number;
    maximumScore: number;
  }): Observable<AdmissionYear> {
    return this.http
      .post<ApiResponse<AdmissionYear>>('/api/admin/admission-years', payload)
      .pipe(map((r) => r.data));
  }

  updateAdmissionYear(
    id: number,
    payload: { year: number; maximumScore: number },
  ): Observable<AdmissionYear> {
    return this.http
      .put<ApiResponse<AdmissionYear>>(
        `/api/admin/admission-years/${id}`,
        payload,
      )
      .pipe(map((r) => r.data));
  }

  deleteAdmissionYear(id: number): Observable<void> {
    return this.http
      .delete<ApiResponse<unknown>>(`/api/admin/admission-years/${id}`)
      .pipe(map(() => undefined));
  }

  getCutoffs(
    yearId?: number,
    search = '',
    page = 1,
    pageSize = 10,
  ): Observable<PagedCutoffs> {
    const params = new URLSearchParams();
    if (yearId) params.set('yearId', String(yearId));
    if (search) params.set('search', search);
    params.set('page', String(page));
    params.set('pageSize', String(pageSize));
    const q = params.toString();
    return this.http
      .get<
        ApiResponse<PagedCutoffs | AdmissionCutoff[]>
      >(`/api/admin/admission-cutoffs?${q}`)
      .pipe(map((r) => normalizePagedCutoffs(r.data, page, pageSize)));
  }

  createCutoff(payload: Partial<AdmissionCutoff>): Observable<AdmissionCutoff> {
    return this.http
      .post<
        ApiResponse<AdmissionCutoff>
      >('/api/admin/admission-cutoffs', payload)
      .pipe(map((r) => r.data));
  }

  updateCutoff(
    id: number,
    payload: Partial<AdmissionCutoff>,
  ): Observable<AdmissionCutoff> {
    return this.http
      .put<
        ApiResponse<AdmissionCutoff>
      >(`/api/admin/admission-cutoffs/${id}`, payload)
      .pipe(map((r) => r.data));
  }

  deleteCutoff(id: number): Observable<void> {
    return this.http
      .delete<ApiResponse<unknown>>(`/api/admin/admission-cutoffs/${id}`)
      .pipe(map(() => undefined));
  }

  importCutoffs(
    yearId: number,
    track: string,
    file: File,
  ): Observable<ImportResult> {
    const form = this.buildCutoffImportFormData(file, track);
    return this.http
      .post<
        ApiResponse<ImportResult>
      >(`/api/admin/admission-years/${yearId}/import`, form)
      .pipe(map((r) => r.data));
  }

  importCutoffsWithProgress(
    yearId: number,
    track: string,
    file: File,
    onProgress: (progress: ImportUploadProgress) => void,
    signal?: AbortSignal,
  ): Promise<ImportResult> {
    const form = this.buildCutoffImportFormData(file, track);
    return uploadImportFile(
      this.http,
      `/api/admin/admission-years/${yearId}/import`,
      form,
      onProgress,
      signal,
    );
  }

  getStudentResultsSummary(yearId: number): Observable<StudentResultsSummary> {
    return this.http
      .get<ApiResponse<StudentResultsSummary>>(
        `/api/admin/admission-years/${yearId}/student-results/summary`,
      )
      .pipe(map((r) => r.data));
  }

  importStudentResultsWithProgress(
    yearId: number,
    file: File,
    onProgress: (progress: ImportUploadProgress) => void,
    signal?: AbortSignal,
  ): Promise<ImportResult> {
    const form = new FormData();
    form.append('file', file);
    return uploadImportFile(
      this.http,
      `/api/admin/admission-years/${yearId}/student-results/import`,
      form,
      onProgress,
      signal,
    );
  }

  deleteStudentResults(yearId: number): Observable<DeleteStudentResultsResponse> {
    return this.http
      .delete<ApiResponse<DeleteStudentResultsResponse>>(
        `/api/admin/admission-years/${yearId}/student-results`,
      )
      .pipe(map((r) => r.data));
  }

  private buildCutoffImportFormData(file: File, track: string): FormData {
    const form = new FormData();
    form.append('file', file);
    form.append('track', track);
    return form;
  }
}

export type { ImportUploadError, ImportUploadProgress } from './import-file-upload';
