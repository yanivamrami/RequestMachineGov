// [NEW] Test 1: query-param mapping (empty omitted, repeated status, sort/page/cursor sent).
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { RequestsApi } from './requests.api';

describe('RequestsApi', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));

  it('omits empty values and sends repeated status plus sort/page/cursor', () => {
    const http = TestBed.inject(HttpTestingController);
    TestBed.inject(RequestsApi)
      .search({ requestNumber: '', status: ['New', 'InProgress'], requestType: '', createdFrom: '2026-01-01', createdTo: '', sortBy: 'status', sortDir: 'asc', pageSize: 10, cursor: 'abc' })
      .subscribe();
    const p = http.expectOne((r) => r.url === '/api/requests').request.params;
    expect(p.getAll('status')).toEqual(['New', 'InProgress']);
    expect(p.get('createdFrom')).toBe('2026-01-01');
    expect(p.get('sortBy')).toBe('status');
    expect(p.get('sortDir')).toBe('asc');
    expect(p.get('pageSize')).toBe('10');
    expect(p.get('cursor')).toBe('abc');
    for (const k of ['requestNumber', 'requestType', 'createdTo']) expect(p.has(k)).toBe(false);
    http.verify();
  });
});
