import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { ToastService } from '../notifications/toast.service';
import { errorInterceptor } from './error.interceptor';

describe('errorInterceptor', () => {
  let http: HttpClient;
  let ctrl: HttpTestingController;
  let toast: { show: jest.Mock };
  let router: { navigateByUrl: jest.Mock };

  beforeEach(() => {
    toast = { show: jest.fn() };
    router = { navigateByUrl: jest.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        { provide: ToastService, useValue: toast },
        { provide: Router, useValue: router },
      ],
    });
    http = TestBed.inject(HttpClient);
    ctrl = TestBed.inject(HttpTestingController);
  });

  afterEach(() => ctrl.verify());

  it('shows a warn toast for 429', async () => {
    const p = new Promise<unknown>((res) =>
      http.get('/api/x').subscribe({ error: (e) => res(e) }),
    );
    ctrl.expectOne('/api/x').flush({}, { status: 429, statusText: 'Too Many' });
    await p;
    expect(toast.show).toHaveBeenCalledWith(
      expect.objectContaining({ severity: 'warn' }),
    );
  });

  it('shows an error toast and redirects on 403 invalid_csrf_token', async () => {
    const p = new Promise<unknown>((res) =>
      http.post('/api/x', {}).subscribe({ error: (e) => res(e) }),
    );
    ctrl
      .expectOne('/api/x')
      .flush({ error: 'invalid_csrf_token' }, { status: 403, statusText: 'Forbidden' });
    await p;
    expect(toast.show).toHaveBeenCalledWith(
      expect.objectContaining({ severity: 'error' }),
    );
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login');
  });

  it('does not toast or redirect for non-csrf 403', async () => {
    const p = new Promise<unknown>((res) =>
      http.post('/api/x', {}).subscribe({ error: (e) => res(e) }),
    );
    ctrl.expectOne('/api/x').flush({ error: 'other' }, { status: 403, statusText: 'Forbidden' });
    await p;
    expect(toast.show).not.toHaveBeenCalled();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('re-throws the error to the caller', async () => {
    const p = new Promise<{ status: number }>((res) =>
      http.get('/api/x').subscribe({ error: (e) => res(e) }),
    );
    ctrl.expectOne('/api/x').flush({}, { status: 500, statusText: 'err' });
    const err = await p;
    expect(err.status).toBe(500);
  });
});
