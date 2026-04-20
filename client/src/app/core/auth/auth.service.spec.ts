import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { AuthService } from './auth.service';

const ME = {
  id: 'u1',
  email: 'a@b.c',
  username: 'alice',
  displayName: 'Alice',
  avatarUrl: null,
  soundOnMessage: true,
  currentSessionId: 's1',
};

describe('AuthService', () => {
  let service: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('starts unauthenticated', () => {
    expect(service.isAuthenticated()).toBe(false);
    expect(service.currentUser()).toBeNull();
  });

  it('bootstrap sets the user when /me returns 200', async () => {
    const p = service.bootstrap();
    http.expectOne(`${environment.apiBase}/auth/me`).flush(ME);
    await p;
    expect(service.isAuthenticated()).toBe(true);
    expect(service.currentUser()).toEqual(ME);
  });

  it('bootstrap clears user on 401', async () => {
    const p = service.bootstrap();
    http
      .expectOne(`${environment.apiBase}/auth/me`)
      .flush({ title: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });
    await p;
    expect(service.isAuthenticated()).toBe(false);
  });

  it('bootstrap treats non-401 errors as logged-out too', async () => {
    const p = service.bootstrap();
    http
      .expectOne(`${environment.apiBase}/auth/me`)
      .flush({}, { status: 500, statusText: 'Server Error' });
    await p;
    expect(service.currentUser()).toBeNull();
  });

  it('login stores the returned user', async () => {
    const p = service.login({ email: 'a@b.c', password: 'pw' });
    const req = http.expectOne(`${environment.apiBase}/auth/login`);
    expect(req.request.method).toBe('POST');
    req.flush(ME);
    await p;
    expect(service.currentUser()).toEqual(ME);
  });

  it('login propagates errors without setting the user', async () => {
    const p = service.login({ email: 'a@b.c', password: 'wrong' }).catch((e) => e);
    http
      .expectOne(`${environment.apiBase}/auth/login`)
      .flush({}, { status: 401, statusText: 'Unauthorized' });
    const err = await p;
    expect(err).toBeInstanceOf(HttpErrorResponse);
    expect(service.currentUser()).toBeNull();
  });

  it('register stores the returned user', async () => {
    const p = service.register({
      email: 'a@b.c',
      username: 'alice',
      displayName: 'Alice',
      password: 'pw',
    });
    http.expectOne(`${environment.apiBase}/auth/register`).flush(ME);
    await p;
    expect(service.currentUser()).toEqual(ME);
  });

  it('logout clears user even when the server 500s', async () => {
    const login = service.login({ email: 'a@b.c', password: 'pw' });
    http.expectOne(`${environment.apiBase}/auth/login`).flush(ME);
    await login;
    expect(service.currentUser()).toEqual(ME);

    const p = service.logout().catch(() => undefined);
    http
      .expectOne(`${environment.apiBase}/auth/logout`)
      .flush({}, { status: 500, statusText: 'err' });
    await p;
    expect(service.currentUser()).toBeNull();
  });

  it('patchLocal merges into the existing user, no-ops when null', () => {
    service.patchLocal({ displayName: 'Z' });
    expect(service.currentUser()).toBeNull();
    service.clearLocalSession();
    // seed and patch
    (service as unknown as { _currentUser: { set: (v: unknown) => void } })._currentUser.set(ME);
    service.patchLocal({ displayName: 'Alice v2' });
    expect(service.currentUser()?.displayName).toBe('Alice v2');
  });

  it('clearLocalSession resets the signal', () => {
    (service as unknown as { _currentUser: { set: (v: unknown) => void } })._currentUser.set(ME);
    service.clearLocalSession();
    expect(service.currentUser()).toBeNull();
  });
});
