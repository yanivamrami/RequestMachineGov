// [NEW] The search page: filter form, sortable table, cursor-stack pager and loading/empty/error states.
import { DatePipe } from '@angular/common';
import { Component, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Subject, catchError, map, of, switchMap, tap } from 'rxjs';
import { IdentityService } from './identity.service';
import { Problem, toProblem } from './problem';
import { Page, RequestDto, RequestType, RequestsApi, SearchParams, SortBy, SortDir, Status } from './requests.api';

// [NEW] Mirrors the backend rule createdFrom <= createdTo. yyyy-MM-dd strings compare correctly as text.
export function dateRangeValidator(g: AbstractControl): ValidationErrors | null {
  const from = g.get('createdFrom')?.value;
  const to = g.get('createdTo')?.value;
  return from && to && from > to ? { dateRange: true } : null;
}

const DEFAULT_SORT = { sortBy: 'createdAt' as SortBy, sortDir: 'desc' as SortDir }; // backend defaults
type Filters = Omit<SearchParams, 'sortBy' | 'sortDir' | 'cursor'>;

@Component({
  selector: 'app-request-search',
  imports: [ReactiveFormsModule, DatePipe],
  templateUrl: './request-search.component.html',
  styleUrl: './request-search.component.css',
})
export class RequestSearchComponent {
  private api = inject(RequestsApi);
  private identity = inject(IdentityService);

  readonly statuses: Status[] = ['New', 'InProgress', 'Completed', 'Cancelled'];
  readonly types: RequestType[] = ['General', 'Legal', 'Payment', 'Appeal'];
  readonly sizes = [10, 25, 50, 100];
  // [NEW] Exactly the backend sort whitelist is sortable; the other columns have no sortBy.
  readonly cols: { label: string; sortBy?: SortBy }[] = [
    { label: 'Id' },
    { label: 'Request number', sortBy: 'requestNumber' },
    { label: 'Customer' },
    { label: 'Owner' },
    { label: 'Assigned to' },
    { label: 'Status', sortBy: 'status' },
    { label: 'Type', sortBy: 'requestType' },
    { label: 'Created (UTC)', sortBy: 'createdAt' },
  ];

  // [NEW] Client validators mirror the backend (UX only; backend stays authoritative). minLength/maxLength ignore empty values.
  readonly form = new FormGroup(
    {
      requestNumber: new FormControl('', { nonNullable: true, validators: [Validators.minLength(3), Validators.maxLength(20)] }),
      status: new FormControl<Status[]>([], { nonNullable: true }),
      requestType: new FormControl<RequestType | ''>('', { nonNullable: true }),
      createdFrom: new FormControl('', { nonNullable: true }),
      createdTo: new FormControl('', { nonNullable: true }),
      pageSize: new FormControl(25, { nonNullable: true }),
    },
    { validators: dateRangeValidator },
  );

  readonly rows = signal<RequestDto[]>([]);
  readonly hasMore = signal(false);
  readonly loading = signal(true);
  readonly pageIndex = signal(0);
  readonly sort = signal(DEFAULT_SORT);
  readonly error = signal<{ text: string; traceId?: string } | null>(null);
  readonly serverErrors = signal<Record<string, string>>({}); // 400 field errors, keyed by lower-cased control name

  // [NEW] stack[i] = cursor used to fetch page i (stack[0] = null). Previous re-fetches stack[i-1]; backend has no backward keyset.
  private stack: (string | null)[] = [null];
  private nextCursor: string | null = null;
  private applied: Filters = this.snapshot(); // filters as of the last Search, so editing the form doesn't change paging
  private load$ = new Subject<string | null>();

