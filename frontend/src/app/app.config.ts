import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { identityInterceptor } from './identity.interceptor';

// HttpClient with the interceptor that adds the demo identity headers to /api calls.
export const appConfig: ApplicationConfig = {
  providers: [provideBrowserGlobalErrorListeners(), provideHttpClient(withInterceptors([identityInterceptor]))],
};
