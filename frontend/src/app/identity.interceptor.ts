// [NEW] Functional interceptor that adds the client-controlled identity headers the backend reads (demo auth).
import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { IdentityService } from './identity.service';

export const identityInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith('/api')) return next(req);
  const id = inject(IdentityService);
  const uid = id.userId();
  // [NEW] Invalid id -> send no identity headers, so the backend answers a real 401 which the UI explains.
  if (uid === null || !Number.isInteger(uid) || uid < 1) return next(req);
  return next(req.clone({ setHeaders: { 'X-User-Id': String(uid), 'X-Is-Admin': String(id.isAdmin()) } }));
};
