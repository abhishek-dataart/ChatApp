import { HttpErrorResponse } from '@angular/common/http';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { LoginComponent } from './login.component';

// Replace the template so we don't depend on the shared UI components in
// isolated component tests; the component logic is what we're asserting.
@Component({ standalone: true, selector: 'app-login', template: '' })
class LoginHarness extends LoginComponent {}

describe('LoginComponent', () => {
  let auth: { login: jest.Mock };
  let router: { navigateByUrl: jest.Mock };
  let cmp: LoginComponent;

  beforeEach(() => {
    auth = { login: jest.fn().mockResolvedValue(undefined) };
    router = { navigateByUrl: jest.fn().mockResolvedValue(true) };

    TestBed.configureTestingModule({
      imports: [LoginHarness],
      providers: [
        { provide: AuthService, useValue: auth },
        { provide: Router, useValue: router },
      ],
    });
    cmp = TestBed.createComponent(LoginHarness).componentInstance;
  });

  it('starts with an invalid empty form', () => {
    expect(cmp.form.valid).toBe(false);
    expect(cmp.submitting()).toBe(false);
    expect(cmp.error()).toBeNull();
  });

  it('submit() does nothing when the form is invalid (marks touched)', async () => {
    await cmp.submit();
    expect(auth.login).not.toHaveBeenCalled();
    expect(cmp.form.controls.email.touched).toBe(true);
  });

  it('submit() calls auth.login and navigates to /app on success', async () => {
    cmp.form.setValue({ email: 'a@b.c', password: 'pw' });
    await cmp.submit();
    expect(auth.login).toHaveBeenCalledWith({ email: 'a@b.c', password: 'pw' });
    expect(router.navigateByUrl).toHaveBeenCalledWith('/app');
    expect(cmp.error()).toBeNull();
    expect(cmp.submitting()).toBe(false);
  });

  it('shows a credential error on HTTP 401', async () => {
    auth.login.mockRejectedValueOnce(
      new HttpErrorResponse({ status: 401, statusText: 'Unauthorized' }),
    );
    cmp.form.setValue({ email: 'a@b.c', password: 'pw' });
    await cmp.submit();
    expect(cmp.error()).toBe('Invalid email or password.');
  });

  it('shows a throttling error on HTTP 429', async () => {
    auth.login.mockRejectedValueOnce(
      new HttpErrorResponse({ status: 429, statusText: 'Too Many' }),
    );
    cmp.form.setValue({ email: 'a@b.c', password: 'pw' });
    await cmp.submit();
    expect(cmp.error()).toBe('Too many attempts. Try again in a minute.');
  });

  it('shows a generic error on other failures', async () => {
    auth.login.mockRejectedValueOnce(new Error('network'));
    cmp.form.setValue({ email: 'a@b.c', password: 'pw' });
    await cmp.submit();
    expect(cmp.error()).toMatch(/something went wrong/i);
  });

  it('clears the submitting flag after errors', async () => {
    auth.login.mockRejectedValueOnce(
      new HttpErrorResponse({ status: 401, statusText: '' }),
    );
    cmp.form.setValue({ email: 'a@b.c', password: 'pw' });
    await cmp.submit();
    expect(cmp.submitting()).toBe(false);
  });
});
