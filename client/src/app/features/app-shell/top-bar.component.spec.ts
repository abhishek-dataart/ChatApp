import { HttpErrorResponse } from '@angular/common/http';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { LayoutService } from '../../core/layout/layout.service';
import { FriendshipsService } from '../../core/social/friendships.service';
import { UserSearchService } from '../../core/users/user-search.service';
import { TopBarComponent } from './top-bar.component';

@Component({ standalone: true, selector: 'app-top-bar', template: '' })
class Harness extends TopBarComponent {}

describe('TopBarComponent', () => {
  let auth: {
    currentUser: ReturnType<typeof signal>;
    logout: jest.Mock;
  };
  let router: { navigateByUrl: jest.Mock; navigate: jest.Mock };
  let userSearch: { search: jest.Mock };
  let friendships: { sendRequest: jest.Mock };
  let cmp: TopBarComponent;

  beforeEach(() => {
    jest.useFakeTimers();
    auth = {
      currentUser: signal({ displayName: 'Jane Doe', username: 'jane' }),
      logout: jest.fn().mockResolvedValue(undefined),
    };
    router = {
      navigateByUrl: jest.fn().mockResolvedValue(true),
      navigate: jest.fn().mockResolvedValue(true),
    };
    userSearch = { search: jest.fn().mockResolvedValue([]) };
    friendships = { sendRequest: jest.fn().mockResolvedValue(undefined) };

    TestBed.configureTestingModule({
      imports: [Harness],
      providers: [
        { provide: AuthService, useValue: auth },
        { provide: Router, useValue: router },
        { provide: UserSearchService, useValue: userSearch },
        { provide: FriendshipsService, useValue: friendships },
        { provide: LayoutService, useValue: {} },
      ],
    });
    cmp = TestBed.createComponent(Harness).componentInstance;
  });

  afterEach(() => jest.useRealTimers());

  it('computes initials from two-part display name', () => {
    expect(cmp.userInitials()).toBe('JD');
  });

  it('falls back to first two characters of single-name users', () => {
    auth.currentUser.set({ displayName: 'Cher', username: 'cher' });
    expect(cmp.userInitials()).toBe('CH');
  });

  it('returns empty initials when user is null', () => {
    auth.currentUser.set(null);
    expect(cmp.userInitials()).toBe('');
  });

  it('toggleDropdown flips the open signal', () => {
    cmp.toggleDropdown();
    expect(cmp.dropdownOpen()).toBe(true);
    cmp.toggleDropdown();
    expect(cmp.dropdownOpen()).toBe(false);
  });

  it('search input shorter than 2 chars closes the results and skips remote lookup', () => {
    cmp.onSearchInput({ target: { value: 'a' } } as unknown as Event);
    jest.advanceTimersByTime(500);
    expect(cmp.searchOpen()).toBe(false);
    expect(userSearch.search).not.toHaveBeenCalled();
  });

  it('debounced search calls the service once after 250ms', async () => {
    userSearch.search.mockResolvedValueOnce([
      { id: '1', username: 'ab', personalChatId: 'pc1' } as never,
    ]);
    cmp.onSearchInput({ target: { value: 'ab' } } as unknown as Event);
    jest.advanceTimersByTime(249);
    expect(userSearch.search).not.toHaveBeenCalled();
    jest.advanceTimersByTime(1);
    await Promise.resolve();
    await Promise.resolve();
    expect(userSearch.search).toHaveBeenCalledWith('ab');
  });

  it('signOut logs out and navigates to /login', async () => {
    await cmp.signOut();
    expect(auth.logout).toHaveBeenCalled();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login');
    expect(cmp.dropdownOpen()).toBe(false);
  });

  it('chatWith navigates to the personal chat when the result has one', async () => {
    await cmp.chatWith({ personalChatId: 'pc1' } as never);
    expect(router.navigate).toHaveBeenCalledWith(['/app/dms', 'pc1']);
  });

  it('chatWith is a no-op when there is no personalChatId', async () => {
    await cmp.chatWith({ personalChatId: null } as never);
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('openAddDialog + submitAdd sends the friend request', async () => {
    cmp.openAddDialog({ username: 'alice' } as never);
    expect(cmp.addDialogOpen()).toBe(true);
    cmp.addNote.set('hi there');
    await cmp.submitAdd();
    expect(friendships.sendRequest).toHaveBeenCalledWith('alice', 'hi there');
    expect(cmp.addDialogOpen()).toBe(false);
  });

  it('maps friendship error codes to user-friendly text', async () => {
    friendships.sendRequest.mockRejectedValueOnce(
      new HttpErrorResponse({ status: 409, error: { code: 'friendship_exists' } }),
    );
    cmp.openAddDialog({ username: 'alice' } as never);
    await cmp.submitAdd();
    expect(cmp.addError()).toMatch(/already exists/i);
  });

  it('unknown errors fall through to a generic message', async () => {
    friendships.sendRequest.mockRejectedValueOnce(new Error('boom'));
    cmp.openAddDialog({ username: 'alice' } as never);
    await cmp.submitAdd();
    expect(cmp.addError()).toMatch(/failed/i);
  });
});
