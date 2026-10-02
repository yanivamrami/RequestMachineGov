import { Component } from '@angular/core';
import { IdentityBarComponent } from './identity-bar.component';
import { RequestSearchComponent } from './request-search.component';

// App shell: identity bar + search page.
@Component({
  selector: 'app-root',
  imports: [IdentityBarComponent, RequestSearchComponent],
  templateUrl: './app.html',
})
export class App {}
