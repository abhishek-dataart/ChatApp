import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { csrfInterceptor } from './csrf.interceptor';

describe('csrfInterceptor', () => {
  let http: HttpClient;
  let ctrl: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([csrfInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    ctrl = TestBed.inject(HttpTestingController);
    document.cookie = 'csrf_token=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/';
  });

  afterEach(() => {
    ctrl.verify();
    document.cookie = 'csrf_token=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/';
  });

  it('does not add the header to GET requests', () => {
    document.cookie = 'csrf_token=abc';
    http.get('/api/x').subscribe();
    const req = ctrl.expectOne('/api/x');
    expect(req.request.headers.has('X-Csrf-Token')).toBe(false);
    req.flush({});
  });

  it('adds X-Csrf-Token on POST when cookie is present', () => {
    document.cookie = 'csrf_token=tok123';
    http.post('/api/x', {}).subscribe();
    const req = ctrl.expectOne('/api/x');
    expect(req.request.headers.get('X-Csrf-Token')).toBe('tok123');
    req.flush({});
  });

  it('adds X-Csrf-Token on PUT, PATCH, DELETE', () => {
    document.cookie = 'csrf_token=tok';
    http.put('/a', {}).subscribe();
    http.patch('/a', {}).subscribe();
    http.delete('/a').subscribe();
    const reqs = ctrl.match('/a');
    expect(reqs).toHaveLength(3);
    for (const r of reqs) {
      expect(r.request.headers.get('X-Csrf-Token')).toBe('tok');
      r.flush({});
    }
  });

  it('skips when cookie missing', () => {
    http.post('/api/x', {}).subscribe();
    const req = ctrl.expectOne('/api/x');
    expect(req.request.headers.has('X-Csrf-Token')).toBe(false);
    req.flush({});
  });

  it('decodes URL-encoded cookie values', () => {
    document.cookie = 'csrf_token=a%2Bb%3D';
    http.post('/api/x', {}).subscribe();
    const req = ctrl.expectOne('/api/x');
    expect(req.request.headers.get('X-Csrf-Token')).toBe('a+b=');
    req.flush({});
  });
});
