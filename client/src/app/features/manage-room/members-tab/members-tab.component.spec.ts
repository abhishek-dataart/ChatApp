import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { PresenceService } from '../../../core/presence/presence.service';
import { ModerationService } from '../../../core/rooms/moderation.service';
import { RoomMemberEntry } from '../../../core/rooms/rooms.models';
import { MembersTabComponent } from './members-tab.component';

@Component({ standalone: true, selector: 'app-members-tab', template: '' })
class Harness extends MembersTabComponent {}

function member(role: string, id = 'u'): RoomMemberEntry {
  return {
    role: role as RoomMemberEntry['role'],
    joinedAt: '2026-01-01T00:00:00Z',
    user: { id, displayName: 'Name', username: id, avatarUrl: null } as never,
  } as RoomMemberEntry;
}

describe('MembersTabComponent', () => {
  let moderation: { ban: jest.Mock; kick: jest.Mock };
  let presence: { stateOf: jest.Mock };
  let cmp: MembersTabComponent;

  beforeEach(() => {
    moderation = {
      ban: jest.fn().mockResolvedValue(undefined),
      kick: jest.fn().mockResolvedValue(undefined),
    };
    presence = { stateOf: jest.fn().mockReturnValue(signal('online')) };

    TestBed.configureTestingModule({
      imports: [Harness],
      providers: [
        { provide: ModerationService, useValue: moderation },
        { provide: PresenceService, useValue: presence },
      ],
    });
    const fixture = TestBed.createComponent(Harness);
    cmp = fixture.componentInstance;
    cmp.roomId = 'r1';
    cmp.room = { currentUserRole: 'owner' } as never;
  });

  describe('canBan permission matrix', () => {
    it('owner can ban admins and members, not other owners', () => {
      cmp.room = { currentUserRole: 'owner' } as never;
      expect(cmp.canBan(member('member'))).toBe(true);
      expect(cmp.canBan(member('admin'))).toBe(true);
      expect(cmp.canBan(member('owner'))).toBe(false);
    });

    it('admin can only ban members', () => {
      cmp.room = { currentUserRole: 'admin' } as never;
      expect(cmp.canBan(member('member'))).toBe(true);
      expect(cmp.canBan(member('admin'))).toBe(false);
      expect(cmp.canBan(member('owner'))).toBe(false);
    });

    it('members cannot ban anyone', () => {
      cmp.room = { currentUserRole: 'member' } as never;
      expect(cmp.canBan(member('member'))).toBe(false);
    });
  });

  describe('canKick permission matrix', () => {
    it('follows the same rules as canBan', () => {
      cmp.room = { currentUserRole: 'owner' } as never;
      expect(cmp.canKick(member('owner'))).toBe(false);
      expect(cmp.canKick(member('member'))).toBe(true);
      cmp.room = { currentUserRole: 'admin' } as never;
      expect(cmp.canKick(member('admin'))).toBe(false);
      expect(cmp.canKick(member('member'))).toBe(true);
    });
  });

  it('ban() short-circuits if the user declines confirmation', async () => {
    jest.spyOn(window, 'confirm').mockReturnValue(false);
    await cmp.ban(member('member', 'u2'));
    expect(moderation.ban).not.toHaveBeenCalled();
  });

  it('ban() calls moderation and emits roomUpdated on success', async () => {
    jest.spyOn(window, 'confirm').mockReturnValue(true);
    const emitted = jest.spyOn(cmp.roomUpdated, 'emit');
    await cmp.ban(member('member', 'u2'));
    expect(moderation.ban).toHaveBeenCalledWith('r1', 'u2');
    expect(emitted).toHaveBeenCalled();
  });

  it('ban() sets error on failure, clears acting flag', async () => {
    jest.spyOn(window, 'confirm').mockReturnValue(true);
    moderation.ban.mockRejectedValueOnce(new Error('boom'));
    await cmp.ban(member('member', 'u2'));
    expect(cmp.error()).toMatch(/failed to ban/i);
    expect(cmp.acting()).toBeNull();
  });

  it('kick() calls moderation and emits on success', async () => {
    jest.spyOn(window, 'confirm').mockReturnValue(true);
    const emitted = jest.spyOn(cmp.roomUpdated, 'emit');
    await cmp.kick(member('member', 'u2'));
    expect(moderation.kick).toHaveBeenCalledWith('r1', 'u2');
    expect(emitted).toHaveBeenCalled();
  });

  it('presenceOf delegates to the presence service', () => {
    const s = cmp.presenceOf('u1');
    expect(presence.stateOf).toHaveBeenCalledWith('u1');
    expect(s()).toBe('online');
  });
});