  constructor() {
    // [NEW] switchMap cancels the in-flight request on a newer load, so a stale response never overwrites a newer one.
    // catchError is INSIDE the inner pipe so an HTTP error doesn't complete the outer stream.
    this.load$
      .pipe(
        tap(() => this.loading.set(true)),
        switchMap((cursor) =>
          this.api.search({ ...this.applied, ...this.sort(), cursor }).pipe(
            map((page): Page | Problem => page),
            catchError((e) => of(toProblem(e))),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((r) => {
        this.loading.set(false);
        if ('items' in r) {
          this.rows.set(r.items);
          this.hasMore.set(r.hasMore);
          this.nextCursor = r.nextCursor;
          this.error.set(null);
        } else this.showProblem(r);
      });

    // [NEW] Identity change (and first render) -> fresh search from page 1 with the applied filters.
    // [OLD] Replaced (review F4): restart() only reset paging, so the previous identity's rows stayed readable under
    // the new "Viewing as" label until the new response arrived (longer if that request stalled or failed).
    // effect(() => {
    //   this.identity.userId();
    //   this.identity.isAdmin();
    //   untracked(() => this.restart());
    // });
    // [NEW] Rows belong to an identity: clear them (and paging/error state) the moment the identity changes,
    // before the new search starts. Ordinary paging still keeps rows on screen while the next page loads.
    effect(() => {
      this.identity.userId();
      this.identity.isAdmin();
      untracked(() => {
        this.rows.set([]);
        this.hasMore.set(false);
        this.nextCursor = null;
        this.error.set(null);
        this.serverErrors.set({});
        this.restart();
      });
    });
  }

  // [NEW] Search / Reset / sort / pageSize / identity all funnel here: stack reset + page 0, no cursor.
  private restart() {
    this.stack = [null];
    this.pageIndex.set(0);
    this.load$.next(null);
  }

  private snapshot(): Filters {
    const v = this.form.getRawValue();
    return { ...v, requestNumber: v.requestNumber.trim() };
  }

  onSubmit() {
    this.form.markAllAsTouched();
    if (this.form.invalid) return; // no request for invalid input
    this.applied = this.snapshot();
    this.serverErrors.set({});
    this.restart();
  }

  reset() {
    this.form.reset();
    this.sort.set(DEFAULT_SORT);
    this.applied = this.snapshot();
    this.serverErrors.set({});
    this.restart();
  }

  changePageSize() {
    this.applied = { ...this.applied, pageSize: this.form.controls.pageSize.value };
    this.restart();
  }

  toggleStatus(s: Status, checked: boolean) {
    const c = this.form.controls.status;
    c.setValue(checked ? [...c.value, s] : c.value.filter((x) => x !== s));
  }

  toggleSort(by: SortBy) {
    const s = this.sort();
    this.sort.set(
      s.sortBy === by
        ? { sortBy: by, sortDir: s.sortDir === 'asc' ? 'desc' : 'asc' }
        : { sortBy: by, sortDir: by === 'createdAt' ? 'desc' : 'asc' },
    );
    this.restart();
  }

  // [NEW] Human label for a status enum value ('InProgress' -> 'In progress'); the API value stays unchanged.
  label(s: Status) {
    return s === 'InProgress' ? 'In progress' : s;
  }

  ariaSort(by: SortBy) {
    const s = this.sort();
    return s.sortBy !== by ? 'none' : s.sortDir === 'asc' ? 'ascending' : 'descending';
  }

  next() {
    if (!this.hasMore() || this.loading()) return;
    const i = this.pageIndex() + 1;
    this.stack = [...this.stack.slice(0, i), this.nextCursor];
    this.pageIndex.set(i);
    this.load$.next(this.nextCursor);
  }

  prev() {
    if (this.pageIndex() === 0 || this.loading()) return;
    const i = this.pageIndex() - 1;
    this.pageIndex.set(i);
    this.load$.next(this.stack[i]);
  }

  private showProblem(p: Problem) {
    this.rows.set([]);
    this.hasMore.set(false);
    const known = new Set(Object.keys(this.form.controls).map((k) => k.toLowerCase()));
    const entries = Object.entries(p.fieldErrors);
    this.serverErrors.set(Object.fromEntries(entries.filter(([k]) => known.has(k))));
    const other = entries.filter(([k]) => !known.has(k)).map(([, m]) => m);
    // [NEW] Field errors show beside the field; anything unmatched (e.g. bad cursor) goes to the banner.
    const text = p.kind !== 'validation' ? p.message : other.length ? other.join(' ') : entries.length ? 'Please fix the highlighted fields.' : p.message;
    this.error.set({ text, traceId: p.traceId });
  }

  // [NEW] Message for a field: client rule (after touch) first, then the server's 400 message.
  err(name: 'requestNumber' | 'createdFrom' | 'createdTo' | 'requestType' | 'status' | 'pageSize'): string | null {
    const c = this.form.controls[name];
    if (c.touched && c.errors?.['minlength']) return 'Enter at least 3 characters.';
    if (c.touched && c.errors?.['maxlength']) return 'At most 20 characters.';
    if (name === 'createdTo' && this.form.touched && this.form.errors?.['dateRange']) return '"From" must be on or before "To".';
    return this.serverErrors()[name.toLowerCase()] ?? null;
  }
}
