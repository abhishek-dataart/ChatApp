import { Injectable, signal } from '@angular/core';

export type ContextPanelContent =
  | { type: 'room'; roomId: string }
  | { type: 'dm'; partnerId: string; chatId: string }
  | { type: 'none' };

@Injectable({ providedIn: 'root' })
export class ContextPanelService {
  readonly content = signal<ContextPanelContent>({ type: 'none' });

  set(content: ContextPanelContent): void {
    this.content.set(content);
  }

  clear(): void {
    this.content.set({ type: 'none' });
  }
}
