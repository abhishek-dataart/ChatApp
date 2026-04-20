import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  HostListener,
  computed,
  inject,
  signal,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { LucideAngularModule, Bell, Search, Menu, PanelRight } from 'lucide-angular';
import { AuthService } from '../../core/auth/auth.service';
import { FriendshipsService } from '../../core/social/friendships.service';
import { UserSearchResult, UserSearchService } from '../../core/users/user-search.service';
import { LayoutService } from '../../core/layout/layout.service';
import { UiAvatarComponent } from '../../shared/ui/avatar/ui-avatar.component';

@Component({
  selector: 'app-top-bar',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, LucideAngularModule, UiAvatarComponent],
  templateUrl: './top-bar.component.html',
  styleUrl: './top-bar.component.scss',
})
export class TopBarComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly el = inject(ElementRef);
  private readonly userSearch = inject(UserSearchService);
  private readonly friendships = inject(FriendshipsService);
  readonly layout = inject(LayoutService);

  readonly BellIcon = Bell;
  readonly SearchIcon = Search;
  readonly MenuIcon = Menu;
  readonly PanelRightIcon = PanelRight;

  readonly user = this.auth.currentUser;

  readonly userInitials = computed(() => {
    const u = this.user();
    if (!u) return '';
    const name = u.displayName || u.username;
    const parts = name.trim().split(/\s+/);
    if (parts.length >= 2) {
      return (parts[0][0] + parts[1][0]).toUpperCase();
    }
    return parts[0].slice(0, 2).toUpperCase();
  });

  readonly dropdownOpen = signal(false);

  readonly searchQuery = signal('');
  readonly searchResults = signal<UserSearchResult[]>([]);
  readonly searchOpen = signal(false);
  readonly searchLoading = signal(false);
  readonly searchInputVisible = signal(false);

  readonly addDialogOpen = signal(false);
  readonly addTarget = signal<UserSearchResult | null>(null);
  readonly addNote = signal('');
  readonly addSubmitting = signal(false);
  readonly addError = signal<string | null>(null);

  private searchTimer: ReturnType<typeof setTimeout> | null = null;
  private searchSeq = 0;

  toggleDropdown(): void {
    this.dropdownOpen.update((v) => !v);
  }

  toggleSearchInput(): void {
    this.searchInputVisible.update((v) => !v);
    if (!this.searchInputVisible()) {
      this.searchQuery.set('');
      this.searchResults.set([]);
      this.searchOpen.set(false);
    }
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (!this.el.nativeElement.contains(event.target)) {
      this.dropdownOpen.set(false);
      this.searchOpen.set(false);
      this.searchInputVisible.set(false);
    }
  }

  onSearchInput(event: Event): void {
    const q = (event.target as HTMLInputElement).value;
    this.searchQuery.set(q);
    if (this.searchTimer) clearTimeout(this.searchTimer);
    if (q.trim().length < 2) {
      this.searchResults.set([]);
      this.searchOpen.set(false);
      this.searchLoading.set(false);
      return;
    }
    this.searchOpen.set(true);
    this.searchLoading.set(true);
    this.searchTimer = setTimeout(() => this.runSearch(q), 250);
  }

  onSearchFocus(): void {
    if (this.searchResults().length > 0) {
      this.searchOpen.set(true);
    }
  }

  private async runSearch(q: string): Promise<void> {
    const seq = ++this.searchSeq;
    try {
      const results = await this.userSearch.search(q);
      if (seq !== this.searchSeq) return;
      this.searchResults.set(results);
      this.searchOpen.set(true);
    } catch {
      if (seq !== this.searchSeq) return;
      this.searchResults.set([]);
    } finally {
      if (seq === this.searchSeq) this.searchLoading.set(false);
    }
  }

  async chatWith(result: UserSearchResult): Promise<void> {
    if (!result.personalChatId) return;
    this.searchOpen.set(false);
    this.searchQuery.set('');
    this.searchResults.set([]);
    await this.router.navigate(['/app/dms', result.personalChatId]);
  }

  openAddDialog(result: UserSearchResult): void {
    this.addTarget.set(result);
    this.addNote.set('');
    this.addError.set(null);
    this.addDialogOpen.set(true);
    this.searchOpen.set(false);
  }

  closeAddDialog(): void {
    this.addDialogOpen.set(false);
    this.addTarget.set(null);
    this.addNote.set('');
    this.addError.set(null);
    this.addSubmitting.set(false);
  }

  onAddNoteInput(event: Event): void {
    this.addNote.set((event.target as HTMLTextAreaElement).value);
  }

  async submitAdd(): Promise<void> {
    const target = this.addTarget();
    if (!target || this.addSubmitting()) return;
    this.addSubmitting.set(true);
    this.addError.set(null);
    try {
      const note = this.addNote().trim();
      await this.friendships.sendRequest(target.username, note || undefined);
      this.closeAddDialog();
      this.searchQuery.set('');
    } catch (err) {
      this.addError.set(this.mapAddError(err));
    } finally {
      this.addSubmitting.set(false);
    }
  }

  private mapAddError(err: unknown): string {
    if (err instanceof HttpErrorResponse) {
      switch (err.error?.code) {
        case 'cannot_friend_self':
          return 'You cannot send a friend request to yourself.';
        case 'user_not_found':
          return 'User not found.';
        case 'friendship_exists':
          return 'A friend request already exists with this user.';
        case 'note_too_long':
          return 'Note must be 500 characters or fewer.';
        case 'user_banned':
          return 'Cannot send a friend request to this user.';
      }
    }
    return 'Failed to send friend request.';
  }

  async signOut(): Promise<void> {
    this.dropdownOpen.set(false);
    await this.auth.logout();
    await this.router.navigateByUrl('/login');
  }

  closeDropdown(): void {
    this.dropdownOpen.set(false);
  }
}
