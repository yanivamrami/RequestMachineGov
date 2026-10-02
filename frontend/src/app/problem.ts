// [NEW] Single mapper from HttpErrorResponse (RFC 7807 ProblemDetails) to what the UI needs.
import { HttpErrorResponse } from '@angular/common/http';

export interface Problem {
  kind: 'validation' | 'auth' | 'server' | 'network';
  message: string;
  fieldErrors: Record<string, string>; // keys lower-cased: backend casing (RequestNumber vs requestNumber) is unverified
  traceId?: string;
}

export function toProblem(e: HttpErrorResponse): Problem {
  const body = (e.error ?? {}) as { title?: string; detail?: string; traceId?: string; errors?: Record<string, string[] | string> };
  if (e.status === 0) return { kind: 'network', message: 'Cannot reach the server.', fieldErrors: {} };
  if (e.status === 401) return { kind: 'auth', message: 'Set a valid user id in the identity bar.', fieldErrors: {} };
  if (e.status === 400) {
    const fieldErrors: Record<string, string> = {};
    for (const [k, v] of Object.entries(body.errors ?? {})) fieldErrors[k.toLowerCase()] = [v].flat().join(' ');
    return { kind: 'validation', message: body.detail ?? body.title ?? 'Invalid request.', fieldErrors, traceId: body.traceId };
  }
  return { kind: 'server', message: 'Something went wrong, try again.', fieldErrors: {}, traceId: body.traceId };
}
