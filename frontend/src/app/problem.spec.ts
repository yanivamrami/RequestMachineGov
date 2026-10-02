// [NEW] Test 3: error mapping for 400 / 401 / 500 / network.
import { HttpErrorResponse } from '@angular/common/http';
import { toProblem } from './problem';

describe('toProblem', () => {
  it('maps 400 errors (keys lower-cased), 401, 500 with traceId, and status 0', () => {
    const p400 = toProblem(new HttpErrorResponse({ status: 400, error: { errors: { RequestNumber: ['too short', 'bad'] } } }));
    expect(p400.kind).toBe('validation');
    expect(p400.fieldErrors).toEqual({ requestnumber: 'too short bad' });
    expect(toProblem(new HttpErrorResponse({ status: 401 })).kind).toBe('auth');
    const p500 = toProblem(new HttpErrorResponse({ status: 500, error: { traceId: 'T1' } }));
    expect(p500).toMatchObject({ kind: 'server', traceId: 'T1' });
    expect(toProblem(new HttpErrorResponse({ status: 0 })).kind).toBe('network');
  });
});
