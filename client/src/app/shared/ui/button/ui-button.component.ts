import {
  ChangeDetectionStrategy,
  Component,
  HostBinding,
  input,
  computed,
} from '@angular/core';

@Component({
  selector: 'ui-button',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ui-button.component.html',
  styleUrl: './ui-button.component.scss',
})
export class UiButtonComponent {
  readonly variant = input<'primary' | 'secondary' | 'ghost' | 'danger'>('secondary');
  readonly size = input<'sm' | 'md'>('md');
  readonly disabled = input<boolean>(false);
  readonly loading = input<boolean>(false);
  readonly type = input<'button' | 'submit' | 'reset'>('button');

  readonly isDisabled = computed(() => this.disabled() || this.loading());

  @HostBinding('class')
  get hostClass(): string {
    return `variant-${this.variant()} size-${this.size()}`;
  }
}
