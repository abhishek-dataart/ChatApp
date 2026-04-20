import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  IncomingInvitationsResponse,
  InvitationEntry,
  OutgoingInvitationEntry,
  RoomInvitationsResponse,
} from './invitations.models';
import { RoomsService } from './rooms.service';

@Injectable({ providedIn: 'root' })
export class InvitationsService {
  private readonly http = inject(HttpClient);
  private readonly roomsService = inject(RoomsService);
  private readonly base = `${environment.apiBase}`;

  private readonly _incoming = signal<InvitationEntry[] | null>(null);
  readonly incoming = this._incoming.asReadonly();

  async refreshIncoming(): Promise<void> {
    const data = await firstValueFrom(
      this.http.get<IncomingInvitationsResponse>(`${this.base}/invitations`),
    );
    this._incoming.set(data.incoming);
  }

  async listOutgoing(roomId: string): Promise<OutgoingInvitationEntry[]> {
    const data = await firstValueFrom(
      this.http.get<RoomInvitationsResponse>(`${this.base}/rooms/${roomId}/invitations`),
    );
    return data.invitations;
  }

  async send(roomId: string, username: string, note?: string): Promise<OutgoingInvitationEntry> {
    return firstValueFrom(
      this.http.post<OutgoingInvitationEntry>(`${this.base}/rooms/${roomId}/invitations`, {
        username,
        note: note || null,
      }),
    );
  }

  async accept(id: string): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/invitations/${id}/accept`, {}));
    await Promise.all([this.refreshIncoming(), this.roomsService.refreshMine()]);
  }

  async decline(id: string): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/invitations/${id}/decline`, {}));
    await this.refreshIncoming();
  }

  async revoke(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`${this.base}/invitations/${id}`));
  }
}
