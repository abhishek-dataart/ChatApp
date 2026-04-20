import {
  ChangeDetectionStrategy,
  Component,
  HostBinding,
  input,
} from '@angular/core';

@Component({
  selector: 'ui-presence-dot',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ui-presence-dot.component.html',
  styleUrl: './ui-presence-dot.component.scss',
})
export class UiPresenceDotComponent {
  readonly status = input.required<'online' | 'afk' | 'offline'>();

  @HostBinding('class')
  get hostClass(): string {
    return `status-${this.status()}`;
  }
}
