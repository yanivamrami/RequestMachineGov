// [NEW] Shell: identity bar + search page.
import { Component } from '@angular/core';
import { IdentityBarComponent } from './identity-bar.component';
import { RequestSearchComponent } from './request-search.component';

// [OLD] Replaced: CLI placeholder component (title signal, empty imports, app.css)
// import { Component, signal } from '@angular/core';
// @Component({ selector: 'app-root', imports: [], templateUrl: './app.html', styleUrl: './app.css' })
// export class App { protected readonly title = signal('frontend'); }
// [NEW] Renders the two real components instead of the placeholder.
@Component({
  selector: 'app-root',
  imports: [IdentityBarComponent, RequestSearchComponent],
  templateUrl: './app.html',
})
export class App {}
