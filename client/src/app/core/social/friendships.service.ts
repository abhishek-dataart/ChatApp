import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { FriendshipListResponse, FriendSummary, PendingFriendship } from './friendships.models';

@Injectable({ providedIn: 'root' })
export class FriendshipsService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBase}/friendships`;

  private readonly _list = signal<FriendshipListResponse | null>(null);
  readonly list = this._list.asReadonly();

  // Temporary: poll every 60s so the requester side sees friend-accepts without reload.
  // Remove once the server pushes a FriendshipAccepted SignalR event.
  startPolling(): void {
    setInterval(() => {
      if (document.visibilityState === 'visible') {
        this.refresh().catch(() => {});
      }
    }, 60_000);
  }

  async refresh(): Promise<void> {
    const data = await firstValueFrom(this.http.get<FriendshipListResponse>(this.base));
    this._list.set(data);
  }

  async sendRequest(username: string, note?: string): Promise<PendingFriendship> {
    const result = await firstValueFrom(
      this.http.post<PendingFriendship>(this.base, { username, note: note ?? null }),
    );
    await this.refresh();
    return result;
  }

  async accept(id: string): Promise<FriendSummary> {
    const result = await firstValueFrom(
      this.http.post<FriendSummary>(`${this.base}/${id}/accept`, {}),
    );
    await this.refresh();
    return result;
  }

  async decline(id: string): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/${id}/decline`, {}));
    await this.refresh();
  }

  async unfriend(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`${this.base}/${id}`));
    await this.refresh();
  }

  async cancelOutgoing(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`${this.base}/${id}`));
    await this.refresh();
  }
}
