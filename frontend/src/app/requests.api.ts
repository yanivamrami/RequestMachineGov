// Contract types for GET /api/requests and the one HTTP call that maps filters to query params.
import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';

export type Status = 'New' | 'InProgress' | 'Completed' | 'Cancelled';
export type RequestType = 'General' | 'Legal' | 'Payment' | 'Appeal';
export type SortBy = 'createdAt' | 'requestNumber' | 'status' | 'requestType';
export type SortDir = 'asc' | 'desc';

export interface RequestDto {
  id: number;
  requestNumber: string;
  customerId: number;
  ownerId: number;
  assignedToUserId: number | null;
  status: Status;
  requestType: RequestType;
  createdAt: string;
}

export interface Page {
  items: RequestDto[];
  nextCursor: string | null;
  hasMore: boolean;
}

export interface SearchParams {
  requestNumber?: string;
  status?: Status[];
  requestType?: RequestType | '';
  createdFrom?: string;
  createdTo?: string;
  sortBy: SortBy;
  sortDir: SortDir;
  pageSize: number;
  cursor?: string | null;
}

@Injectable({ providedIn: 'root' })
export class RequestsApi {
  private http = inject(HttpClient);

  search(p: SearchParams) {
    let params = new HttpParams().set('sortBy', p.sortBy).set('sortDir', p.sortDir).set('pageSize', p.pageSize);
    // Empty values are never sent (no `requestNumber=`), so the backend sees "no filter".
    for (const k of ['requestNumber', 'requestType', 'createdFrom', 'createdTo', 'cursor'] as const) {
      const v = p[k];
      if (v) params = params.set(k, v);
    }
    // Multi-status = repeated `status=` params, which the backend binds to an array for IN (...).
    for (const s of p.status ?? []) params = params.append('status', s);
    return this.http.get<Page>('/api/requests', { params });
  }
}
