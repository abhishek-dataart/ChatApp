import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { BannedUserEntry, BanListResponse, BanStatusResponse } from './bans.models';

@Injectable({ providedIn: 'root' })
export class BansService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBase}/users`;

  readonly myBans = signal<BannedUserEntry[]>([]);

  async block(userId: string): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/${userId}/ban`, {}));
    await this.listMyBans();
  }

  async unblock(userId: string): Promise<void> {
    await firstValueFrom(this.http.delete(`${this.base}/${userId}/ban`));
    await this.listMyBans();
  }

  async listMyBans(): Promise<BannedUserEntry[]> {
    const data = await firstValueFrom(this.http.get<BanListResponse>(`${this.base}/bans`));
    this.myBans.set(data.bans);
    return data.bans;
  }

  async getBanStatus(userId: string): Promise<BanStatusResponse> {
    return firstValueFrom(this.http.get<BanStatusResponse>(`${this.base}/${userId}/ban-status`));
  }
}
