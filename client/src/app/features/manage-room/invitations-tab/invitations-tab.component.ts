import {
  ChangeDetectionStrategy,
  Component,
  Input,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { InvitationsService } from '../../../core/rooms/invitations.service';
import { OutgoingInvitationEntry } from '../../../core/rooms/invitations.models';
import { InviteUserDialogComponent } from '../../rooms/invite-user-dialog/invite-user-dialog.component';
import { RoomDetailResponse } from '../../../core/rooms/rooms.models';

@Component({
  selector: 'app-invitations-tab',
  standalone: true,
  imports: [InviteUserDialogComponent],
  templateUrl: './invitations-tab.component.html',
  styleUrl: './invitations-tab.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class InvitationsTabComponent implements OnInit {
  @Input({ required: true }) room!: RoomDetailResponse;
  @Input({ required: true }) roomId!: string;

  private readonly invitationsService = inject(InvitationsService);

  readonly showInviteDialog = signal(false);
  readonly pendingInvitations = signal<OutgoingInvitationEntry[] | null>(null);
  readonly loadingPending = signal(false);

  async ngOnInit(): Promise<void> {
    await this.loadPending();
  }

  openInviteDialog(): void {
    this.showInviteDialog.set(true);
  }

  closeInviteDialog(): void {
    this.showInviteDialog.set(false);
  }

  onInvited(entry: OutgoingInvitationEntry): void {
    this.pendingInvitations.update((list) => (list ? [entry, ...list] : [entry]));
  }

  async loadPending(): Promise<void> {
    this.loadingPending.set(true);
    try {
      const list = await this.invitationsService.listOutgoing(this.roomId);
      this.pendingInvitations.set(list);
    } catch {
      this.pendingInvitations.set([]);
    } finally {
      this.loadingPending.set(false);
    }
  }

  async revokeInvitation(invitationId: string): Promise<void> {
    try {
      await this.invitationsService.revoke(invitationId);
      this.pendingInvitations.update((list) => list?.filter((i) => i.invitationId !== invitationId) ?? []);
    } catch {
      // ignore
    }
  }
}
