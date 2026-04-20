import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { LucideAngularModule, Hash } from 'lucide-angular';
import { RoomsService } from '../../../core/rooms/rooms.service';
import { CreateRoomDialogComponent } from '../create-room-dialog/create-room-dialog.component';
import { UiButtonComponent } from '../../../shared/ui/button/ui-button.component';
import { UiCardComponent } from '../../../shared/ui/card/ui-card.component';
import { UiEmptyStateComponent } from '../../../shared/ui/empty-state/ui-empty-state.component';
import { UiSkeletonComponent } from '../../../shared/ui/skeleton/ui-skeleton.component';
import { RoomLogoComponent } from '../../../shared/ui/room-logo/room-logo.component';

@Component({
  selector: 'app-public-rooms',
  standalone: true,
  imports: [
    RouterLink,
    LucideAngularModule,
    CreateRoomDialogComponent,
    UiButtonComponent,
    UiCardComponent,
    UiEmptyStateComponent,
    UiSkeletonComponent,
    RoomLogoComponent,
  ],
  templateUrl: './public-rooms.component.html',
  styleUrl: './public-rooms.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PublicRoomsComponent implements OnInit {
  private readonly roomsService = inject(RoomsService);
  private readonly router = inject(Router);

  readonly HashIcon = Hash;

  readonly catalog = this.roomsService.catalog;
  readonly showCreate = signal(false);
  readonly searchQuery = signal('');
  readonly joiningId = signal<string | null>(null);
  readonly error = signal<string | null>(null);

  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  async ngOnInit(): Promise<void> {
    await this.roomsService.refreshCatalog();
  }

  onSearchInput(event: Event): void {
    const q = (event.target as HTMLInputElement).value;
    this.searchQuery.set(q);
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => {
      this.roomsService.refreshCatalog(q || undefined);
    }, 200);
  }

  async onRoomCreated(id: string): Promise<void> {
    this.showCreate.set(false);
    await this.router.navigate(['/app/rooms', id]);
  }

  async join(roomId: string): Promise<void> {
    this.joiningId.set(roomId);
    this.error.set(null);
    try {
      await this.roomsService.join(roomId);
      await this.router.navigate(['/app/rooms', roomId]);
    } catch (err) {
      this.error.set(this.mapError(err));
    } finally {
      this.joiningId.set(null);
    }
  }

  private mapError(err: unknown): string {
    if (err instanceof HttpErrorResponse) {
      switch (err.error?.code) {
        case 'room_is_private': return 'This room is private.';
        case 'room_full': return 'This room is full.';
        case 'already_member': return 'You are already a member.';
      }
    }
    return 'Failed to join room.';
  }
}
