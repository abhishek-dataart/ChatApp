import { Injectable, signal, computed, effect } from '@angular/core';

export type ThemePreference = 'system' | 'light' | 'dark';
export type ResolvedTheme = 'light' | 'dark';

const STORAGE_KEY = 'chatapp.theme';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly preference = signal<ThemePreference>(this.#readPref());
  readonly theme = computed<ResolvedTheme>(() => this.#resolve(this.preference()));

  readonly #mq = window.matchMedia('(prefers-color-scheme: dark)');

  constructor() {
    this.#mq.addEventListener('change', () => {
      if (this.preference() === 'system') {
        this.#apply(this.#resolve('system'));
      }
    });

    effect(() => {
      this.#apply(this.theme());
    });
  }

  setPreference(pref: ThemePreference): void {
    try {
      localStorage.setItem(STORAGE_KEY, pref);
    } catch {
      // ignore storage errors
    }
    this.preference.set(pref);
  }

  toggle(): void {
    this.setPreference(this.theme() === 'dark' ? 'light' : 'dark');
  }

  #readPref(): ThemePreference {
    try {
      const stored = localStorage.getItem(STORAGE_KEY);
      if (stored === 'light' || stored === 'dark' || stored === 'system') return stored;
    } catch {
      // ignore
    }
    return 'system';
  }

  #resolve(pref: ThemePreference): ResolvedTheme {
    if (pref === 'dark') return 'dark';
    if (pref === 'light') return 'light';
    return this.#mq.matches ? 'dark' : 'light';
  }

  #apply(theme: ResolvedTheme): void {
    document.documentElement.setAttribute('data-theme', theme);
  }
}
