// "Viewing as" panel to switch the demo user and admin flag; the search page reacts to these signals.
import { Component, inject } from '@angular/core';
import { IdentityService } from './identity.service';

@Component({
  selector: 'app-identity-bar',
  template: `
    <!-- (change), not (input): one search per committed edit, not per keystroke. -->
    <div class="identity" role="group" aria-labelledby="identity-title">
      <div class="identity-row">
        <span id="identity-title" class="identity-title">Viewing as</span>
        <label for="uid">User</label>
        <input id="uid" type="number" min="1" step="1" [value]="identity.userId() ?? ''" #uid (change)="identity.userId.set(uid.value === '' ? null : +uid.value)" />
        <label class="switch"><input type="checkbox" [checked]="identity.isAdmin()" #adm (change)="identity.isAdmin.set(adm.checked)" /> Admin</label>
      </div>
      <span class="hint">Demo identity only, not real authentication.</span>
    </div>
  `,
})
export class IdentityBarComponent {
  protected identity = inject(IdentityService);
}
