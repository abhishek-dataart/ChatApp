import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Output,
  inject,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { RoomsService } from '../../../core/rooms/rooms.service';
import { RoomVisibility } from '../../../core/rooms/rooms.models';

@Component({
  selector: 'app-create-room-dialog',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './create-room-dialog.component.html',
  styleUrl: './create-room-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CreateRoomDialogComponent {
  private readonly roomsService = inject(RoomsService);
  private readonly fb = inject(FormBuilder);

  @Output() closed = new EventEmitter<void>();
  @Output() roomCreated = new EventEmitter<string>();

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(40)]],
    description: ['', [Validators.required, Validators.maxLength(200)]],
    visibility: ['public' as RoomVisibility],
    capacity: [null as number | null, [Validators.min(2), Validators.max(1000)]],
  });

  readonly submitting = signal(false);
  readonly fieldErrors = signal<Record<string, string>>({});
  readonly generalError = signal<string | null>(null);
  readonly logoFile = signal<File | null>(null);
  readonly logoPreview = signal<string | null>(null);
  readonly logoWarning = signal<string | null>(null);

  onLogoChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    if (!file) {
      this.clearLogo();
      return;
    }
    if (file.size > 1_048_576) {
      this.logoWarning.set('Logo must be 1 MB or smaller.');
      input.value = '';
      return;
    }
    this.logoWarning.set(null);
    this.logoFile.set(file);
    const reader = new FileReader();
    reader.onload = () => this.logoPreview.set(reader.result as string);
    reader.readAsDataURL(file);
  }

  clearLogo(): void {
    this.logoFile.set(null);
    this.logoPreview.set(null);
    this.logoWarning.set(null);
  }

  async submit(): Promise<void> {
    if (this.form.invalid) return;
    this.submitting.set(true);
    this.fieldErrors.set({});
    this.generalError.set(null);

    const { name, description, visibility, capacity } = this.form.getRawValue();
    try {
      const result = await this.roomsService.create({
        name,
        description,
        visibility: visibility as RoomVisibility,
        capacity: capacity ?? undefined,
      });
      const file = this.logoFile();
      if (file) {
        try {
          await this.roomsService.uploadLogo(result.id, file);
        } catch {
          this.logoWarning.set('Room created, but logo upload failed. Retry from settings.');
        }
      }
      this.roomCreated.emit(result.id);
    } catch (err) {
      this.mapError(err);
    } finally {
      this.submitting.set(false);
    }
  }

  private mapError(err: unknown): void {
    if (err instanceof HttpErrorResponse) {
      switch (err.error?.code) {
        case 'room_name_taken':
          this.fieldErrors.set({ name: 'A room with this name already exists.' });
          return;
        case 'invalid_room_name':
          this.fieldErrors.set({ name: 'Invalid room name. Must be 3-40 chars, alphanumeric start/end.' });
          return;
        case 'invalid_description':
          this.fieldErrors.set({ description: 'Description is required and must be 200 chars or fewer.' });
          return;
        case 'invalid_capacity':
          this.fieldErrors.set({ capacity: 'Capacity must be between 2 and 1,000.' });
          return;
      }
    }
    this.generalError.set('Failed to create room. Please try again.');
  }
}
