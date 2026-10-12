import { HttpErrorResponse } from '@angular/common/http';
import { SESSION_EXPIRED_MESSAGE } from '../constants/auth-messages';
import { ApiResponse } from '../models';

export function resolveApiError(
  err:
    | HttpErrorResponse
    | { status?: number; error?: ApiResponse<unknown> | null },
  fallback: string,
): string {
  const status = err.status ?? 0;
  const body = err.error as ApiResponse<unknown> | null | undefined;

  if (status === 0) {
    return 'تعذر الاتصال بالخادم. تحقق من اتصال الإنترنت.';
  }

  if (body?.message) {
    return body.message;
  }

  if (status === 401 || status === 403) {
    return SESSION_EXPIRED_MESSAGE;
  }

  if (status === 429) {
    return body?.message ?? 'طلبات كثيرة. حاول مرة أخرى بعد دقيقة.';
  }

  if (status >= 500) {
    return 'حدث خطأ في الخادم. حاول مرة أخرى لاحقًا.';
  }

  return fallback;
}
