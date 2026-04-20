import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { LucideAngularModule, Hash, Lock, Settings, Users } from 'lucide-angular';
import { ContextPanelService } from '../../core/context/context-panel.service';
import { AuthService } from '../../core/auth/auth.service';
import { FriendshipsService } from '../../core/social/friendships.service';
import { BansService } from '../../core/social/bans.service';
import { PresenceService } from '../../core/presence/presence.service';
import { ToastService } from '../../core/notifications/toast.service';
import { RoomsService } from '../../core/rooms/rooms.service';
import { SignalrService } from '../../core/signalr/signalr.service';
import { RoomDetailResponse, RoomMemberEntry } from '../../core/rooms/rooms.models';
import { UiAvatarComponent } from '../../shared/ui/avatar/ui-avatar.component';
import { UiButtonComponent } from '../../shared/ui/button/ui-button.component';
import { UiBadgeComponent } from '../../shared/ui/badge/ui-badge.component';
import { UiPresenceDotComponent } from '../../shared/ui/presence-dot/ui-presence-dot.component';
import { UiSkeletonComponent } from '../../shared/ui/skeleton/ui-skeleton.component';
import { RoomLogoComponent } from '../../shared/ui/room-logo/room-logo.component';

@Component({
  selector: 'app-context-panel',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    LucideAngularModule,
    UiAvatarComponent,
    UiButtonComponent,
    UiBadgeComponent,
    UiPresenceDotComponent,
    UiSkeletonComponent,
    RoomLogoComponent,
  ],
  templateUrl: './context-panel.component.html',
  styleUrl: './context-panel.component.scss',
})
export class ContextPanelComponent {
  readonly contextPanel = inject(ContextPanelService);
  private readonly auth = inject(AuthService);
  private readonly friendships = inject(FriendshipsService);
  private readonly bans = inject(BansService);
  readonly presence = inject(PresenceService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly rooms = inject(RoomsService);
  private readonly signalr = inject(SignalrService);
  private readonly destroyRef = inject(DestroyRef);

  readonly HashIcon = Hash;
  readonly LockIcon = Lock;
  readonly SettingsIcon = Settings;
  readonly UsersIcon = Users;

  readonly content = this.contextPanel.content;
  readonly busy = signal(false);
  readonly currentUserId = computed(() => this.auth.currentUser()?.id ?? null);

  readonly room = signal<RoomDetailResponse | null>(null);
  readonly roomLoading = signal(false);

  readonly dmPartner = computed(() => {
    const c = this.content();
    if (c.type !== 'dm') return null;
    const friend = this.friendships
      .list()
      ?.friends.find((f) => f.personalChatId === c.chatId || f.user.id === c.partnerId);
    return friend ?? null;
  });

  readonly owner = computed(() => this.room()?.owner ?? null);

  readonly admins = computed<RoomMemberEntry[]>(() => {
    const r = this.room();
    if (!r) return [];
    return r.members.filter((m) => m.role === 'admin');
  });

  readonly regularMembers = computed<RoomMemberEntry[]>(() => {
    const r = this.room();
    if (!r) return [];
    const list = r.members.filter((m) => m.role === 'member');
    const rank = (id: string): number => {
      const s = this.presence.stateOf(id)();
      return s === 'online' ? 0 : s === 'afk' ? 1 : 2;
    };
    return [...list].sort((a, b) => {
      const pa = rank(a.user.id);
      const pb = rank(b.user.id);
      if (pa !== pb) return pa - pb;
      const na = (a.user.displayName || a.user.username).toLowerCase();
      const nb = (b.user.displayName || b.user.username).toLowerCase();
      return na.localeCompare(nb);
    });
  });

  readonly canManage = computed(() => {
    const r = this.room();
    return r?.currentUserRole === 'owner' || r?.currentUserRole === 'admin';
  });

  readonly capacityPercent = computed(() => {
    const r = this.room();
    if (!r) return 0;
    return Math.round((r.memberCount / r.capacity) * 100);
  });

  readonly capacityNearFull = computed(() => this.capacityPercent() >= 95);

  constructor() {
    effect(() => {
      const c = this.content();
      if (c.type === 'room') {
        void this.loadRoom(c.roomId);
      } else {
        this.room.set(null);
      }
    });

    this.signalr.roomMemberChanged$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((payload) => {
        const current = this.room();
        if (!current || current.id !== payload.roomId) return;
        void this.loadRoom(payload.roomId, true);
      });

    this.signalr.roomDeleted$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((payload) => {
        const current = this.room();
        if (current && current.id === payload.roomId) {
          this.room.set(null);
        }
      });
  }

  private async loadRoom(roomId: string, silent = false): Promise<void> {
    if (!silent) this.roomLoading.set(true);
    try {
      const data = await this.rooms.get(roomId);
      const c = this.content();
      if (c.type === 'room' && c.roomId === roomId) {
        this.room.set(data);
      }
    } catch {
      if (!silent) this.room.set(null);
    } finally {
      if (!silent) this.roomLoading.set(false);
    }
  }

  async openManage(): Promise<void> {
    const r = this.room();
    if (!r) return;
    await this.router.navigate(['/app/rooms', r.id, 'manage']);
  }

  async unfriend(): Promise<void> {
    const f = this.dmPartner();
    if (!f || this.busy()) return;
    if (!confirm(`Unfriend ${f.user.displayName || f.user.username}?`)) return;
    this.busy.set(true);
    try {
      await this.friendships.unfriend(f.friendshipId);
      this.toast.show({ severity: 'info', message: 'Unfriended.' });
      await this.router.navigate(['/app/contacts']);
    } catch {
      this.toast.show({ severity: 'error', message: 'Failed to unfriend.' });
    } finally {
      this.busy.set(false);
    }
  }

  async block(): Promise<void> {
    const c = this.content();
    if (c.type !== 'dm' || this.busy()) return;
    const f = this.dmPartner();
    const name = f?.user.displayName || f?.user.username || 'this user';
    if (!confirm(`Block ${name}? This also removes the friendship.`)) return;
    this.busy.set(true);
    try {
      if (f) {
        try {
          await this.friendships.unfriend(f.friendshipId);
        } catch {
          // If already gone, ignore
        }
      }
      await this.bans.block(c.partnerId);
      this.toast.show({ severity: 'info', message: `Blocked ${name}.` });
      await this.router.navigate(['/app/contacts']);
    } catch {
      this.toast.show({ severity: 'error', message: 'Failed to block user.' });
    } finally {
      this.busy.set(false);
    }
  }
}
