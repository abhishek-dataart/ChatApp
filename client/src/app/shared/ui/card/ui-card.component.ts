import {
  ChangeDetectionStrategy,
  Component,
  HostBinding,
  input,
} from '@angular/core';

@Component({
  selector: 'ui-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ui-card.component.html',
  styleUrl: './ui-card.component.scss',
})
export class UiCardComponent {
  readonly padding = input<'sm' | 'md' | 'lg'>('md');

  @HostBinding('class')
  get hostClass(): string {
    return `padding-${this.padding()}`;
  }
}
