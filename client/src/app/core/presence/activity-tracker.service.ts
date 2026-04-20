import { DestroyRef, Injectable, Signal, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { fromEvent, merge } from 'rxjs';

const AFK_THRESHOLD_MS = 60_000;
const POLL_INTERVAL_MS = 5_000;

/**
 * Tracks per-tab user activity and exposes an `isActive` signal that flips
 * within seconds of the AFK threshold being crossed in either direction.
 *
 * "Active" means the user has touched this tab (mouse / key / scroll / touch /
 * focus) within {@link AFK_THRESHOLD_MS}, AND the tab is currently visible.
 * A hidden/backgrounded tab is treated as inactive immediately, so a user with
 * only minimised tabs no longer reports online from this tab.
 */
@Injectable({ providedIn: 'root' })
export class ActivityTrackerService {
  private readonly destroyRef = inject(DestroyRef);
  private readonly _lastActivityAt = signal<number>(performance.now());
  private readonly _isVisible = signal<boolean>(this.computeVisible());
  private readonly _now = signal<number>(performance.now());

  /** True when the user has interacted with this tab within the AFK window AND the tab is visible. */
  readonly isActive: Signal<boolean> = computed(() => {
    if (!this._isVisible()) return false;
    return this._now() - this._lastActivityAt() < AFK_THRESHOLD_MS;
  });

  constructor() {
    merge(
      fromEvent(document, 'mousemove', { passive: true }),
      fromEvent(document, 'mousedown', { passive: true }),
      fromEvent(document, 'click', { passive: true }),
      fromEvent(document, 'keydown', { passive: true }),
      fromEvent(document, 'scroll', { passive: true, capture: true }),
      fromEvent(document, 'touchstart', { passive: true }),
      fromEvent(window, 'focus', { passive: true }),
    )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this._lastActivityAt.set(performance.now());
      });

    fromEvent(document, 'visibilitychange', { passive: true })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        const visible = this.computeVisible();
        this._isVisible.set(visible);
        if (visible) {
          this._lastActivityAt.set(performance.now());
        }
      });

    // Poll a coarse "now" signal so the computed isActive flips at the AFK
    // boundary even if the user generates no further events.
    const tick = setInterval(() => this._now.set(performance.now()), POLL_INTERVAL_MS);
    this.destroyRef.onDestroy(() => clearInterval(tick));
  }

  isActiveNow(): boolean {
    return this.isActive();
  }

  private computeVisible(): boolean {
    return typeof document === 'undefined' ? true : !document.hidden;
  }
}
