// [NEW] Holds the demo identity (stand-in for real auth) and persists it across reloads.
import { Injectable, effect, signal } from '@angular/core';

const KEY = 'identity';

@Injectable({ providedIn: 'root' })
export class IdentityService {
  readonly userId = signal<number | null>(1); // default 1 so seeded data works on first load (null = empty box)
  readonly isAdmin = signal(false);

  constructor() {
    // [NEW] localStorage can throw (private mode / blocked), so every access is try/catch and the app works without it.
    try {
      const s = JSON.parse(localStorage.getItem(KEY) ?? 'null');
      if (s) {
        this.userId.set(Number.isInteger(s.userId) ? s.userId : null);
        this.isAdmin.set(s.isAdmin === true);
      }
    } catch {}
    effect(() => {
      const v = JSON.stringify({ userId: this.userId(), isAdmin: this.isAdmin() });
      try {
        localStorage.setItem(KEY, v);
      } catch {}
    });
  }
}
