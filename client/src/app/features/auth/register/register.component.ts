import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { ProblemDetails } from '../../../core/auth/auth.models';
import { UiButtonComponent } from '../../../shared/ui/button/ui-button.component';
import { UiCardComponent } from '../../../shared/ui/card/ui-card.component';

function passwordPolicy(control: AbstractControl): ValidationErrors | null {
  const v = (control.value as string) ?? '';
  if (v.length < 10) return { minlength: true };
  const hasLetter = /[A-Za-z]/.test(v);
  const hasDigit = /\d/.test(v);
  if (!hasLetter || !hasDigit) return { policy: true };
  return null;
}

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, UiButtonComponent, UiCardComponent],
  templateUrl: './register.component.html',
  styleUrl: './register.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegisterComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly error = signal<string | null>(null);
  readonly fieldErrors = signal<Record<string, string>>({});
  readonly submitting = signal(false);

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    username: ['', [Validators.required, Validators.pattern(/^[a-z0-9_]{3,20}$/)]],
    displayName: ['', [Validators.required, Validators.maxLength(64)]],
    password: ['', [Validators.required, passwordPolicy]],
  });

  async submit(): Promise<void> {
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();
      return;
    }
    this.submitting.set(true);
    this.error.set(null);
    this.fieldErrors.set({});
    try {
      await this.auth.register(this.form.getRawValue());
      await this.router.navigateByUrl('/app');
    } catch (err) {
      if (err instanceof HttpErrorResponse) {
        const body = err.error as ProblemDetails | null;
        const code = body?.code;
        if (err.status === 409 && code === 'email_taken') {
          this.fieldErrors.set({ email: 'Email is already registered.' });
        } else if (err.status === 409 && code === 'username_taken') {
          this.fieldErrors.set({ username: 'Username is already taken.' });
        } else if (err.status === 400) {
          this.error.set(body?.title ?? 'Invalid input.');
        } else {
          this.error.set('Something went wrong. Please try again.');
        }
      } else {
        this.error.set('Something went wrong. Please try again.');
      }
    } finally {
      this.submitting.set(false);
    }
  }
}
