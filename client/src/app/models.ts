export const DEFAULT_MAXIMUM_SCORE = 320;

export interface AuthUser {
  email: string;
  role: string;
}

export interface ApiResponse<T> {
  success: boolean;
  message: string;
  errorCode?: string;
  data: T;
  errors?: {
    field: string;
    message: string;
    rowNumber?: number;
    errorCode?: string;
  }[];
}

export interface Config {
  appName: string;
  currentYear: number;
  maximumScore: number;
  tracks: string[];
  studentResultsAvailable: boolean;
}

export interface StudentResultImportEvent {
  action: string;
  fileName?: string | null;
  rowCount?: number | null;
  performedAt: string;
  performedByEmail: string;
}

export interface StudentResultsSummary {
  totalCount: number;
  latestEvent: StudentResultImportEvent | null;
}

export interface DeleteStudentResultsResponse {
  deletedCount: number;
}

export interface PredictRequest {
  track: string;
  score: number;
  page: number;
  pageSize: number;
}

export interface NamedEntity {
  nameAr: string;
}

export interface AdmissionResult {
  university: NamedEntity;
  faculty: NamedEntity;
}

export interface PredictResponse {
  results: AdmissionResult[];
  hasMore: boolean;
  totalCount: number;
}

export interface Dashboard {
  governoratesCount: number;
  facultiesCount: number;
  studentResultsCount: number;
  currentYear: number | null;
}

export interface UniversityFaculty {
  id: number;
  universityId: number;
  facultyId: number;
  universityName?: string;
  facultyName?: string;
}
export interface AdmissionYear {
  id: number;
  year: number;
  maximumScore: number;
  isCurrent: boolean;
}
export interface AdmissionCutoff {
  id: number;
  admissionYearId: number;
  universityFacultyId: number;
  track: string;
  cutoffScore: number;
  universityName?: string;
  facultyName?: string;
}

export interface PagedCutoffs {
  items: AdmissionCutoff[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface ImportResult {
  success: boolean;
  message: string;
  importedCount?: number;
  errors?: {
    rowNumber: number;
    column: string;
    errorCode: string;
    message: string;
  }[];
}

export interface StudentResult {
  seatingNo: string;
  arabicName: string;
  totalDegree: number;
  year: number;
  maximumScore: number;
}

export const TRACK_LABELS: Record<string, string> = {
  Science: 'علمي علوم',
  Mathematics: 'علمي رياضة',
  Literature: 'أدبي',
};

export const TRACK_OPTIONS = [
  { value: 'Science', label: 'علمي علوم' },
  { value: 'Mathematics', label: 'علمي رياضة' },
  { value: 'Literature', label: 'أدبي' },
];
