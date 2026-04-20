import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { ProblemDetails } from '../../../core/auth/auth.models';
import { UiButtonComponent } from '../../../shared/ui/button/ui-button.component';
import { UiCardComponent } from '../../../shared/ui/card/ui-card.component';

function passwordPolicy(control: AbstractControl): ValidationErrors | null {
  const v = (control.value as string) ?? '';
  if (v.length < 10) return { minlength: true };
  if (!/[A-Za-z]/.test(v) || !/\d/.test(v)) return { policy: true };
  return null;
}

@Component({
  selector: 'app-reset-password',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, UiButtonComponent, UiCardComponent],
  templateUrl: './reset-password.component.html',
  styleUrl: '../login/login.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ResetPasswordComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly error = signal<string | null>(null);
  readonly submitting = signal(false);
  readonly done = signal(false);

  readonly form = this.fb.nonNullable.group({
    token: [this.route.snapshot.queryParamMap.get('token') ?? '', [Validators.required]],
    newPassword: ['', [Validators.required, passwordPolicy]],
  });

  async submit(): Promise<void> {
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();
      return;
    }
    this.submitting.set(true);
    this.error.set(null);
    try {
      await this.auth.resetPassword(this.form.getRawValue());
      this.done.set(true);
      setTimeout(() => this.router.navigateByUrl('/login'), 1500);
    } catch (err) {
      if (err instanceof HttpErrorResponse) {
        const body = err.error as ProblemDetails | null;
        this.error.set(body?.title ?? 'Reset failed.');
      } else {
        this.error.set('Something went wrong.');
      }
    } finally {
      this.submitting.set(false);
    }
  }
}
