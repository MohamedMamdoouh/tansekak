import { Component, DestroyRef, ElementRef, NgZone, ViewChild, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ApiService, ImportUploadError } from '../../../services/api.service';
import {
  extractImportResult,
  importErrorMessage,
} from '../../../utils/import-error.util';
import { resolveApiError } from '../../../utils/api-error.util';
import { ImportUploadService } from '../../../services/import-upload.service';
import { AdmissionYearStore } from '../../../services/admission-year.store';
import {
  AdmissionYear,
  ImportResult,
  StudentResultImportEvent,
} from '../../../models';
import { ImportSuccessDialogComponent } from '../../../components/import-success-dialog/import-success-dialog.component';

const MAX_FILE_BYTES = 52_428_800;
const DELETE_CONFIRM = 'حذف';

@Component({
  selector: 'app-admin-import-results',
  standalone: true,
  imports: [CommonModule, FormsModule, ImportSuccessDialogComponent],
  templateUrl: './import-results.component.html',
  styleUrl: './import-results.component.scss',
})
export class AdminImportResultsComponent {
  private api = inject(ApiService);
  private importUpload = inject(ImportUploadService);
  private admissionYears = inject(AdmissionYearStore);
  private ngZone = inject(NgZone);
  private destroyRef = inject(DestroyRef);

  @ViewChild('fileInput') private fileInput?: ElementRef<HTMLInputElement>;

  currentYear?: AdmissionYear;
  apiAvailable: boolean | null = null;
  yearLoadError = '';
  summaryLoading = false;
  summaryError = '';
  totalCount = 0;
  latestEvent: StudentResultImportEvent | null = null;

  file: File | null = null;
  fileTouched = false;
  uploading = false;
  deleting = false;
  message = '';
  result: ImportResult | null = null;
  showSuccessDialog = false;
  successMessage = '';
  importedCount?: number;

  deleteConfirmText = '';
  showDeleteDialog = false;

  readonly maxFileMb = 50;
  readonly deleteConfirm = DELETE_CONFIRM;

  constructor() {
    this.api
      .checkHealth()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((ok) => {
        this.apiAvailable = ok;
      });

    this.admissionYears
      .refreshCurrentYear()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (year) => {
          this.currentYear = year;
          this.yearLoadError = '';
          this.refreshSummary();
        },
        error: (err: HttpErrorResponse) => {
          this.currentYear = undefined;
          this.yearLoadError = resolveApiError(
            err,
            'تعذر تحميل سنة القبول الحالية.',
          );
        },
      });
  }

  onFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.file = input.files?.[0] ?? null;
    this.fileTouched = true;
    this.message = '';
    this.result = null;

    if (this.file && this.file.size > MAX_FILE_BYTES) {
      this.message = `حجم الملف يتجاوز ${this.maxFileMb} ميجابايت.`;
      this.file = null;
      if (this.fileInput) this.fileInput.nativeElement.value = '';
    }
  }

  upload(): void {
    this.fileTouched = true;
    const yearId = this.currentYear?.id;
    if (!this.file || !yearId || this.apiAvailable === false) return;

    if (!this.file.name.toLowerCase().endsWith('.xlsx')) {
      this.message = 'يُسمح فقط بملفات .xlsx.';
      return;
    }

    this.uploading = true;
    this.message = '';
    this.result = null;

    const signal = this.importUpload.begin();

    void this.api
      .importStudentResultsWithProgress(
        yearId,
        this.file,
        ({ percent, phase }) => this.importUpload.updateProgress(percent, phase),
        signal,
      )
      .then((res) => {
        this.ngZone.run(() => {
          this.uploading = false;
          this.importUpload.finish();
          if (res.success) {
            this.openSuccessDialog(res);
            this.refreshSummary();
            return;
          }
          this.result = res;
          this.message = res.message;
        });
      })
      .catch((err: ImportUploadError) => {
        this.ngZone.run(() => {
          this.uploading = false;
          this.importUpload.finish();

          if (err.aborted) {
            this.message = 'تم إلغاء الاستيراد.';
            return;
          }

          this.result = extractImportResult(err.error);
          this.message = importErrorMessage(err.status, err.error);
        });
      });
  }

  openDeleteDialog(): void {
    this.deleteConfirmText = '';
    this.showDeleteDialog = true;
  }

  closeDeleteDialog(): void {
    this.showDeleteDialog = false;
    this.deleteConfirmText = '';
  }

  confirmDelete(): void {
    const yearId = this.currentYear?.id;
    if (!yearId || this.deleteConfirmText.trim() !== DELETE_CONFIRM) return;

    this.deleting = true;
    this.message = '';
    this.api.deleteStudentResults(yearId).subscribe({
      next: (res) => {
        this.deleting = false;
        this.showDeleteDialog = false;
        this.message = `تم حذف ${res.deletedCount} نتيجة.`;
        this.refreshSummary();
      },
      error: (err: HttpErrorResponse) => {
        this.deleting = false;
        this.message = resolveApiError(err, 'تعذر حذف النتائج.');
      },
    });
  }

  closeSuccessDialog(): void {
    this.showSuccessDialog = false;
    this.successMessage = '';
    this.importedCount = undefined;
  }

  formatEventDate(iso: string): string {
    try {
      return new Intl.DateTimeFormat('ar-EG', {
        dateStyle: 'medium',
        timeStyle: 'short',
      }).format(new Date(iso));
    } catch {
      return iso;
    }
  }

  private openSuccessDialog(result: ImportResult): void {
    this.successMessage = result.message;
    this.importedCount = result.importedCount;
    this.showSuccessDialog = true;
    this.resetForm();
  }

  private resetForm(): void {
    this.file = null;
    this.fileTouched = false;
    this.result = null;
    if (this.fileInput) this.fileInput.nativeElement.value = '';
  }

  private refreshSummary(): void {
    const yearId = this.currentYear?.id;
    if (!yearId) return;

    this.summaryLoading = true;
    this.summaryError = '';
    this.api.getStudentResultsSummary(yearId).subscribe({
      next: (summary) => {
        this.summaryLoading = false;
        this.totalCount = summary.totalCount;
        this.latestEvent = summary.latestEvent;
      },
      error: (err: HttpErrorResponse) => {
        this.summaryLoading = false;
        this.summaryError = resolveApiError(err, 'تعذر تحميل ملخص النتائج.');
      },
    });
  }
}
