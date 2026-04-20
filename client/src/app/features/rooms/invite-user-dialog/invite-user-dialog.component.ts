import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  inject,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { InvitationsService } from '../../../core/rooms/invitations.service';
import { OutgoingInvitationEntry } from '../../../core/rooms/invitations.models';

@Component({
  selector: 'app-invite-user-dialog',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './invite-user-dialog.component.html',
  styleUrl: './invite-user-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class InviteUserDialogComponent {
  private readonly invitationsService = inject(InvitationsService);
  private readonly fb = inject(FormBuilder);

  @Input({ required: true }) roomId!: string;
  @Input({ required: true }) roomName!: string;
  @Output() closed = new EventEmitter<void>();
  @Output() invited = new EventEmitter<OutgoingInvitationEntry>();

  readonly form = this.fb.nonNullable.group({
    username: ['', [Validators.required]],
    note: ['', [Validators.maxLength(200)]],
  });

  readonly submitting = signal(false);
  readonly fieldErrors = signal<Record<string, string>>({});
  readonly generalError = signal<string | null>(null);

  async submit(): Promise<void> {
    if (this.form.invalid) {
      return;
    }
    this.submitting.set(true);
    this.fieldErrors.set({});
    this.generalError.set(null);

    const { username, note } = this.form.getRawValue();
    try {
      const result = await this.invitationsService.send(this.roomId, username, note || undefined);
      this.invited.emit(result);
      this.closed.emit();
    } catch (err) {
      this.mapError(err);
    } finally {
      this.submitting.set(false);
    }
  }

  private mapError(err: unknown): void {
    if (err instanceof HttpErrorResponse) {
      switch (err.error?.code) {
        case 'user_not_found':
          this.fieldErrors.set({ username: 'User not found.' });
          return;
        case 'cannot_invite_self':
          this.fieldErrors.set({ username: 'You cannot invite yourself.' });
          return;
        case 'already_member':
          this.fieldErrors.set({ username: 'This user is already a member.' });
          return;
        case 'invitation_exists':
          this.fieldErrors.set({ username: 'A pending invitation already exists for this user.' });
          return;
        case 'note_too_long':
          this.fieldErrors.set({ note: 'Note must be 200 characters or fewer.' });
          return;
        case 'not_admin_or_owner':
          this.generalError.set('You must be an admin or owner to invite users.');
          return;
        case 'room_full':
          this.generalError.set('This room is at capacity.');
          return;
      }
    }
    this.generalError.set('Failed to send invitation. Please try again.');
  }
}
