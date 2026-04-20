import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuditResponse, RoomBanEntry, RoomBansResponse } from './moderation.models';

@Injectable({ providedIn: 'root' })
export class ModerationService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBase}/rooms`;

  readonly bans = signal<RoomBanEntry[] | null>(null);

  async listBans(roomId: string): Promise<RoomBanEntry[]> {
    const res = await firstValueFrom(
      this.http.get<RoomBansResponse>(`${this.base}/${roomId}/bans`),
    );
    this.bans.set(res.bans);
    return res.bans;
  }

  async ban(roomId: string, userId: string): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/${roomId}/bans`, { userId }));
  }

  async unban(roomId: string, userId: string): Promise<void> {
    await firstValueFrom(this.http.delete(`${this.base}/${roomId}/bans/${userId}`));
  }

  async changeRole(roomId: string, userId: string, role: string): Promise<void> {
    await firstValueFrom(
      this.http.patch(`${this.base}/${roomId}/members/${userId}/role`, { role }),
    );
  }

  async kick(roomId: string, userId: string): Promise<void> {
    await firstValueFrom(this.http.delete(`${this.base}/${roomId}/members/${userId}`));
  }

  async listAudit(roomId: string, before?: string, limit = 50): Promise<AuditResponse> {
    const params: Record<string, string> = { limit: limit.toString() };
    if (before) params['before'] = before;
    return firstValueFrom(
      this.http.get<AuditResponse>(`${this.base}/${roomId}/audit`, { params }),
    );
  }
}
