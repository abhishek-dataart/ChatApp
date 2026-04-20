import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  Output,
  ViewChild,
  inject,
  signal,
} from '@angular/core';
import { LucideAngularModule, Paperclip, Send, Smile } from 'lucide-angular';
import { AttachmentsService } from '../../core/messaging/attachments.service';
import { MessageResponse, PendingAttachment } from '../../core/messaging/messaging.models';
import { ToastService } from '../../core/notifications/toast.service';

@Component({
  selector: 'app-message-composer',
  standalone: true,
  imports: [LucideAngularModule],
  templateUrl: './message-composer.component.html',
  styleUrl: './message-composer.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MessageComposerComponent {
  @Input() disabled = false;
  @Input() replyingTo: MessageResponse | null = null;
  @Output() send = new EventEmitter<{ body: string; replyToId?: string | null; attachmentIds?: string[] }>();
  @Output() cancelReply = new EventEmitter<void>();

  @ViewChild('fileInput') private fileInput!: ElementRef<HTMLInputElement>;
  @ViewChild('textArea') private textArea!: ElementRef<HTMLTextAreaElement>;

  private readonly attachmentsService = inject(AttachmentsService);
  private readonly toast = inject(ToastService);

  readonly PaperclipIcon = Paperclip;
  readonly SendIcon = Send;
  readonly SmileIcon = Smile;

  readonly emojiPickerOpen = signal(false);
  readonly commonEmojis: readonly string[] = [
    '😀', '😂', '😍', '😎', '😭', '😡', '👍', '👎',
    '🙏', '👏', '🎉', '🔥', '💯', '❤️', '💔', '✨',
    '🤔', '😅', '😊', '🙌', '💪', '🚀', '✅', '❌',
  ];

  readonly body = signal('');
  readonly byteCount = signal(0);
  readonly pending = signal<PendingAttachment[]>([]);
  readonly dragOver = signal(false);

  private static readonly MaxBytes = 3000;

  onInput(event: Event): void {
    const value = (event.target as HTMLTextAreaElement).value;
    this.body.set(value);
    this.byteCount.set(new TextEncoder().encode(value).length);
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.submit();
    }
  }

  onPaste(event: ClipboardEvent): void {
    const items = event.clipboardData?.items ?? [];
    for (const item of Array.from(items)) {
      if (item.kind === 'file') {
        const file = item.getAsFile();
        if (file) {
          this.queue(file);
          event.preventDefault();
        }
      }
    }
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragOver.set(true);
  }

  onDragLeave(): void {
    this.dragOver.set(false);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragOver.set(false);
    for (const file of Array.from(event.dataTransfer?.files ?? [])) {
      this.queue(file);
    }
  }

  toggleEmojiPicker(): void {
    this.emojiPickerOpen.update((v) => !v);
  }

  insertEmoji(emoji: string): void {
    const ta = this.textArea?.nativeElement;
    const current = this.body();
    if (ta && typeof ta.selectionStart === 'number') {
      const start = ta.selectionStart;
      const end = ta.selectionEnd ?? start;
      const next = current.slice(0, start) + emoji + current.slice(end);
      this.body.set(next);
      this.byteCount.set(new TextEncoder().encode(next).length);
      queueMicrotask(() => {
        ta.focus();
        const pos = start + emoji.length;
        ta.setSelectionRange(pos, pos);
      });
    } else {
      const next = current + emoji;
      this.body.set(next);
      this.byteCount.set(new TextEncoder().encode(next).length);
    }
    this.emojiPickerOpen.set(false);
  }

  openFilePicker(): void {
    this.fileInput.nativeElement.click();
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    for (const file of Array.from(input.files ?? [])) {
      this.queue(file);
    }
    input.value = '';
  }

  updateComment(localId: string, comment: string): void {
    this.pending.update((ps) =>
      ps.map((p) => (p.localId === localId ? { ...p, comment } : p)),
    );
  }

  removeAttachment(localId: string): void {
    const p = this.pending().find((x) => x.localId === localId);
    if (p?.previewUrl) URL.revokeObjectURL(p.previewUrl);
    this.pending.update((ps) => ps.filter((x) => x.localId !== localId));
  }

  retryAttachment(localId: string): void {
    const p = this.pending().find((x) => x.localId === localId);
    if (p) this.uploadPending(p);
  }

  submit(): void {
    const text = this.body().trim();
    const readyAttachments = this.pending().filter((p) => p.status === 'ready');
    const hasContent = text.length > 0 || readyAttachments.length > 0;

    if (!hasContent || this.disabled || this.overLimit || this.uploading) return;

    this.send.emit({
      body: text,
      replyToId: this.replyingTo?.id ?? null,
      attachmentIds: readyAttachments.map((p) => p.uploadId!),
    });

    this.body.set('');
    this.byteCount.set(0);
    this.pending().forEach((p) => { if (p.previewUrl) URL.revokeObjectURL(p.previewUrl); });
    this.pending.set([]);
  }

  get overLimit(): boolean {
    return this.byteCount() > MessageComposerComponent.MaxBytes;
  }

  get uploading(): boolean {
    return this.pending().some((p) => p.status === 'uploading');
  }

  get canSend(): boolean {
    const text = this.body().trim();
    const hasReady = this.pending().some((p) => p.status === 'ready');
    return (text.length > 0 || hasReady) && !this.disabled && !this.overLimit && !this.uploading;
  }

  private async queue(file: File): Promise<void> {
    const limit = this.attachmentsService.limitFor(file);
    if (file.size > limit) {
      this.toast.show({
        severity: 'error',
        message: `"${file.name}" is ${this.formatBytes(file.size)} — exceeds the ${this.formatBytes(limit)} limit.`,
      });
      return;
    }
    const previewUrl = file.type.startsWith('image/') ? URL.createObjectURL(file) : null;
    const local: PendingAttachment = {
      localId: crypto.randomUUID(),
      file,
      previewUrl,
      status: 'uploading',
      comment: '',
    };
    this.pending.update((ps) => [...ps, local]);
    await this.uploadPending(local);
  }

  private formatBytes(bytes: number): string {
    if (bytes >= 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
    if (bytes >= 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${bytes} B`;
  }

  private async uploadPending(local: PendingAttachment): Promise<void> {
    this.pending.update((ps) =>
      ps.map((p) => (p.localId === local.localId ? { ...p, status: 'uploading' as const } : p)),
    );
    try {
      const result = await this.attachmentsService.upload(local.file, local.comment || undefined);
      this.pending.update((ps) =>
        ps.map((p) =>
          p.localId === local.localId
            ? { ...p, uploadId: result.id, status: 'ready' as const }
            : p,
        ),
      );
    } catch {
      this.pending.update((ps) =>
        ps.map((p) =>
          p.localId === local.localId ? { ...p, status: 'failed' as const } : p,
        ),
      );
    }
  }
}
