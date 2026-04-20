import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  Signal,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { PresenceService } from '../../core/presence/presence.service';
import { PresenceState } from '../../core/presence/presence.models';
import { FriendshipsService } from '../../core/social/friendships.service';
import { BansService } from '../../core/social/bans.service';
import { UnreadService } from '../../core/messaging/unread.service';
import { InvitationsService } from '../../core/rooms/invitations.service';
import { UiAvatarComponent } from '../../shared/ui/avatar/ui-avatar.component';
import { UiBadgeComponent } from '../../shared/ui/badge/ui-badge.component';
import { UiButtonComponent } from '../../shared/ui/button/ui-button.component';
import { UiEmptyStateComponent } from '../../shared/ui/empty-state/ui-empty-state.component';

@Component({
  selector: 'app-contacts',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    UiAvatarComponent,
    UiBadgeComponent,
    UiButtonComponent,
    UiEmptyStateComponent,
  ],
  templateUrl: './contacts.component.html',
  styleUrl: './contacts.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ContactsComponent implements OnInit {
  private readonly friendshipsService = inject(FriendshipsService);
  private readonly bansService = inject(BansService);
  private readonly invitationsService = inject(InvitationsService);
  private readonly presenceService = inject(PresenceService);
  private readonly unread = inject(UnreadService);
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);

  dmUnreadFor(chatId: string): number {
    return this.unread.countFor('personal', chatId);
  }

  readonly list = this.friendshipsService.list;
  readonly friends = computed(() => this.list()?.friends ?? []);
  readonly incoming = computed(() => this.list()?.incoming ?? []);
  readonly outgoing = computed(() => this.list()?.outgoing ?? []);

  readonly roomInvitations = this.invitationsService.incoming;

  readonly addForm = this.fb.nonNullable.group({
    username: ['', [Validators.required]],
    note: [''],
  });

  readonly activeTab = signal<'add' | 'incoming' | 'outgoing' | 'friends' | 'rooms'>('add');

  readonly submitting = signal(false);
  readonly sendError = signal<string | null>(null);
  readonly sendSuccess = signal<string | null>(null);
  readonly actionError = signal<string | null>(null);

  presenceOf(userId: string): Signal<PresenceState> {
    return this.presenceService.stateOf(userId);
  }

  async ngOnInit(): Promise<void> {
    await Promise.all([
      this.friendshipsService.refresh(),
      this.invitationsService.refreshIncoming(),
    ]);
  }

  async sendRequest(): Promise<void> {
    if (this.addForm.invalid) {
      return;
    }
    this.submitting.set(true);
    this.sendError.set(null);
    this.sendSuccess.set(null);
    try {
      const { username, note } = this.addForm.getRawValue();
      await this.friendshipsService.sendRequest(username, note || undefined);
      this.sendSuccess.set(`Friend request sent to ${username}.`);
      this.addForm.reset();
    } catch (err) {
      this.sendError.set(this.mapSendError(err));
    } finally {
      this.submitting.set(false);
    }
  }

  async accept(id: string): Promise<void> {
    this.actionError.set(null);
    try {
      await this.friendshipsService.accept(id);
    } catch {
      this.actionError.set('Failed to accept request.');
    }
  }

  async decline(id: string): Promise<void> {
    this.actionError.set(null);
    try {
      await this.friendshipsService.decline(id);
    } catch {
      this.actionError.set('Failed to decline request.');
    }
  }

  async cancelOutgoing(id: string): Promise<void> {
    this.actionError.set(null);
    try {
      await this.friendshipsService.cancelOutgoing(id);
    } catch {
      this.actionError.set('Failed to cancel request.');
    }
  }

  async unfriend(id: string): Promise<void> {
    this.actionError.set(null);
    try {
      await this.friendshipsService.unfriend(id);
    } catch {
      this.actionError.set('Failed to remove friend.');
    }
  }

  async block(userId: string): Promise<void> {
    if (!confirm('Block this user? This will end your friendship.')) return;
    this.actionError.set(null);
    try {
      await this.bansService.block(userId);
      await this.friendshipsService.refresh();
    } catch {
      this.actionError.set('Failed to block user.');
    }
  }

  async acceptRoomInvitation(id: string, roomId: string): Promise<void> {
    this.actionError.set(null);
    try {
      await this.invitationsService.accept(id);
      await this.router.navigate(['/app/rooms', roomId]);
    } catch {
      this.actionError.set('Failed to accept invitation.');
    }
  }

  async declineRoomInvitation(id: string): Promise<void> {
    this.actionError.set(null);
    try {
      await this.invitationsService.decline(id);
    } catch {
      this.actionError.set('Failed to decline invitation.');
    }
  }

  private mapSendError(err: unknown): string {
    if (err instanceof HttpErrorResponse) {
      switch (err.error?.code) {
        case 'cannot_friend_self':
          return 'You cannot send a friend request to yourself.';
        case 'user_not_found':
          return 'User not found.';
        case 'friendship_exists':
          return 'A pending or accepted friendship already exists with this user.';
        case 'note_too_long':
          return 'Note must be 500 characters or fewer.';
      }
    }
    return 'Failed to send friend request. Please try again.';
  }
}
