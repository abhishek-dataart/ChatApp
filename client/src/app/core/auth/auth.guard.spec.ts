import { TestBed } from '@angular/core/testing';
import { Router, UrlTree } from '@angular/router';
import { anonGuard, authGuard } from './auth.guard';
import { AuthService } from './auth.service';

function runGuard(g: typeof authGuard) {
  return TestBed.runInInjectionContext(
    () => g({} as never, { url: '/' } as never) as boolean | UrlTree,
  );
}

describe('auth guards', () => {
  let auth: { isAuthenticated: jest.Mock };
  let router: Router;

  beforeEach(() => {
    auth = { isAuthenticated: jest.fn().mockReturnValue(false) };
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: auth },
        {
          provide: Router,
          useValue: {
            parseUrl: (path: string) => ({ __url: path }) as unknown as UrlTree,
          },
        },
      ],
    });
    router = TestBed.inject(Router);
  });

  it('authGuard allows authenticated users', () => {
    auth.isAuthenticated.mockReturnValue(true);
    expect(runGuard(authGuard)).toBe(true);
  });

  it('authGuard redirects anonymous users to /login', () => {
    const res = runGuard(authGuard) as UrlTree & { __url: string };
    expect(res.__url).toBe('/login');
  });

  it('anonGuard allows anonymous users', () => {
    expect(runGuard(anonGuard)).toBe(true);
  });

  it('anonGuard redirects authenticated users to /app', () => {
    auth.isAuthenticated.mockReturnValue(true);
    const res = runGuard(anonGuard) as UrlTree & { __url: string };
    expect(res.__url).toBe('/app');
  });

  it('uses Router.parseUrl for redirects', () => {
    const spy = jest.spyOn(router, 'parseUrl');
    auth.isAuthenticated.mockReturnValue(false);
    runGuard(authGuard);
    expect(spy).toHaveBeenCalledWith('/login');
  });
});
