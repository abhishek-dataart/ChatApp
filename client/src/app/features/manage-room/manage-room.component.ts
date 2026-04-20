import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { RoomsService } from '../../core/rooms/rooms.service';
import { RoomDetailResponse } from '../../core/rooms/rooms.models';
import { MembersTabComponent } from './members-tab/members-tab.component';
import { BansTabComponent } from './bans-tab/bans-tab.component';
import { AuditTabComponent } from './audit-tab/audit-tab.component';
import { SettingsTabComponent } from './settings-tab/settings-tab.component';
import { AdminsTabComponent } from './admins-tab/admins-tab.component';
import { InvitationsTabComponent } from './invitations-tab/invitations-tab.component';
import { UiTabsComponent } from '../../shared/ui/tabs/ui-tabs.component';
import { UiTabComponent } from '../../shared/ui/tabs/ui-tab.component';
import { UiSkeletonComponent } from '../../shared/ui/skeleton/ui-skeleton.component';

type Tab = 'members' | 'admins' | 'bans' | 'invitations' | 'audit' | 'settings';

@Component({
  selector: 'app-manage-room',
  standalone: true,
  imports: [
    RouterLink,
    MembersTabComponent,
    BansTabComponent,
    AuditTabComponent,
    SettingsTabComponent,
    AdminsTabComponent,
    InvitationsTabComponent,
    UiTabsComponent,
    UiTabComponent,
    UiSkeletonComponent,
  ],
  templateUrl: './manage-room.component.html',
  styleUrl: './manage-room.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ManageRoomComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly roomsService = inject(RoomsService);

  readonly room = signal<RoomDetailResponse | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly activeTab = signal<Tab>('members');

  roomId = '';

  readonly isAdminOrOwner = computed(() => {
    const role = this.room()?.currentUserRole;
    return role === 'admin' || role === 'owner';
  });

  readonly isOwner = computed(() => this.room()?.currentUserRole === 'owner');

  async ngOnInit(): Promise<void> {
    this.roomId = this.route.snapshot.paramMap.get('id') ?? '';
    try {
      const data = await this.roomsService.get(this.roomId);
      const role = data.currentUserRole;
      if (role === 'member') {
        await this.router.navigate(['/app/rooms', this.roomId]);
        return;
      }
      this.room.set(data);
    } catch (err) {
      if (err instanceof HttpErrorResponse && (err.status === 403 || err.status === 404)) {
        await this.router.navigate(['/app/rooms']);
        return;
      }
      this.error.set('Failed to load room.');
    } finally {
      this.loading.set(false);
    }
  }

  setTab(tab: Tab): void {
    this.activeTab.set(tab);
  }

  async onRoomUpdated(): Promise<void> {
    try {
      const data = await this.roomsService.get(this.roomId);
      this.room.set(data);
    } catch {
      // ignore
    }
  }

  goBack(): void {
    void this.router.navigate(['/app/rooms', this.roomId]);
  }
}
