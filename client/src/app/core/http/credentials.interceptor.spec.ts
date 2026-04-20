import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { credentialsInterceptor } from './credentials.interceptor';

describe('credentialsInterceptor', () => {
  let http: HttpClient;
  let ctrl: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([credentialsInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    ctrl = TestBed.inject(HttpTestingController);
  });

  afterEach(() => ctrl.verify());

  it('sets withCredentials=true on GET', () => {
    http.get('/api/thing').subscribe();
    const req = ctrl.expectOne('/api/thing');
    expect(req.request.withCredentials).toBe(true);
    req.flush({});
  });

  it('sets withCredentials=true on POST', () => {
    http.post('/api/thing', {}).subscribe();
    const req = ctrl.expectOne('/api/thing');
    expect(req.request.withCredentials).toBe(true);
    req.flush({});
  });
});
