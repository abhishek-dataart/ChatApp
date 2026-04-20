import {
  ChangeDetectionStrategy,
  Component,
  OnDestroy,
  OnInit,
  inject,
} from '@angular/core';
import { Router, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { UnreadService } from '../../core/messaging/unread.service';
import { PresenceService } from '../../core/presence/presence.service';
import { RoomsService } from '../../core/rooms/rooms.service';
import { FriendshipsService } from '../../core/social/friendships.service';
import { InvitationsService } from '../../core/rooms/invitations.service';
import { SignalrService } from '../../core/signalr/signalr.service';
import { ToastService } from '../../core/notifications/toast.service';
import { SoundService } from '../../core/notifications/sound.service';
import { ContextPanelService } from '../../core/context/context-panel.service';
import { LayoutService } from '../../core/layout/layout.service';
import { MessageResponse } from '../../core/messaging/messaging.models';
import { UiToastContainerComponent } from '../../shared/ui/toast/ui-toast-container.component';
import { TopBarComponent } from './top-bar.component';
import { SideNavComponent } from './side-nav.component';
import { ContextPanelComponent } from './context-panel.component';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, UiToastContainerComponent, TopBarComponent, SideNavComponent, ContextPanelComponent],
  templateUrl: './app-shell.component.html',
  styleUrl: './app-shell.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppShellComponent implements OnInit, OnDestroy {
  private readonly auth = inject(AuthService);
  readonly router = inject(Router);
  private readonly signalr = inject(SignalrService);
  private readonly presence = inject(PresenceService);
  private readonly unread = inject(UnreadService);
  private readonly toast = inject(ToastService);
  private readonly sound = inject(SoundService);
  private readonly roomsService = inject(RoomsService);
  private readonly friendshipsService = inject(FriendshipsService);
  private readonly invitationsService = inject(InvitationsService);
  readonly contextPanel = inject(ContextPanelService);
  readonly layout = inject(LayoutService);

  private readonly bannedSub = this.signalr.roomBanned$.subscribe(async (payload) => {
    this.toast.show({ severity: 'warn', message: `You were banned from #${payload.roomName}` });
    await this.roomsService.refreshMine();
    const url = this.router.url;
    if (url.startsWith(`/app/rooms/${payload.roomId}`)) {
      await this.router.navigate(['/app/rooms']);
    }
  });

  private readonly friendshipSub = this.signalr.friendshipChanged$.subscribe(async () => {
    const before = this.friendshipsService.list()?.incoming.length ?? 0;
    try {
      await this.friendshipsService.refresh();
    } catch {
      return;
    }
    const after = this.friendshipsService.list()?.incoming.length ?? 0;
    if (after > before) this.pingNotify();
  });

  private readonly invitationSub = this.signalr.invitationChanged$.subscribe(async () => {
    const before = this.invitationsService.incoming()?.length ?? 0;
    try {
      await this.invitationsService.refreshIncoming();
    } catch {
      return;
    }
    const after = this.invitationsService.incoming()?.length ?? 0;
    if (after > before) {
      this.pingNotify();
      const newest = this.invitationsService.incoming()?.at(-1);
      const msg = newest
        ? `${newest.inviter.displayName} invited you to ${newest.room.name} — open Invitations to accept`
        : 'You have a new room invitation';
      this.toast.show({ severity: 'info', message: msg });
    }
  });

  private pingNotify(): void {
    const me = this.auth.currentUser();
    if (me?.soundOnMessage) this.sound.play();
  }

  readonly user = this.auth.currentUser;

  private readonly soundHandler = (msg: MessageResponse) => {
    const me = this.auth.currentUser();
    if (!me || msg.authorId === me.id) return;
    if (!me.soundOnMessage) return;
    this.sound.play();
  };

  async ngOnInit(): Promise<void> {
    await this.signalr.start();
    this.presence.start();
    this.unread.subscribe();
    this.signalr.chat.on('MessageCreated', this.soundHandler);
    await Promise.all([
      this.unread.loadAll(),
      this.roomsService.refreshMine().catch(() => {}),
      this.friendshipsService.refresh().catch(() => {}),
      this.invitationsService.refreshIncoming().catch(() => {}),
    ]);
    this.friendshipsService.startPolling();
  }

  ngOnDestroy(): void {
    this.bannedSub.unsubscribe();
    this.friendshipSub.unsubscribe();
    this.invitationSub.unsubscribe();
    this.signalr.chat.off('MessageCreated', this.soundHandler);
    this.unread.unsubscribe();
    this.presence.stop();
    this.signalr.stop();
  }

  async signOut(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/login');
  }
}
