// [NEW] Registers HttpClient with the identity interceptor.
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { identityInterceptor } from './identity.interceptor';

// [OLD] Replaced: no HttpClient / interceptor registered
// export const appConfig: ApplicationConfig = {
//   providers: [
//     provideBrowserGlobalErrorListeners(),
//
//   ]
// };
// [NEW] Same config plus provideHttpClient so the search page can call /api with identity headers.
export const appConfig: ApplicationConfig = {
  providers: [provideBrowserGlobalErrorListeners(), provideHttpClient(withInterceptors([identityInterceptor]))],
};
