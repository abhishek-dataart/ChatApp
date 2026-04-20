import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { UiAvatarComponent } from '../avatar/ui-avatar.component';

export interface RoomLogoInput {
  name: string;
  logoUrl?: string | null;
}

@Component({
  selector: 'app-room-logo',
  standalone: true,
  imports: [UiAvatarComponent],
  template: `<ui-avatar [name]="room().name" [src]="room().logoUrl ?? undefined" [size]="size()" />`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RoomLogoComponent {
  readonly room = input.required<RoomLogoInput>();
  readonly size = input<'sm' | 'md' | 'lg'>('md');
}
