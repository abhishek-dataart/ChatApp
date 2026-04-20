import {
  ChangeDetectionStrategy,
  Component,
  OnDestroy,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Subscription } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { ContextPanelService } from '../../core/context/context-panel.service';
import { DmService } from '../../core/messaging/dm.service';
import { MessageResponse } from '../../core/messaging/messaging.models';
import { UnreadService } from '../../core/messaging/unread.service';
import { BansService } from '../../core/social/bans.service';
import { BanStatusResponse } from '../../core/social/bans.models';
import { FriendshipsService } from '../../core/social/friendships.service';
import { MessageListComponent } from '../../shared/messaging/message-list.component';
import { MessageComposerComponent } from '../../shared/messaging/message-composer.component';
import { UiButtonComponent } from '../../shared/ui/button/ui-button.component';
import { UiSkeletonComponent } from '../../shared/ui/skeleton/ui-skeleton.component';

@Component({
  selector: 'app-dms',
  standalone: true,
  imports: [MessageListComponent, MessageComposerComponent, UiButtonComponent, UiSkeletonComponent],
  templateUrl: './dms.component.html',
  styleUrl: './dms.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DmsComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  readonly dmService = inject(DmService);
  private readonly auth = inject(AuthService);
  private readonly unread = inject(UnreadService);
  private readonly bansService = inject(BansService);
  private readonly friendshipsService = inject(FriendshipsService);
  private readonly contextPanel = inject(ContextPanelService);

  readonly messages = computed(() => this.dmService.messages());
  readonly replyingTo = computed(() => this.dmService.replyingTo());
  readonly currentUser = this.auth.currentUser;
  readonly loading = signal(true);
  readonly sending = signal(false);
  readonly sendError = signal<string | null>(null);
  readonly banStatus = signal<BanStatusResponse | null>(null);
  chatId = '';
  partnerId = '';
  private paramSub: Subscription | null = null;

  async ngOnInit(): Promise<void> {
    this.paramSub = this.route.paramMap.subscribe(async (params) => {
      const newId = params.get('chatId') ?? '';
      if (!newId || newId === this.chatId) return;
      this.dmService.unsubscribe();
      this.chatId = newId;
      this.loading.set(true);
      this.banStatus.set(null);
      this.dmService.subscribe(this.chatId);
      try {
        await this.dmService.loadHistory(this.chatId);
        this.unread.setActive('personal', this.chatId);

        this.partnerId = this.resolvePartnerId();
        if (this.partnerId) {
          void this.bansService.getBanStatus(this.partnerId).then((s) => this.banStatus.set(s));
        }
        this.contextPanel.set({ type: 'dm', partnerId: this.partnerId, chatId: this.chatId });
      } finally {
        this.loading.set(false);
      }
    });
  }

  private resolvePartnerId(): string {
    const me = this.currentUser()?.id ?? '';
    const friend = this.friendshipsService.list()?.friends.find(
      (f) => f.personalChatId === this.chatId,
    );
    if (friend) return friend.user.id;
    const msg = this.dmService.messages().find((m) => m.authorId !== me);
    return msg?.authorId ?? '';
  }

  ngOnDestroy(): void {
    this.paramSub?.unsubscribe();
    this.dmService.unsubscribe();
    this.unread.clearActive();
    this.contextPanel.clear();
  }

  async onSend(event: { body: string; replyToId?: string | null; attachmentIds?: string[] }): Promise<void> {
    if (this.sending()) return;
    this.sending.set(true);
    this.sendError.set(null);
    try {
      await this.dmService.send(this.chatId, event.body, event.replyToId, event.attachmentIds);
      this.dmService.clearReplyTo();
    } catch {
      this.sendError.set('Failed to send message.');
    } finally {
      this.sending.set(false);
    }
  }

  async onEditMessage(event: { id: string; body: string }): Promise<void> {
    try {
      await this.dmService.edit(event.id, event.body);
    } catch {
      this.sendError.set('Failed to edit message.');
    }
  }

  async onDeleteMessage(id: string): Promise<void> {
    try {
      await this.dmService.deleteMessage(id);
    } catch {
      this.sendError.set('Failed to delete message.');
    }
  }

  onReplyTo(msg: MessageResponse): void {
    this.dmService.setReplyTo(msg);
  }

  onCancelReply(): void {
    this.dmService.clearReplyTo();
  }

  async onLoadOlder(): Promise<void> {
    await this.dmService.loadOlder();
  }

  async unblock(): Promise<void> {
    if (!this.partnerId) return;
    try {
      await this.bansService.unblock(this.partnerId);
      const status = await this.bansService.getBanStatus(this.partnerId);
      this.banStatus.set(status);
    } catch {
      this.sendError.set('Failed to unblock user.');
    }
  }
}
