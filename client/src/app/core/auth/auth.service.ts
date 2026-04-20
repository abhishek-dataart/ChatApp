import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  ChangePasswordRequest,
  LoginRequest,
  MeResponse,
  RegisterRequest,
} from './auth.models';

interface ApiMe {
  id: string;
  email: string;
  username: string;
  displayName: string;
  avatarUrl: string | null;
  soundOnMessage: boolean;
  currentSessionId: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBase}/auth`;

  private readonly _currentUser = signal<MeResponse | null>(null);
  readonly currentUser = this._currentUser.asReadonly();
  readonly isAuthenticated = computed(() => this._currentUser() !== null);

  async bootstrap(): Promise<void> {
    try {
      const me = await firstValueFrom(this.http.get<ApiMe>(`${this.base}/me`));
      this._currentUser.set(me);
    } catch (err) {
      if (err instanceof HttpErrorResponse && err.status === 401) {
        this._currentUser.set(null);
        return;
      }
      this._currentUser.set(null);
    }
  }

  async login(body: LoginRequest): Promise<void> {
    const me = await firstValueFrom(this.http.post<ApiMe>(`${this.base}/login`, body));
    this._currentUser.set(me);
  }

  async register(body: RegisterRequest): Promise<void> {
    const me = await firstValueFrom(this.http.post<ApiMe>(`${this.base}/register`, body));
    this._currentUser.set(me);
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.http.post(`${this.base}/logout`, {}));
    } finally {
      this._currentUser.set(null);
    }
  }

  async changePassword(body: ChangePasswordRequest): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/change-password`, body));
  }

  patchLocal(partial: Partial<MeResponse>): void {
    const current = this._currentUser();
    if (current) {
      this._currentUser.set({ ...current, ...partial });
    }
  }

  clearLocalSession(): void {
    this._currentUser.set(null);
  }
}
