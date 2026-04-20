import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SignalrService } from '../signalr/signalr.service';
import { MessageDeletedPayload, MessageResponse, SendMessageRequest } from './messaging.models';

@Injectable({ providedIn: 'root' })
export class DmService {
  private readonly http = inject(HttpClient);
  private readonly signalr = inject(SignalrService);

  private readonly _messages = signal<MessageResponse[]>([]);
  readonly messages = this._messages.asReadonly();

  readonly replyingTo = signal<MessageResponse | null>(null);

  private readonly _hasMoreHistory = signal(true);
  readonly hasMoreHistory = this._hasMoreHistory.asReadonly();

  private readonly _isLoadingOlder = signal(false);
  readonly isLoadingOlder = this._isLoadingOlder.asReadonly();

  private readonly pageSize = 50;
  private currentChatId: string | null = null;

  private readonly createdHandler = (msg: MessageResponse) => {
    if (msg.scope !== 'personal' || msg.personalChatId !== this.currentChatId) return;
    const existing = this._messages();
    if (existing.some((m) => m.id === msg.id)) return;
    this._messages.set([...existing, msg]);
  };

  private readonly editedHandler = (msg: MessageResponse) => {
    if (msg.scope !== 'personal' || msg.personalChatId !== this.currentChatId) return;
    this._messages.update((msgs) => msgs.map((m) => (m.id === msg.id ? msg : m)));
  };

  private readonly deletedHandler = (payload: MessageDeletedPayload) => {
    if (payload.scope !== 'personal' || payload.personalChatId !== this.currentChatId) return;
    this._messages.update((msgs) => msgs.filter((m) => m.id !== payload.id));
  };

  async loadHistory(chatId: string): Promise<void> {
    this.currentChatId = chatId;
    this._messages.set([]);
    this._hasMoreHistory.set(true);
    const data = await firstValueFrom(
      this.http.get<MessageResponse[]>(`${environment.apiBase}/chats/personal/${chatId}/messages`, {
        params: new HttpParams().set('limit', this.pageSize),
      }),
    );
    this._messages.set(data);
    if (data.length < this.pageSize) {
      this._hasMoreHistory.set(false);
    }
  }

  async loadOlder(): Promise<void> {
    if (!this.currentChatId || this._isLoadingOlder() || !this._hasMoreHistory()) return;
    const current = this._messages();
    if (current.length === 0) return;
    const oldest = current[0];
    this._isLoadingOlder.set(true);
    try {
      const data = await firstValueFrom(
        this.http.get<MessageResponse[]>(
          `${environment.apiBase}/chats/personal/${this.currentChatId}/messages`,
          {
            params: new HttpParams()
              .set('beforeCreatedAt', oldest.createdAt)
              .set('beforeId', oldest.id)
              .set('limit', this.pageSize),
          },
        ),
      );
      if (data.length < this.pageSize) {
        this._hasMoreHistory.set(false);
      }
      if (data.length > 0) {
        this._messages.set([...data, ...current]);
      }
    } finally {
      this._isLoadingOlder.set(false);
    }
  }

  async send(chatId: string, body: string, replyToId?: string | null, attachmentIds?: string[]): Promise<MessageResponse> {
    const payload: SendMessageRequest = { body, replyToId: replyToId ?? null, attachmentIds };
    const result = await firstValueFrom(
      this.http.post<MessageResponse>(
        `${environment.apiBase}/chats/personal/${chatId}/messages`,
        payload,
      ),
    );
    if (this.currentChatId === chatId) {
      const existing = this._messages();
      if (!existing.some((m) => m.id === result.id)) {
        this._messages.set([...existing, result]);
      }
    }
    return result;
  }

  async edit(id: string, body: string): Promise<void> {
    await firstValueFrom(
      this.http.put<MessageResponse>(`${environment.apiBase}/messages/${id}`, { body }),
    );
  }

  async deleteMessage(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`${environment.apiBase}/messages/${id}`));
  }

  setReplyTo(msg: MessageResponse): void {
    this.replyingTo.set(msg);
  }

  clearReplyTo(): void {
    this.replyingTo.set(null);
  }

  subscribe(chatId: string): void {
    this.currentChatId = chatId;
    this.signalr.chat.on('MessageCreated', this.createdHandler);
    this.signalr.chat.on('MessageEdited', this.editedHandler);
    this.signalr.chat.on('MessageDeleted', this.deletedHandler);
    void this.signalr.joinPersonalChatGroup(chatId);
  }

  unsubscribe(): void {
    this.signalr.chat.off('MessageCreated', this.createdHandler);
    this.signalr.chat.off('MessageEdited', this.editedHandler);
    this.signalr.chat.off('MessageDeleted', this.deletedHandler);
    this.currentChatId = null;
    this._messages.set([]);
    this._hasMoreHistory.set(true);
    this._isLoadingOlder.set(false);
    this.replyingTo.set(null);
  }
}
