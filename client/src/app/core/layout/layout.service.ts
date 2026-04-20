import { Injectable, effect, inject, signal } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs/operators';

const SIDEBAR_STORAGE_KEY = 'chatapp:sidebar:collapsed';

@Injectable({ providedIn: 'root' })
export class LayoutService {
  private readonly router = inject(Router);

  readonly sidebarCollapsed = signal<boolean>(
    localStorage.getItem(SIDEBAR_STORAGE_KEY) === 'true',
  );
  readonly sidebarOpenMobile = signal(false);
  readonly contextPanelVisible = signal(true);

  constructor() {
    effect(() => {
      localStorage.setItem(SIDEBAR_STORAGE_KEY, String(this.sidebarCollapsed()));
    });

    this.router.events
      .pipe(filter((e) => e instanceof NavigationEnd))
      .subscribe(() => this.sidebarOpenMobile.set(false));
  }

  toggleSidebar(): void {
    if (window.innerWidth <= 960) {
      this.sidebarOpenMobile.update((v) => !v);
    } else {
      this.sidebarCollapsed.update((v) => !v);
    }
  }

  toggleContextPanel(): void {
    this.contextPanelVisible.update((v) => !v);
  }

  closeMobileDrawer(): void {
    this.sidebarOpenMobile.set(false);
  }
}
