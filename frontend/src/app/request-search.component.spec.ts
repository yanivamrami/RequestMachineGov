// [NEW] Tests 2 and 4: cursor-stack paging/reset behaviour and form validators.
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { RequestSearchComponent } from './request-search.component';

function setup() {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  const fixture = TestBed.createComponent(RequestSearchComponent);
  const http = TestBed.inject(HttpTestingController);
  const expectReq = () => http.expectOne((r) => r.url === '/api/requests');
  const page = (req: TestRequest, next: string | null) => req.flush({ items: [], nextCursor: next, hasMore: next !== null });
  fixture.detectChanges(); // runs the initial-load effect
  return { c: fixture.componentInstance, http, expectReq, page };
}

describe('RequestSearchComponent', () => {
  it('Next pushes nextCursor, Previous re-requests the earlier cursor, Search/sort reset to page 1', () => {
    const { c, http, expectReq, page } = setup();
    let r = expectReq();
    expect(r.request.params.has('cursor')).toBe(false);
    page(r, 'c1');
    c.next();
    r = expectReq();
    expect(r.request.params.get('cursor')).toBe('c1');
    page(r, 'c2');
    c.next();
    r = expectReq();
    expect(r.request.params.get('cursor')).toBe('c2');
    page(r, null);
    c.prev();
    r = expectReq();
    expect(r.request.params.get('cursor')).toBe('c1');
    page(r, 'c2');
    c.prev();
    r = expectReq();
    expect(r.request.params.has('cursor')).toBe(false); // page 1 = stack[0] = null
    page(r, 'c1');
    c.next();
    page(expectReq(), 'c2');
    c.toggleSort('requestNumber'); // sort change resets
    r = expectReq();
    expect(c.pageIndex()).toBe(0);
    expect(r.request.params.has('cursor')).toBe(false);
    expect(r.request.params.get('sortBy')).toBe('requestNumber');
    expect(r.request.params.get('sortDir')).toBe('asc');
    page(r, 'c9');
    c.next();
    page(expectReq(), null);
    c.onSubmit(); // Search resets too
    r = expectReq();
    expect(c.pageIndex()).toBe(0);
    expect(r.request.params.has('cursor')).toBe(false);
    page(r, null);
    http.verify();
  });

  it('validates requestNumber length and date range, and sends no request when invalid', () => {
    const { c, http, expectReq, page } = setup();
    page(expectReq(), null);
    const f = c.form;
    f.controls.requestNumber.setValue('ab');
    expect(f.controls.requestNumber.valid).toBe(false);
    c.onSubmit();
    http.expectNone('/api/requests');
    f.controls.requestNumber.setValue('');
    expect(f.controls.requestNumber.valid).toBe(true);
    f.patchValue({ createdFrom: '2026-02-01', createdTo: '2026-01-01' });
    expect(f.errors).toEqual({ dateRange: true });
    f.patchValue({ createdTo: '2026-02-01' });
    expect(f.valid).toBe(true);
  });
});
