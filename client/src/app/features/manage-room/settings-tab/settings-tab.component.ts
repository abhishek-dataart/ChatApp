import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  OnInit,
  Output,
  SimpleChanges,
  computed,
  inject,
  signal,
} from '@angular/core';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { RoomsService } from '../../../core/rooms/rooms.service';
import { RoomDetailResponse, RoomVisibility } from '../../../core/rooms/rooms.models';
import { ToastService } from '../../../core/notifications/toast.service';
import { RoomLogoComponent } from '../../../shared/ui/room-logo/room-logo.component';

@Component({
  selector: 'app-settings-tab',
  standalone: true,
  imports: [FormsModule, RoomLogoComponent],
  templateUrl: './settings-tab.component.html',
  styleUrl: './settings-tab.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SettingsTabComponent implements OnInit, OnChanges {
  @Input({ required: true }) room!: RoomDetailResponse;
  @Input({ required: true }) roomId!: string;
  @Input({ required: true }) isOwner!: boolean;
  @Output() roomUpdated = new EventEmitter<void>();

  private readonly roomsService = inject(RoomsService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  readonly name = signal('');
  readonly description = signal('');
  readonly visibility = signal<RoomVisibility>('public');
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);
  readonly deletingRoom = signal(false);
  readonly deleteError = signal<string | null>(null);
  readonly savingLogo = signal(false);
  readonly logoError = signal<string | null>(null);

  private readonly _room = signal<RoomDetailResponse | null>(null);
  private readonly _logoUrlOverride = signal<string | null | undefined>(undefined);
  readonly displayRoom = computed<RoomDetailResponse>(() => {
    const r = this._room()!;
    const override = this._logoUrlOverride();
    return override === undefined ? r : { ...r, logoUrl: override };
  });

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['room'] && this.room) {
      this._room.set(this.room);
    }
  }

  ngOnInit(): void {
    this._room.set(this.room);
    this.name.set(this.room.name);
    this.description.set(this.room.description);
    this.visibility.set(this.room.visibility);
  }

  onNameInput(event: Event): void {
    this.name.set((event.target as HTMLInputElement).value);
  }

  onDescInput(event: Event): void {
    this.description.set((event.target as HTMLTextAreaElement).value);
  }

  onVisibilityChange(event: Event): void {
    this.visibility.set((event.target as HTMLSelectElement).value as RoomVisibility);
  }

  async saveSettings(): Promise<void> {
    this.saving.set(true);
    this.saveError.set(null);
    try {
      await this.roomsService.update(this.roomId, {
        name: this.name(),
        description: this.description(),
        visibility: this.visibility(),
      });
      this.roomUpdated.emit();
    } catch (err: any) {
      const code = err?.error?.code;
      if (code === 'invalid_room_name') {
        this.saveError.set('Invalid room name.');
      } else if (code === 'room_name_taken') {
        this.saveError.set('A room with this name already exists.');
      } else if (code === 'invalid_description') {
        this.saveError.set('Description must be 1-200 characters.');
      } else {
        this.saveError.set('Failed to save settings.');
      }
    } finally {
      this.saving.set(false);
    }
  }

  async onLogoChange(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    this.savingLogo.set(true);
    this.logoError.set(null);
    try {
      const result = await this.roomsService.uploadLogo(this.roomId, file);
      this._logoUrlOverride.set(result.logoUrl ?? null);
      this.roomUpdated.emit();
    } catch (err) {
      const msg =
        err instanceof HttpErrorResponse && err.error?.code === 'unsupported_media_type'
          ? 'Only PNG, JPEG, GIF, and WEBP images are supported.'
          : 'Failed to upload logo. Make sure the file is under 1 MB.';
      this.logoError.set(msg);
      this.toast.show({ severity: 'error', message: msg });
    } finally {
      this.savingLogo.set(false);
      input.value = '';
    }
  }

  async removeLogo(): Promise<void> {
    this.savingLogo.set(true);
    this.logoError.set(null);
    try {
      await this.roomsService.deleteLogo(this.roomId);
      this._logoUrlOverride.set(null);
      this.roomUpdated.emit();
    } catch {
      this.logoError.set('Failed to remove logo.');
    } finally {
      this.savingLogo.set(false);
    }
  }

  async deleteRoom(): Promise<void> {
    const name = prompt(`Type the room name "${this.room.name}" to confirm deletion:`);
    if (name !== this.room.name) return;
    this.deletingRoom.set(true);
    this.deleteError.set(null);
    try {
      await this.roomsService.delete(this.roomId);
      await this.router.navigate(['/app/rooms']);
    } catch {
      this.deleteError.set('Failed to delete room.');
    } finally {
      this.deletingRoom.set(false);
    }
  }
}
