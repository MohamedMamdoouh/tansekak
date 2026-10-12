import { Component, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../services/api.service';
import { DEFAULT_MAXIMUM_SCORE, StudentResult } from '../../models';
import {
  applyDigitsOnlyInput,
  digitsOnlyValidator,
} from '../../form-validators';
import { formatNumber } from '../../utils/format-number.util';
import { predictQueryParams } from '../../utils/student-result.util';
import { scorePercentage, scoreProgress } from '../../utils/thanaweya-score.util';
import { submitStudentLookup } from '../../utils/student-lookup.util';

const LOOKUP_UNAVAILABLE =
  'الخدمة غير متاحة حاليًا — سيتم تفعيلها لاحقًا';

@Component({
  selector: 'app-thanaweya-result',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  templateUrl: './thanaweya-result.component.html',
  styleUrl: './thanaweya-result.component.scss',
})
export class ThanaweyaResultComponent {
  private fb = inject(FormBuilder);
  private api = inject(ApiService);
  private destroyRef = inject(DestroyRef);

  readonly fmt = formatNumber;
  lookupEnabled = false;
  readonly lookupUnavailableMessage = LOOKUP_UNAVAILABLE;

  loading = false;
  error = '';
  result: StudentResult | null = null;

  form = this.fb.group({
    seatingNo: ['', [Validators.required, digitsOnlyValidator()]],
  });

  constructor() {
    this.api
      .getConfig()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (config) => {
          this.lookupEnabled = config.studentResultsAvailable;
          if (!this.lookupEnabled) {
            this.form.disable();
          } else {
            this.form.enable();
          }
        },
        error: () => {
          this.lookupEnabled = false;
          this.form.disable();
        },
      });
  }

  get thanaweyaMaxScore(): number {
    if (!this.result?.maximumScore) return DEFAULT_MAXIMUM_SCORE;
    return this.result.maximumScore;
  }

  resultPercentage(totalDegree: number): string {
    return scorePercentage(totalDegree, this.thanaweyaMaxScore);
  }

  scoreProgress(totalDegree: number): number {
    return scoreProgress(totalDegree, this.thanaweyaMaxScore);
  }

  onSeatingInput(event: Event): void {
    applyDigitsOnlyInput(event, this.form.get('seatingNo'));
    this.error = '';
    this.result = null;
  }

  submit(): void {
    if (!this.lookupEnabled) return;

    submitStudentLookup(
      this.form,
      (seatingNo) => this.api.getThanaweyaResult(seatingNo),
      (state) => {
        if (state.loading !== undefined) this.loading = state.loading;
        if (state.error !== undefined) this.error = state.error;
        if (state.result !== undefined) this.result = state.result;
      },
    );
  }

  predictQueryParams(): { score: number } {
    return predictQueryParams(this.result);
  }
}
