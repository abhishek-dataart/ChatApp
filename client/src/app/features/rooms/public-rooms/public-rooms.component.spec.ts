import { HttpErrorResponse } from '@angular/common/http';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { RoomsService } from '../../../core/rooms/rooms.service';
import { PublicRoomsComponent } from './public-rooms.component';

@Component({ standalone: true, selector: 'app-public-rooms', template: '' })
class Harness extends PublicRoomsComponent {}

describe('PublicRoomsComponent', () => {
  let rooms: {
    catalog: ReturnType<typeof signal>;
    refreshCatalog: jest.Mock;
    join: jest.Mock;
  };
  let router: { navigate: jest.Mock };
  let cmp: PublicRoomsComponent;

  beforeEach(() => {
    jest.useFakeTimers();
    rooms = {
      catalog: signal([]),
      refreshCatalog: jest.fn().mockResolvedValue(undefined),
      join: jest.fn().mockResolvedValue({ id: 'r1' }),
    };
    router = { navigate: jest.fn().mockResolvedValue(true) };
    TestBed.configureTestingModule({
      imports: [Harness],
      providers: [
        { provide: RoomsService, useValue: rooms },
        { provide: Router, useValue: router },
      ],
    });
    cmp = TestBed.createComponent(Harness).componentInstance;
  });

  afterEach(() => jest.useRealTimers());

  it('refreshes the catalog on init', async () => {
    await cmp.ngOnInit();
    expect(rooms.refreshCatalog).toHaveBeenCalledTimes(1);
  });

  it('debounces search input to 200ms', () => {
    cmp.onSearchInput({ target: { value: 'abc' } } as unknown as Event);
    expect(rooms.refreshCatalog).not.toHaveBeenCalled();
    jest.advanceTimersByTime(199);
    expect(rooms.refreshCatalog).not.toHaveBeenCalled();
    jest.advanceTimersByTime(1);
    expect(rooms.refreshCatalog).toHaveBeenCalledWith('abc');
  });

  it('collapses rapid keystrokes into a single refresh', () => {
    cmp.onSearchInput({ target: { value: 'a' } } as unknown as Event);
    cmp.onSearchInput({ target: { value: 'ab' } } as unknown as Event);
    cmp.onSearchInput({ target: { value: 'abc' } } as unknown as Event);
    jest.advanceTimersByTime(200);
    expect(rooms.refreshCatalog).toHaveBeenCalledTimes(1);
    expect(rooms.refreshCatalog).toHaveBeenCalledWith('abc');
  });

  it('empty search passes undefined (not an empty string)', () => {
    cmp.onSearchInput({ target: { value: '' } } as unknown as Event);
    jest.advanceTimersByTime(200);
    expect(rooms.refreshCatalog).toHaveBeenCalledWith(undefined);
  });

  it('join navigates on success', async () => {
    await cmp.join('r1');
    expect(rooms.join).toHaveBeenCalledWith('r1');
    expect(router.navigate).toHaveBeenCalledWith(['/app/rooms', 'r1']);
    expect(cmp.error()).toBeNull();
  });

  it('join surfaces server error codes as friendly copy', async () => {
    rooms.join.mockRejectedValueOnce(
      new HttpErrorResponse({ status: 409, error: { code: 'room_full' } }),
    );
    await cmp.join('r1');
    expect(cmp.error()).toMatch(/full/i);
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('onRoomCreated closes the dialog and navigates to the new room', async () => {
    cmp.showCreate.set(true);
    await cmp.onRoomCreated('new-id');
    expect(cmp.showCreate()).toBe(false);
    expect(router.navigate).toHaveBeenCalledWith(['/app/rooms', 'new-id']);
  });
});
