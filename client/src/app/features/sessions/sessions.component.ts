import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { SessionView, SessionsService } from '../../core/sessions/sessions.service';
import { UiBadgeComponent } from '../../shared/ui/badge/ui-badge.component';
import { UiButtonComponent } from '../../shared/ui/button/ui-button.component';
import { UiCardComponent } from '../../shared/ui/card/ui-card.component';
import { UiSkeletonComponent } from '../../shared/ui/skeleton/ui-skeleton.component';

@Component({
  selector: 'app-sessions',
  standalone: true,
  imports: [DatePipe, UiCardComponent, UiBadgeComponent, UiButtonComponent, UiSkeletonComponent],
  templateUrl: './sessions.component.html',
  styleUrl: './sessions.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SessionsComponent implements OnInit {
  private readonly sessionsService = inject(SessionsService);

  readonly sessions = signal<SessionView[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly revoking = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const result = await this.sessionsService.list();
      this.sessions.set(result);
    } catch {
      this.error.set('Failed to load sessions.');
    } finally {
      this.loading.set(false);
    }
  }

  async revoke(id: string): Promise<void> {
    this.revoking.set(id);
    try {
      await this.sessionsService.revoke(id);
      // If we're still here the revoked session wasn't ours; refresh the list
      this.sessions.update(list => list.filter(s => s.id !== id));
    } catch {
      this.error.set('Failed to revoke session.');
    } finally {
      this.revoking.set(null);
    }
  }

  async revokeOthers(): Promise<void> {
    this.revoking.set('others');
    try {
      await this.sessionsService.revokeOthers();
      this.sessions.update(list => list.filter(s => s.isCurrent));
    } catch {
      this.error.set('Failed to revoke sessions.');
    } finally {
      this.revoking.set(null);
    }
  }
}
