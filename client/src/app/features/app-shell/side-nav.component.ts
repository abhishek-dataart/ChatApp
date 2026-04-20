import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  signal,
} from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import {
  LucideAngularModule,
  Search,
  Hash,
  Lock,
  Plus,
  ChevronDown,
  ChevronRight,
  Mail,
} from 'lucide-angular';
import { FriendshipsService } from '../../core/social/friendships.service';
import { RoomsService } from '../../core/rooms/rooms.service';
import { InvitationsService } from '../../core/rooms/invitations.service';
import { PresenceService } from '../../core/presence/presence.service';
import { UnreadService } from '../../core/messaging/unread.service';
import { UiBadgeComponent } from '../../shared/ui/badge/ui-badge.component';
import { UiAvatarComponent } from '../../shared/ui/avatar/ui-avatar.component';
import { UiPresenceDotComponent } from '../../shared/ui/presence-dot/ui-presence-dot.component';
import { RoomLogoComponent } from '../../shared/ui/room-logo/room-logo.component';

@Component({
  selector: 'app-side-nav',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    RouterLinkActive,
    LucideAngularModule,
    UiBadgeComponent,
    UiAvatarComponent,
    UiPresenceDotComponent,
    RoomLogoComponent,
  ],
  templateUrl: './side-nav.component.html',
  styleUrl: './side-nav.component.scss',
})
export class SideNavComponent {
  private readonly roomsService = inject(RoomsService);
  private readonly friendshipsService = inject(FriendshipsService);
  private readonly invitationsService = inject(InvitationsService);
  readonly presenceService = inject(PresenceService);
  readonly unreadService = inject(UnreadService);
  readonly router = inject(Router);

  readonly SearchIcon = Search;
  readonly HashIcon = Hash;
  readonly LockIcon = Lock;
  readonly PlusIcon = Plus;
  readonly ChevronDownIcon = ChevronDown;
  readonly ChevronRightIcon = ChevronRight;
  readonly MailIcon = Mail;

  readonly roomsExpanded = signal(true);
  readonly contactsExpanded = signal(true);
  readonly filterQuery = signal('');

  private readonly normalizedQuery = computed(() => this.filterQuery().trim().toLowerCase());

  readonly publicRooms = computed(() => {
    const q = this.normalizedQuery();
    const list = (this.roomsService.mine() ?? []).filter((r) => r.visibility === 'public');
    return q ? list.filter((r) => r.name.toLowerCase().includes(q)) : list;
  });

  readonly privateRooms = computed(() => {
    const q = this.normalizedQuery();
    const list = (this.roomsService.mine() ?? []).filter((r) => r.visibility === 'private');
    return q ? list.filter((r) => r.name.toLowerCase().includes(q)) : list;
  });

  readonly pendingRequestsCount = computed(
    () => this.friendshipsService.list()?.incoming.length ?? 0,
  );

  readonly pendingInvitationsCount = computed(
    () => this.invitationsService.incoming()?.length ?? 0,
  );

  readonly friends = computed(() => {
    const q = this.normalizedQuery();
    const list = this.friendshipsService.list()?.friends ?? [];
    if (!q) return list;
    return list.filter(
      (f) =>
        f.user.username.toLowerCase().includes(q) ||
        f.user.displayName.toLowerCase().includes(q),
    );
  });

  onFilterInput(event: Event): void {
    this.filterQuery.set((event.target as HTMLInputElement).value);
  }

  toggleRooms(): void {
    this.roomsExpanded.update((v) => !v);
  }

  toggleContacts(): void {
    this.contactsExpanded.update((v) => !v);
  }

  isRoomActive(roomId: string): boolean {
    return this.router.url.includes(roomId);
  }

  navigateNewRoom(): void {
    void this.router.navigate(['/app/rooms/public']);
  }
}
