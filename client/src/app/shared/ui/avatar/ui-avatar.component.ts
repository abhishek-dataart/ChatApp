import {
  ChangeDetectionStrategy,
  Component,
  HostBinding,
  computed,
  input,
  signal,
} from '@angular/core';

@Component({
  selector: 'ui-avatar',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ui-avatar.component.html',
  styleUrl: './ui-avatar.component.scss',
})
export class UiAvatarComponent {
  readonly src = input<string | undefined>(undefined);
  readonly name = input.required<string>();
  readonly size = input<'sm' | 'md' | 'lg'>('md');
  readonly presence = input<'online' | 'afk' | 'offline' | null>(null);

  readonly imgError = signal(false);

  readonly initials = computed(() => {
    const parts = this.name().trim().split(/\s+/);
    if (parts.length >= 2) {
      return (parts[0][0] + parts[1][0]).toUpperCase();
    }
    return parts[0].slice(0, 2).toUpperCase();
  });

  readonly showImage = computed(() => !!this.src() && !this.imgError());

  readonly presenceDotSize = computed(() => {
    const map: Record<string, string> = { sm: '8px', md: '10px', lg: '12px' };
    return map[this.size()];
  });

  onImgError(): void {
    this.imgError.set(true);
  }

  @HostBinding('class')
  get hostClass(): string {
    return `size-${this.size()}`;
  }
}
