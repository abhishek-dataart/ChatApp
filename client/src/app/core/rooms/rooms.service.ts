import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CatalogEntry, CreateRoomRequest, MyRoomEntry, RoomDetailResponse, UpdateRoomRequest } from './rooms.models';

@Injectable({ providedIn: 'root' })
export class RoomsService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBase}/rooms`;

  private readonly _catalog = signal<CatalogEntry[] | null>(null);
  private readonly _mine = signal<MyRoomEntry[] | null>(null);

  readonly catalog = this._catalog.asReadonly();
  readonly mine = this._mine.asReadonly();

  async refreshCatalog(q?: string): Promise<void> {
    const params: Record<string, string> = {};
    if (q) {
      params['q'] = q;
    }
    const data = await firstValueFrom(this.http.get<CatalogEntry[]>(this.base, { params }));
    this._catalog.set(data);
  }

  async refreshMine(): Promise<void> {
    const data = await firstValueFrom(this.http.get<MyRoomEntry[]>(`${this.base}/mine`));
    this._mine.set(data);
  }

  async create(input: CreateRoomRequest): Promise<RoomDetailResponse> {
    const result = await firstValueFrom(
      this.http.post<RoomDetailResponse>(this.base, input),
    );
    await Promise.all([this.refreshCatalog(), this.refreshMine()]);
    return result;
  }

  async get(id: string): Promise<RoomDetailResponse> {
    return firstValueFrom(this.http.get<RoomDetailResponse>(`${this.base}/${id}`));
  }

  async join(id: string): Promise<RoomDetailResponse> {
    const result = await firstValueFrom(
      this.http.post<RoomDetailResponse>(`${this.base}/${id}/join`, {}),
    );
    await Promise.all([this.refreshCatalog(), this.refreshMine()]);
    return result;
  }

  async leave(id: string): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/${id}/leave`, {}));
    await Promise.all([this.refreshCatalog(), this.refreshMine()]);
  }

  async delete(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`${this.base}/${id}`));
    await Promise.all([this.refreshCatalog(), this.refreshMine()]);
  }

  async update(id: string, patch: UpdateRoomRequest): Promise<RoomDetailResponse> {
    return firstValueFrom(this.http.patch<RoomDetailResponse>(`${this.base}/${id}`, patch));
  }

  async updateCapacity(id: string, capacity: number): Promise<RoomDetailResponse> {
    const result = await firstValueFrom(
      this.http.patch<RoomDetailResponse>(`${this.base}/${id}/capacity`, { capacity }),
    );
    return result;
  }

  async uploadLogo(id: string, file: File): Promise<RoomDetailResponse> {
    const form = new FormData();
    form.append('file', file);
    const result = await firstValueFrom(
      this.http.post<RoomDetailResponse>(`${this.base}/${id}/logo`, form),
    );
    await Promise.all([this.refreshCatalog(), this.refreshMine()]);
    const newLogoUrl = result.logoUrl ? `${result.logoUrl}?t=${Date.now()}` : null;
    this._mine.update(list => list?.map(r => r.id === id ? { ...r, logoUrl: newLogoUrl } : r) ?? null);
    this._catalog.update(list => list?.map(r => r.id === id ? { ...r, logoUrl: newLogoUrl } : r) ?? null);
    return { ...result, logoUrl: newLogoUrl };
  }

  async deleteLogo(id: string): Promise<RoomDetailResponse> {
    const result = await firstValueFrom(
      this.http.delete<RoomDetailResponse>(`${this.base}/${id}/logo`),
    );
    await Promise.all([this.refreshCatalog(), this.refreshMine()]);
    return result;
  }
}
