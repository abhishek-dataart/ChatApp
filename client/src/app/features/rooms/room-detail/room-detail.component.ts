import {
  ChangeDetectionStrategy,
  Component,
  OnDestroy,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { Subscription } from 'rxjs';
import { LucideAngularModule, Hash, Lock, Settings } from 'lucide-angular';
import { RoomsService } from '../../../core/rooms/rooms.service';
import { RoomDetailResponse } from '../../../core/rooms/rooms.models';
import { AuthService } from '../../../core/auth/auth.service';
import { RoomMessagingService } from '../../../core/messaging/room-messaging.service';
import { MessageResponse } from '../../../core/messaging/messaging.models';
import { UnreadService } from '../../../core/messaging/unread.service';
import { SignalrService } from '../../../core/signalr/signalr.service';
import { ToastService } from '../../../core/notifications/toast.service';
import { ContextPanelService } from '../../../core/context/context-panel.service';
import { MessageListComponent } from '../../../shared/messaging/message-list.component';
import { MessageComposerComponent } from '../../../shared/messaging/message-composer.component';
import { UiButtonComponent } from '../../../shared/ui/button/ui-button.component';
import { UiBadgeComponent } from '../../../shared/ui/badge/ui-badge.component';
import { UiSkeletonComponent } from '../../../shared/ui/skeleton/ui-skeleton.component';
import { RoomLogoComponent } from '../../../shared/ui/room-logo/room-logo.component';

@Component({
  selector: 'app-room-detail',
  standalone: true,
  imports: [
    LucideAngularModule,
    MessageListComponent,
    MessageComposerComponent,
    UiButtonComponent,
    UiBadgeComponent,
    UiSkeletonComponent,
    RoomLogoComponent,
  ],
  templateUrl: './room-detail.component.html',
  styleUrl: './room-detail.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RoomDetailComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly roomsService = inject(RoomsService);
  private readonly auth = inject(AuthService);
  private readonly signalr = inject(SignalrService);
  private readonly toast = inject(ToastService);
  private readonly contextPanel = inject(ContextPanelService);
  readonly roomMessaging = inject(RoomMessagingService);
  private readonly unread = inject(UnreadService);

  readonly HashIcon = Hash;
  readonly LockIcon = Lock;
  readonly SettingsIcon = Settings;

  readonly room = signal<RoomDetailResponse | null>(null);
  readonly loading = signal(true);
  readonly leaving = signal(false);
  readonly sending = signal(false);
  readonly error = signal<string | null>(null);
  readonly replyingTo = computed(() => this.roomMessaging.replyingTo());

  readonly capacityNearFull = computed(() => {
    const r = this.room();
    if (!r) return false;
    return r.memberCount / r.capacity >= 0.95;
  });

  readonly capacityFull = computed(() => {
    const r = this.room();
    if (!r) return false;
    return r.memberCount >= r.capacity;
  });

  readonly capacityPercent = computed(() => {
    const r = this.room();
    if (!r) return 0;
    return Math.round((r.memberCount / r.capacity) * 100);
  });

  private roomId = '';
  private subs: Subscription[] = [];

  async ngOnInit(): Promise<void> {
    this.subs.push(
      this.route.paramMap.subscribe(async (params) => {
        const newId = params.get('id') ?? '';
        if (newId === this.roomId) return;
        this.roomMessaging.unsubscribe();
        this.contextPanel.clear();
        this.roomId = newId;
        this.loading.set(true);
        this.room.set(null);
        this.error.set(null);
        try {
          const data = await this.roomsService.get(this.roomId);
          this.room.set(data);
          await this.roomMessaging.loadHistory(this.roomId);
          this.roomMessaging.subscribe(this.roomId);
          this.unread.setActive('room', this.roomId);
          this.contextPanel.set({ type: 'room', roomId: this.roomId });
        } catch (err) {
          if (err instanceof HttpErrorResponse && (err.status === 403 || err.status === 404)) {
            await this.router.navigate(['/app/rooms']);
            return;
          }
          this.error.set('Failed to load room.');
        } finally {
          this.loading.set(false);
        }
      }),
    );

    this.subs.push(
      this.signalr.roomMemberChanged$.subscribe(async (payload) => {
        if (payload.roomId !== this.roomId) return;
        if (payload.change === 'removed' || payload.change === 'role_changed') {
          try {
            const updated = await this.roomsService.get(this.roomId);
            this.room.set(updated);
          } catch {
            // room may be gone
          }
        }
      }),
      this.signalr.roomDeleted$.subscribe(async (payload) => {
        if (payload.roomId !== this.roomId) return;
        this.toast.show({ severity: 'warn', message: 'This room was deleted.' });
        await this.roomsService.refreshMine();
        await this.router.navigate(['/app/rooms']);
      }),
    );
  }

  ngOnDestroy(): void {
    this.subs.forEach((s) => s.unsubscribe());
    this.roomMessaging.unsubscribe();
    this.contextPanel.clear();
    this.unread.clearActive();
  }

  navigateToManage(): void {
    void this.router.navigate(['/app/rooms', this.roomId, 'manage']);
  }

  async leave(): Promise<void> {
    const r = this.room();
    if (!r) {
      return;
    }
    this.leaving.set(true);
    this.error.set(null);
    try {
      await this.roomsService.leave(r.id);
      await this.router.navigate(['/app/rooms']);
    } catch (err) {
      if (err instanceof HttpErrorResponse && err.error?.code === 'owner_cannot_leave') {
        this.error.set('Owners cannot leave — delete the room instead.');
      } else {
        this.error.set('Failed to leave room.');
      }
    } finally {
      this.leaving.set(false);
    }
  }

  async onSend(event: { body: string; replyToId?: string | null; attachmentIds?: string[] }): Promise<void> {
    if (this.sending()) {
      return;
    }
    this.sending.set(true);
    this.error.set(null);
    try {
      await this.roomMessaging.send(this.roomId, event.body, event.replyToId, event.attachmentIds);
      this.roomMessaging.clearReplyTo();
    } catch {
      this.error.set('Failed to send message.');
    } finally {
      this.sending.set(false);
    }
  }

  async onEditMessage(event: { id: string; body: string }): Promise<void> {
    try {
      await this.roomMessaging.edit(event.id, event.body);
    } catch {
      this.error.set('Failed to edit message.');
    }
  }

  async onDeleteMessage(id: string): Promise<void> {
    try {
      await this.roomMessaging.deleteMessage(id);
    } catch {
      this.error.set('Failed to delete message.');
    }
  }

  onReplyTo(msg: MessageResponse): void {
    this.roomMessaging.setReplyTo(msg);
  }

  onCancelReply(): void {
    this.roomMessaging.clearReplyTo();
  }

  async onLoadOlder(): Promise<void> {
    await this.roomMessaging.loadOlder();
  }

  get isOwner(): boolean {
    return this.room()?.currentUserRole === 'owner';
  }

  get isAdminOrOwner(): boolean {
    const role = this.room()?.currentUserRole;
    return role === 'owner' || role === 'admin';
  }

  get currentUserId(): string {
    return this.auth.currentUser()?.id ?? '';
  }

}
