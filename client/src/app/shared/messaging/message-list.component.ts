import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  ViewChild,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import {
  CdkFixedSizeVirtualScroll,
  CdkVirtualForOf,
  CdkVirtualScrollViewport,
} from '@angular/cdk/scrolling';
import { LucideAngularModule, Reply, Pencil, Trash2 } from 'lucide-angular';
import { MessageResponse } from '../../core/messaging/messaging.models';
import { UiAvatarComponent } from '../ui/avatar/ui-avatar.component';
import { UiButtonComponent } from '../ui/button/ui-button.component';

@Component({
  selector: 'app-message-list',
  standalone: true,
  imports: [
    DatePipe,
    CdkVirtualScrollViewport,
    CdkFixedSizeVirtualScroll,
    CdkVirtualForOf,
    LucideAngularModule,
    UiAvatarComponent,
    UiButtonComponent,
  ],
  templateUrl: './message-list.component.html',
  styleUrl: './message-list.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MessageListComponent implements OnChanges, AfterViewInit {
  @Input() messages: MessageResponse[] = [];
  @Input({ required: true }) currentUserId!: string;
  @Input() isAdminOrOwner = false;
  @Input() isLoadingOlder = false;

  @Output() editMessage = new EventEmitter<{ id: string; body: string }>();
  @Output() deleteMessage = new EventEmitter<string>();
  @Output() replyTo = new EventEmitter<MessageResponse>();
  @Output() loadOlder = new EventEmitter<void>();

  @ViewChild(CdkVirtualScrollViewport) private viewport!: CdkVirtualScrollViewport;

  readonly itemSize = 72;
  private readonly loadOlderThreshold = 5;

  readonly editingId = signal<string | null>(null);
  readonly editBody = signal('');

  readonly ReplyIcon = Reply;
  readonly EditIcon = Pencil;
  readonly DeleteIcon = Trash2;

  private initialScrollDone = false;

  ngAfterViewInit(): void {
    if (this.messages.length > 0) {
      setTimeout(() => {
        this.scrollToBottom();
        this.initialScrollDone = true;
      });
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    const change = changes['messages'];
    if (!change || !this.viewport) return;

    const prev: MessageResponse[] = change.firstChange ? [] : ((change.previousValue as MessageResponse[]) ?? []);
    const curr = this.messages;

    if (prev.length === 0 && curr.length > 0) {
      setTimeout(() => {
        this.scrollToBottom();
        this.initialScrollDone = true;
      });
    } else if (curr.length > prev.length && curr[0]?.id !== prev[0]?.id) {
      // Prepend: capture current scroll offset, then restore after render
      const prevOffset = this.viewport.measureScrollOffset('top');
      const addedCount = curr.length - prev.length;
      setTimeout(() => {
        this.viewport?.scrollToOffset(prevOffset + addedCount * this.itemSize);
      });
    } else if (curr.length > prev.length) {
      // Live append: only scroll to bottom if already near the bottom
      if (this.viewport.measureScrollOffset('bottom') < 200) {
        setTimeout(() => this.scrollToBottom());
      }
    }
  }

  onScrolledIndexChange(firstIndex: number): void {
    if (!this.initialScrollDone) return;
    if (firstIndex <= this.loadOlderThreshold && !this.isLoadingOlder) {
      this.loadOlder.emit();
    }
  }

  trackById(_: number, msg: MessageResponse): string {
    return msg.id;
  }

  isMine(authorId: string): boolean {
    return this.currentUserId === authorId;
  }

  canDelete(msg: MessageResponse): boolean {
    return this.isMine(msg.authorId) || this.isAdminOrOwner;
  }

  startEdit(msg: MessageResponse): void {
    this.editingId.set(msg.id);
    this.editBody.set(msg.body);
  }

  cancelEdit(): void {
    this.editingId.set(null);
    this.editBody.set('');
  }

  saveEdit(id: string): void {
    const body = this.editBody().trim();
    if (!body) return;
    this.editMessage.emit({ id, body });
    this.editingId.set(null);
    this.editBody.set('');
  }

  confirmDelete(id: string): void {
    if (confirm('Delete this message?')) {
      this.deleteMessage.emit(id);
    }
  }

  openAttachment(downloadUrl: string): void {
    window.open(downloadUrl, '_blank');
  }

  formatSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  private scrollToBottom(): void {
    if (this.viewport && this.messages.length > 0) {
      this.viewport.scrollToIndex(this.messages.length - 1);
    }
  }
}
