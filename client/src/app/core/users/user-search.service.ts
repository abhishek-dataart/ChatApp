import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface UserSearchResult {
  id: string;
  username: string;
  displayName: string;
  avatarUrl: string | null;
  isFriend: boolean;
  personalChatId: string | null;
}

@Injectable({ providedIn: 'root' })
export class UserSearchService {
  private readonly http = inject(HttpClient);

  async search(q: string): Promise<UserSearchResult[]> {
    const trimmed = q.trim();
    if (trimmed.length < 2) return [];
    const params = new HttpParams().set('q', trimmed);
    return firstValueFrom(
      this.http.get<UserSearchResult[]>(`${environment.apiBase}/users/search`, { params }),
    );
  }
}
