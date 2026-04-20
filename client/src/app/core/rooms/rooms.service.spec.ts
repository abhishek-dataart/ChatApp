import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { RoomsService } from './rooms.service';

describe('RoomsService', () => {
  let svc: RoomsService;
  let http: HttpTestingController;
  const base = `${environment.apiBase}/rooms`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    svc = TestBed.inject(RoomsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('refreshCatalog stores the result', async () => {
    const p = svc.refreshCatalog();
    const req = http.expectOne((r) => r.url === base);
    expect(req.request.method).toBe('GET');
    req.flush([{ id: 'r1', name: 'A' }]);
    await p;
    expect(svc.catalog()).toEqual([{ id: 'r1', name: 'A' }]);
  });

  it('refreshCatalog includes the q parameter when supplied', async () => {
    const p = svc.refreshCatalog('needle');
    const req = http.expectOne((r) => r.url === base);
    expect(req.request.params.get('q')).toBe('needle');
    req.flush([]);
    await p;
  });

  it('refreshMine stores into the mine signal', async () => {
    const p = svc.refreshMine();
    http.expectOne(`${base}/mine`).flush([{ id: 'r1' }]);
    await p;
    expect(svc.mine()).toHaveLength(1);
  });

  it('create posts then refreshes catalog + mine', async () => {
    const p = svc.create({ name: 'new' } as never);
    http.expectOne(base).flush({ id: 'rx', name: 'new' });
    await Promise.resolve();
    await Promise.resolve();
    http.expectOne(base).flush([]);
    http.expectOne(`${base}/mine`).flush([]);
    const res = await p;
    expect(res.id).toBe('rx');
  });

  it('join posts and refreshes', async () => {
    const p = svc.join('r1');
    http.expectOne(`${base}/r1/join`).flush({ id: 'r1' });
    await Promise.resolve();
    await Promise.resolve();
    http.expectOne(base).flush([]);
    http.expectOne(`${base}/mine`).flush([]);
    const res = await p;
    expect(res.id).toBe('r1');
  });

  it('delete issues DELETE then refreshes', async () => {
    const p = svc.delete('r1');
    const req = http.expectOne(`${base}/r1`);
    expect(req.request.method).toBe('DELETE');
    req.flush({});
    await Promise.resolve();
    await Promise.resolve();
    http.expectOne(base).flush([]);
    http.expectOne(`${base}/mine`).flush([]);
    await p;
  });

  it('update uses PATCH on the room id', async () => {
    const p = svc.update('r1', { name: 'z' } as never);
    const req = http.expectOne(`${base}/r1`);
    expect(req.request.method).toBe('PATCH');
    req.flush({ id: 'r1', name: 'z' });
    const res = await p;
    expect(res.name).toBe('z');
  });

  it('updateCapacity PATCHes capacity', async () => {
    const p = svc.updateCapacity('r1', 50);
    const req = http.expectOne(`${base}/r1/capacity`);
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ capacity: 50 });
    req.flush({ id: 'r1', capacity: 50 });
    await p;
  });
});
