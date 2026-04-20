import {
  ChangeDetectionStrategy,
  Component,
  input,
} from '@angular/core';

@Component({
  selector: 'ui-empty-state',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ui-empty-state.component.html',
  styleUrl: './ui-empty-state.component.scss',
})
export class UiEmptyStateComponent {
  readonly icon = input<string | undefined>(undefined);
  readonly heading = input.required<string>();
  readonly subtext = input<string | undefined>(undefined);
}
