import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  Signal,
  inject,
  signal,
} from '@angular/core';
import { ModerationService } from '../../../core/rooms/moderation.service';
import { PresenceService } from '../../../core/presence/presence.service';
import { PresenceState } from '../../../core/presence/presence.models';
import { RoomDetailResponse, RoomMemberEntry } from '../../../core/rooms/rooms.models';
import { DatePipe } from '@angular/common';
import { UiPresenceDotComponent } from '../../../shared/ui/presence-dot/ui-presence-dot.component';

@Component({
  selector: 'app-members-tab',
  standalone: true,
  imports: [DatePipe, UiPresenceDotComponent],
  templateUrl: './members-tab.component.html',
  styleUrl: './members-tab.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MembersTabComponent {
  @Input({ required: true }) room!: RoomDetailResponse;
  @Input({ required: true }) roomId!: string;
  @Output() roomUpdated = new EventEmitter<void>();

  private readonly moderation = inject(ModerationService);
  private readonly presence = inject(PresenceService);

  readonly acting = signal<string | null>(null);
  readonly error = signal<string | null>(null);

  presenceOf(userId: string): Signal<PresenceState> {
    return this.presence.stateOf(userId);
  }

  get currentUserRole(): string {
    return this.room.currentUserRole;
  }

  canBan(member: RoomMemberEntry): boolean {
    if (this.currentUserRole === 'owner') {
      return member.role !== 'owner';
    }
    if (this.currentUserRole === 'admin') {
      return member.role === 'member';
    }
    return false;
  }

  canKick(member: RoomMemberEntry): boolean {
    if (this.currentUserRole === 'owner') return member.role !== 'owner';
    if (this.currentUserRole === 'admin') return member.role === 'member';
    return false;
  }

  async ban(member: RoomMemberEntry): Promise<void> {
    if (!confirm(`Ban ${member.user.displayName} from this room?`)) return;
    this.acting.set(member.user.id);
    this.error.set(null);
    try {
      await this.moderation.ban(this.roomId, member.user.id);
      this.roomUpdated.emit();
    } catch {
      this.error.set('Failed to ban member.');
    } finally {
      this.acting.set(null);
    }
  }

  async kick(member: RoomMemberEntry): Promise<void> {
    if (!confirm(`Remove ${member.user.displayName} from this room?`)) return;
    this.acting.set(member.user.id);
    this.error.set(null);
    try {
      await this.moderation.kick(this.roomId, member.user.id);
      this.roomUpdated.emit();
    } catch {
      this.error.set('Failed to remove member.');
    } finally {
      this.acting.set(null);
    }
  }
}
