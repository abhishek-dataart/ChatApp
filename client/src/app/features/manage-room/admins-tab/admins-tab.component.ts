import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  inject,
  signal,
} from '@angular/core';
import { ModerationService } from '../../../core/rooms/moderation.service';
import { RoomDetailResponse, RoomMemberEntry } from '../../../core/rooms/rooms.models';

@Component({
  selector: 'app-admins-tab',
  standalone: true,
  imports: [],
  templateUrl: './admins-tab.component.html',
  styleUrl: './admins-tab.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminsTabComponent {
  @Input({ required: true }) room!: RoomDetailResponse;
  @Input({ required: true }) roomId!: string;
  @Output() roomUpdated = new EventEmitter<void>();

  private readonly moderation = inject(ModerationService);

  readonly acting = signal<string | null>(null);
  readonly error = signal<string | null>(null);

  get adminsAndOwner(): RoomMemberEntry[] {
    return this.room.members.filter((m) => m.role === 'owner' || m.role === 'admin');
  }

  get eligibleForPromotion(): RoomMemberEntry[] {
    return this.room.members.filter((m) => m.role === 'member');
  }

  get currentUserRole(): string {
    return this.room.currentUserRole;
  }

  async promote(member: RoomMemberEntry): Promise<void> {
    if (!confirm(`Promote ${member.user.displayName} to admin?`)) return;
    this.acting.set(member.user.id);
    this.error.set(null);
    try {
      await this.moderation.changeRole(this.roomId, member.user.id, 'admin');
      this.roomUpdated.emit();
    } catch {
      this.error.set('Failed to promote member.');
    } finally {
      this.acting.set(null);
    }
  }

  async demote(member: RoomMemberEntry): Promise<void> {
    if (!confirm(`Remove admin role from ${member.user.displayName}?`)) return;
    this.acting.set(member.user.id);
    this.error.set(null);
    try {
      await this.moderation.changeRole(this.roomId, member.user.id, 'member');
      this.roomUpdated.emit();
    } catch {
      this.error.set('Failed to demote admin.');
    } finally {
      this.acting.set(null);
    }
  }

  canDemote(member: RoomMemberEntry): boolean {
    if (member.role !== 'admin') return false;
    return this.currentUserRole === 'owner';
  }
}
