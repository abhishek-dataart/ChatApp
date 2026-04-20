import { ChangeDetectionStrategy, Component, OnInit, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LucideAngularModule, Lock } from 'lucide-angular';
import { RoomsService } from '../../../core/rooms/rooms.service';
import { InvitationsService } from '../../../core/rooms/invitations.service';
import { UnreadService } from '../../../core/messaging/unread.service';
import { UiButtonComponent } from '../../../shared/ui/button/ui-button.component';
import { UiBadgeComponent } from '../../../shared/ui/badge/ui-badge.component';
import { UiEmptyStateComponent } from '../../../shared/ui/empty-state/ui-empty-state.component';
import { UiSkeletonComponent } from '../../../shared/ui/skeleton/ui-skeleton.component';
import { RoomLogoComponent } from '../../../shared/ui/room-logo/room-logo.component';

@Component({
  selector: 'app-private-rooms',
  standalone: true,
  imports: [
    RouterLink,
    LucideAngularModule,
    UiButtonComponent,
    UiBadgeComponent,
    UiEmptyStateComponent,
    UiSkeletonComponent,
    RoomLogoComponent,
  ],
  templateUrl: './private-rooms.component.html',
  styleUrl: './private-rooms.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PrivateRoomsComponent implements OnInit {
  private readonly roomsService = inject(RoomsService);
  private readonly invitationsService = inject(InvitationsService);
  private readonly unread = inject(UnreadService);

  readonly LockIcon = Lock;

  readonly mine = this.roomsService.mine;
  readonly incoming = this.invitationsService.incoming;
  readonly privateRooms = computed(() => this.mine()?.filter((r) => r.visibility === 'private') ?? null);

  unreadFor(id: string): number {
    return this.unread.countFor('room', id);
  }

  async ngOnInit(): Promise<void> {
    await Promise.all([
      this.roomsService.refreshMine(),
      this.invitationsService.refreshIncoming(),
    ]);
  }

  async acceptInvitation(id: string): Promise<void> {
    await this.invitationsService.accept(id);
  }

  async declineInvitation(id: string): Promise<void> {
    await this.invitationsService.decline(id);
  }
}
