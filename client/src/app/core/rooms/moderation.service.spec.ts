import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { ModerationService } from './moderation.service';

describe('ModerationService', () => {
  let svc: ModerationService;
  let http: HttpTestingController;
  const base = `${environment.apiBase}/rooms`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    svc = TestBed.inject(ModerationService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('listBans stores the returned bans', async () => {
    const p = svc.listBans('r1');
    http
      .expectOne(`${base}/r1/bans`)
      .flush({ bans: [{ userId: 'u1', bannedAt: '2026-01-01T00:00:00Z' }] });
    const res = await p;
    expect(res).toHaveLength(1);
    expect(svc.bans()).toHaveLength(1);
  });

  it('ban POSTs a userId', async () => {
    const p = svc.ban('r1', 'u2');
    const req = http.expectOne(`${base}/r1/bans`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ userId: 'u2' });
    req.flush({});
    await p;
  });

  it('unban DELETEs the user ban', async () => {
    const p = svc.unban('r1', 'u2');
    const req = http.expectOne(`${base}/r1/bans/u2`);
    expect(req.request.method).toBe('DELETE');
    req.flush({});
    await p;
  });

  it('changeRole PATCHes the role', async () => {
    const p = svc.changeRole('r1', 'u2', 'moderator');
    const req = http.expectOne(`${base}/r1/members/u2/role`);
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ role: 'moderator' });
    req.flush({});
    await p;
  });

  it('kick DELETEs the member', async () => {
    const p = svc.kick('r1', 'u2');
    const req = http.expectOne(`${base}/r1/members/u2`);
    expect(req.request.method).toBe('DELETE');
    req.flush({});
    await p;
  });

  it('listAudit forwards limit and before params', async () => {
    const p = svc.listAudit('r1', '2026-01-01T00:00:00Z', 10);
    const req = http.expectOne((r) => r.url === `${base}/r1/audit`);
    expect(req.request.params.get('limit')).toBe('10');
    expect(req.request.params.get('before')).toBe('2026-01-01T00:00:00Z');
    req.flush({ entries: [] });
    await p;
  });
});
