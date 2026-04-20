import {
  ChangeDetectionStrategy,
  Component,
  HostBinding,
  input,
} from '@angular/core';

@Component({
  selector: 'ui-badge',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ui-badge.component.html',
  styleUrl: './ui-badge.component.scss',
})
export class UiBadgeComponent {
  readonly variant = input<'default' | 'accent' | 'success' | 'danger' | 'warning'>('default');
  readonly count = input<number | undefined>(undefined);

  @HostBinding('class')
  get hostClass(): string {
    return `variant-${this.variant()}`;
  }
}
