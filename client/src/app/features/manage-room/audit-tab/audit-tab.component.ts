import { ChangeDetectionStrategy, Component, Input, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ModerationService } from '../../../core/rooms/moderation.service';
import { AuditEntry } from '../../../core/rooms/moderation.models';

@Component({
  selector: 'app-audit-tab',
  standalone: true,
  imports: [DatePipe],
  templateUrl: './audit-tab.component.html',
  styleUrl: './audit-tab.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuditTabComponent implements OnInit {
  @Input({ required: true }) roomId!: string;

  private readonly moderation = inject(ModerationService);

  readonly items = signal<AuditEntry[]>([]);
  readonly loading = signal(true);
  readonly loadingMore = signal(false);
  readonly nextBefore = signal<string | null>(null);
  readonly error = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    try {
      const res = await this.moderation.listAudit(this.roomId);
      this.items.set(res.items);
      this.nextBefore.set(res.nextBefore);
    } catch {
      this.error.set('Failed to load audit log.');
    } finally {
      this.loading.set(false);
    }
  }

  async loadMore(): Promise<void> {
    const cursor = this.nextBefore();
    if (!cursor) return;
    this.loadingMore.set(true);
    try {
      const res = await this.moderation.listAudit(this.roomId, cursor);
      this.items.update((list) => [...list, ...res.items]);
      this.nextBefore.set(res.nextBefore);
    } catch {
      this.error.set('Failed to load more entries.');
    } finally {
      this.loadingMore.set(false);
    }
  }

  renderDetail(detail: string | null): string {
    if (!detail) return '';
    try {
      const d = JSON.parse(detail);
      if (d.from !== undefined && d.to !== undefined) {
        return `${d.from} → ${d.to}`;
      }
    } catch {
      // ignore
    }
    return detail;
  }
}
