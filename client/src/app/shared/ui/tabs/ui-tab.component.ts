import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
} from '@angular/core';
import { UiTabsComponent } from './ui-tabs.component';

@Component({
  selector: 'ui-tab',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ui-tab.component.html',
  styleUrl: './ui-tab.component.scss',
})
export class UiTabComponent {
  readonly label = input.required<string>();
  readonly tabId = input.required<string>();

  private readonly tabs = inject(UiTabsComponent);

  readonly isActive = computed(() => this.tabs.isActive(this.tabId()));
}
