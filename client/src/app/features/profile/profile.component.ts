import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { ProfileService } from '../../core/profile/profile.service';
import { UiButtonComponent } from '../../shared/ui/button/ui-button.component';
import { UiCardComponent } from '../../shared/ui/card/ui-card.component';

@Component({
  selector: 'app-profile',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, UiButtonComponent, UiCardComponent],
  templateUrl: './profile.component.html',
  styleUrl: './profile.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProfileComponent {
  private readonly auth = inject(AuthService);
  private readonly profileService = inject(ProfileService);
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);

  readonly user = this.auth.currentUser;

  readonly profileForm = this.fb.group({
    displayName: ['', [Validators.required, Validators.minLength(1), Validators.maxLength(64)]],
  });

  readonly soundForm = this.fb.group({
    soundOnMessage: [true],
  });

  readonly passwordForm = this.fb.group({
    currentPassword: ['', Validators.required],
    newPassword: ['', [Validators.required, Validators.minLength(10)]],
  });

  readonly saving = signal(false);
  readonly savingAvatar = signal(false);
  readonly savingPassword = signal(false);
  readonly deletingAccount = signal(false);
  readonly profileError = signal<string | null>(null);
  readonly avatarError = signal<string | null>(null);
  readonly passwordError = signal<string | null>(null);
  readonly passwordSuccess = signal(false);
  readonly deleteAccountError = signal<string | null>(null);

  readonly deleteAccountForm = this.fb.group({
    password: ['', Validators.required],
    confirmUsername: ['', Validators.required],
  });

  ngOnInit(): void {
    const u = this.user();
    if (u) {
      this.profileForm.patchValue({ displayName: u.displayName });
      this.soundForm.patchValue({ soundOnMessage: u.soundOnMessage });
    }
  }

  async saveProfile(): Promise<void> {
    if (this.profileForm.invalid) { return; }
    this.saving.set(true);
    this.profileError.set(null);
    try {
      const displayName = this.profileForm.value.displayName ?? undefined;
      await this.profileService.updateProfile({ displayName });
    } catch {
      this.profileError.set('Failed to update profile. Please try again.');
    } finally {
      this.saving.set(false);
    }
  }

  async saveSound(): Promise<void> {
    this.saving.set(true);
    this.profileError.set(null);
    try {
      const soundOnMessage = this.soundForm.value.soundOnMessage ?? true;
      await this.profileService.updateProfile({ soundOnMessage });
    } catch {
      this.profileError.set('Failed to save preferences.');
    } finally {
      this.saving.set(false);
    }
  }

  async onAvatarChange(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) { return; }
    this.savingAvatar.set(true);
    this.avatarError.set(null);
    try {
      await this.profileService.uploadAvatar(file);
    } catch (err) {
      const msg = err instanceof HttpErrorResponse && err.error?.code === 'unsupported_media_type'
        ? 'Only PNG, JPEG, GIF, and WEBP images are supported.'
        : 'Failed to upload avatar. Make sure the file is under 1 MB.';
      this.avatarError.set(msg);
    } finally {
      this.savingAvatar.set(false);
      input.value = '';
    }
  }

  async clearAvatar(): Promise<void> {
    this.savingAvatar.set(true);
    this.avatarError.set(null);
    try {
      await this.profileService.deleteAvatar();
    } catch {
      this.avatarError.set('Failed to remove avatar.');
    } finally {
      this.savingAvatar.set(false);
    }
  }

  async changePassword(): Promise<void> {
    if (this.passwordForm.invalid) { return; }
    this.savingPassword.set(true);
    this.passwordError.set(null);
    this.passwordSuccess.set(false);
    try {
      await this.auth.changePassword({
        currentPassword: this.passwordForm.value.currentPassword!,
        newPassword: this.passwordForm.value.newPassword!,
      });
      this.passwordSuccess.set(true);
      this.passwordForm.reset();
    } catch (err) {
      const msg = err instanceof HttpErrorResponse && err.error?.code === 'invalid_current_password'
        ? 'Current password is incorrect.'
        : 'Failed to change password.';
      this.passwordError.set(msg);
    } finally {
      this.savingPassword.set(false);
    }
  }

  async deleteAccount(): Promise<void> {
    if (this.deleteAccountForm.invalid) { return; }
    const u = this.user();
    if (!u) { return; }
    if (this.deleteAccountForm.value.confirmUsername !== u.username) {
      this.deleteAccountError.set('Username does not match.');
      return;
    }
    if (!confirm('This will permanently delete your account and all your owned rooms. This cannot be undone.')) { return; }
    this.deletingAccount.set(true);
    this.deleteAccountError.set(null);
    try {
      await this.profileService.deleteAccount(this.deleteAccountForm.value.password!);
      await this.router.navigate(['/login']);
    } catch (err) {
      const msg = err instanceof HttpErrorResponse && err.error?.code === 'invalid_current_password'
        ? 'Incorrect password.'
        : 'Failed to delete account.';
      this.deleteAccountError.set(msg);
    } finally {
      this.deletingAccount.set(false);
    }
  }
}
