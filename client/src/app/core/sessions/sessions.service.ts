import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from '../auth/auth.service';

export interface SessionView {
  id: string;
  userAgent: string;
  ip: string;
  createdAt: string;
  lastSeenAt: string;
  isCurrent: boolean;
}

@Injectable({ providedIn: 'root' })
export class SessionsService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly base = `${environment.apiBase}/sessions`;

  async list(): Promise<SessionView[]> {
    return firstValueFrom(this.http.get<SessionView[]>(this.base));
  }

  async revoke(id: string): Promise<void> {
    const currentUser = this.auth.currentUser();
    await firstValueFrom(this.http.delete(`${this.base}/${id}`));
    if (currentUser?.currentSessionId === id) {
      this.auth.clearLocalSession();
      await this.router.navigate(['/login']);
    }
  }

  async revokeOthers(): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/revoke-others`, {}));
  }
}
