import {
  ChangeDetectionStrategy,
  Component,
  ContentChildren,
  QueryList,
  AfterContentInit,
  signal,
} from '@angular/core';
import { UiTabComponent } from './ui-tab.component';

@Component({
  selector: 'ui-tabs',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ui-tabs.component.html',
  styleUrl: './ui-tabs.component.scss',
})
export class UiTabsComponent implements AfterContentInit {
  @ContentChildren(UiTabComponent) tabList!: QueryList<UiTabComponent>;

  readonly activeTabId = signal<string>('');

  readonly tabs = signal<{ tabId: string; label: string }[]>([]);

  ngAfterContentInit(): void {
    const initial = this.tabList.map((t) => ({ tabId: t.tabId(), label: t.label() }));
    this.tabs.set(initial);
    if (initial.length > 0 && !this.activeTabId()) {
      this.activeTabId.set(initial[0].tabId);
    }

    this.tabList.changes.subscribe((list: QueryList<UiTabComponent>) => {
      this.tabs.set(list.map((t) => ({ tabId: t.tabId(), label: t.label() })));
    });
  }

  select(tabId: string): void {
    this.activeTabId.set(tabId);
  }

  isActive(tabId: string): boolean {
    return this.activeTabId() === tabId;
  }
}
