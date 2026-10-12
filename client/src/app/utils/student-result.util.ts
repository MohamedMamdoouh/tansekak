import { StudentResult } from '../models';

export function predictQueryParams(
  result: StudentResult | null,
): { score: number } {
  return { score: result?.totalDegree ?? 0 };
}
