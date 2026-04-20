import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from '../auth/auth.service';

export interface ProfileResponse {
  id: string;
  email: string;
  username: string;
  displayName: string;
  avatarUrl: string | null;
  soundOnMessage: boolean;
}

export interface UpdateProfileRequest {
  displayName?: string;
  soundOnMessage?: boolean;
}

@Injectable({ providedIn: 'root' })
export class ProfileService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly base = `${environment.apiBase}/profile`;

  async updateProfile(body: UpdateProfileRequest): Promise<ProfileResponse> {
    const result = await firstValueFrom(this.http.patch<ProfileResponse>(this.base, body));
    this.auth.patchLocal({
      displayName: result.displayName,
      soundOnMessage: result.soundOnMessage,
      avatarUrl: result.avatarUrl,
    });
    return result;
  }

  async uploadAvatar(file: File): Promise<ProfileResponse> {
    const form = new FormData();
    form.append('file', file);
    const result = await firstValueFrom(this.http.post<ProfileResponse>(`${this.base}/avatar`, form));
    this.auth.patchLocal({ avatarUrl: result.avatarUrl ? `${result.avatarUrl}?t=${Date.now()}` : null });
    return result;
  }

  async deleteAvatar(): Promise<void> {
    await firstValueFrom(this.http.delete(`${this.base}/avatar`));
    this.auth.patchLocal({ avatarUrl: null });
  }

  async deleteAccount(password: string): Promise<void> {
    await firstValueFrom(this.http.delete(`${this.base}`, { body: { password } }));
    this.auth.clearLocalSession();
  }

  getAvatarUrl(userId: string): string {
    return `${environment.apiBase}/profile/avatar/${userId}`;
  }
}
