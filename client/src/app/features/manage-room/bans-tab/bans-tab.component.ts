import { ChangeDetectionStrategy, Component, Input, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ModerationService } from '../../../core/rooms/moderation.service';
import { RoomBanEntry } from '../../../core/rooms/moderation.models';

@Component({
  selector: 'app-bans-tab',
  standalone: true,
  imports: [DatePipe],
  templateUrl: './bans-tab.component.html',
  styleUrl: './bans-tab.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BansTabComponent implements OnInit {
  @Input({ required: true }) roomId!: string;

  private readonly moderation = inject(ModerationService);

  readonly bans = signal<RoomBanEntry[] | null>(null);
  readonly loading = signal(true);
  readonly acting = signal<string | null>(null);
  readonly error = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    try {
      await this.moderation.listBans(this.roomId);
      this.bans.set(this.moderation.bans());
    } catch {
      this.error.set('Failed to load bans.');
    } finally {
      this.loading.set(false);
    }
  }

  async unban(ban: RoomBanEntry): Promise<void> {
    this.acting.set(ban.banId);
    this.error.set(null);
    try {
      await this.moderation.unban(this.roomId, ban.user.id);
      this.bans.update((list) => list?.filter((b) => b.banId !== ban.banId) ?? []);
    } catch {
      this.error.set('Failed to unban user.');
    } finally {
      this.acting.set(null);
    }
  }
}
