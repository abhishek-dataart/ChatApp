import {
  ChangeDetectionStrategy,
  Component,
  HostBinding,
  input,
} from '@angular/core';

@Component({
  selector: 'ui-skeleton',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ui-skeleton.component.html',
  styleUrl: './ui-skeleton.component.scss',
})
export class UiSkeletonComponent {
  readonly width = input<string>('100%');
  readonly height = input<string>('16px');
  readonly variant = input<'line' | 'circle'>('line');

  @HostBinding('style.width')
  get hostWidth(): string {
    return this.variant() === 'circle' ? this.width() : this.width();
  }

  @HostBinding('style.height')
  get hostHeight(): string {
    return this.variant() === 'circle' ? this.width() : this.height();
  }

  @HostBinding('class')
  get hostClass(): string {
    return `variant-${this.variant()}`;
  }
}
