import { Injectable, signal } from '@angular/core';

export interface Toast {
  id: string;
  severity: 'info' | 'warn' | 'error';
  message: string;
}

@Injectable({ providedIn: 'root' })
export class ToastService {
  readonly toasts = signal<Toast[]>([]);

  show(opts: { severity: 'info' | 'warn' | 'error'; message: string; durationMs?: number }): void {
    const id = crypto.randomUUID();
    this.toasts.update((list) => [...list, { id, severity: opts.severity, message: opts.message }]);
    setTimeout(() => this.dismiss(id), opts.durationMs ?? 5000);
  }

  dismiss(id: string): void {
    this.toasts.update((list) => list.filter((t) => t.id !== id));
  }
}
