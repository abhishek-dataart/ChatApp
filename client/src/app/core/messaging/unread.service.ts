import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SignalrService } from '../signalr/signalr.service';
import { UnreadChangedPayload, UnreadResponse } from './messaging.models';

@Injectable({ providedIn: 'root' })
export class UnreadService {
  private readonly http = inject(HttpClient);
  private readonly signalr = inject(SignalrService);

  private readonly _counts = signal<Record<string, number>>({});
  readonly counts = this._counts.asReadonly();

  private readonly _activeKey = signal<string | null>(null);
  readonly activeKey = this._activeKey.asReadonly();

  private readonly handler = (payload: UnreadChangedPayload) => {
    const key = `${payload.scope}:${payload.scopeId}`;
    if (this._activeKey() === key) {
      this._counts.update((c) => ({ ...c, [key]: 0 }));
      if (payload.unreadCount > 0) {
        void firstValueFrom(
          this.http.post(`${environment.apiBase}/chats/${payload.scope}/${payload.scopeId}/read`, null),
        ).catch(() => {});
      }
      return;
    }
    this._counts.update((c) => ({ ...c, [key]: payload.unreadCount }));
  };

  constructor() {
    this.signalr.chat.onreconnected(() => {
      void this.loadAll();
    });
  }

  async loadAll(): Promise<void> {
    const data = await firstValueFrom(
      this.http.get<UnreadResponse[]>(`${environment.apiBase}/chats/unread`),
    );
    const map: Record<string, number> = {};
    for (const item of data) {
      map[`${item.scope}:${item.scopeId}`] = item.unreadCount;
    }
    this._counts.set(map);
  }

  subscribe(): void {
    this.signalr.chat.on('UnreadChanged', this.handler);
  }

  unsubscribe(): void {
    this.signalr.chat.off('UnreadChanged', this.handler);
  }

  countFor(scope: 'personal' | 'room', scopeId: string): number {
    return this._counts()[`${scope}:${scopeId}`] ?? 0;
  }

  async markRead(scope: 'personal' | 'room', scopeId: string): Promise<void> {
    this._counts.update((c) => ({ ...c, [`${scope}:${scopeId}`]: 0 }));
    await firstValueFrom(
      this.http.post(`${environment.apiBase}/chats/${scope}/${scopeId}/read`, null),
    );
  }

  setActive(scope: 'personal' | 'room', scopeId: string): void {
    const key = `${scope}:${scopeId}`;
    this._activeKey.set(key);
    this._counts.update((c) => ({ ...c, [key]: 0 }));
    void this.markRead(scope, scopeId).catch(() => {});
  }

  clearActive(): void {
    this._activeKey.set(null);
  }
}
