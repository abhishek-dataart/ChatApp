import { HttpErrorResponse } from '@angular/common/http';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { RoomsService } from '../../../core/rooms/rooms.service';
import { CreateRoomDialogComponent } from './create-room-dialog.component';

@Component({ standalone: true, selector: 'app-create-room-dialog', template: '' })
class Harness extends CreateRoomDialogComponent {}

describe('CreateRoomDialogComponent', () => {
  let rooms: { create: jest.Mock; uploadLogo: jest.Mock };
  let cmp: CreateRoomDialogComponent;

  beforeEach(() => {
    rooms = {
      create: jest.fn().mockResolvedValue({ id: 'r1' }),
      uploadLogo: jest.fn().mockResolvedValue({ id: 'r1' }),
    };
    TestBed.configureTestingModule({
      imports: [Harness],
      providers: [{ provide: RoomsService, useValue: rooms }],
    });
    cmp = TestBed.createComponent(Harness).componentInstance;
  });

  it('starts invalid with defaults', () => {
    expect(cmp.form.valid).toBe(false);
    expect(cmp.form.controls.visibility.value).toBe('public');
  });

  it('submit() is a no-op when invalid', async () => {
    await cmp.submit();
    expect(rooms.create).not.toHaveBeenCalled();
  });

  it('submit() creates the room and emits roomCreated with the id', async () => {
    const emitted = jest.spyOn(cmp.roomCreated, 'emit');
    cmp.form.setValue({
      name: 'abc',
      description: 'hello',
      visibility: 'public',
      capacity: 10,
    });
    await cmp.submit();
    expect(rooms.create).toHaveBeenCalledWith({
      name: 'abc',
      description: 'hello',
      visibility: 'public',
      capacity: 10,
    });
    expect(emitted).toHaveBeenCalledWith('r1');
  });

  it('uploads logo after create when a logo file is present', async () => {
    cmp.form.setValue({
      name: 'abc',
      description: 'hello',
      visibility: 'public',
      capacity: null,
    });
    const file = new File([new Uint8Array(10)], 'logo.png', { type: 'image/png' });
    cmp.logoFile.set(file);
    await cmp.submit();
    expect(rooms.uploadLogo).toHaveBeenCalledWith('r1', file);
  });

  it('logo upload failure warns but still emits roomCreated', async () => {
    rooms.uploadLogo.mockRejectedValueOnce(new Error('boom'));
    cmp.form.setValue({
      name: 'abc',
      description: 'hello',
      visibility: 'public',
      capacity: null,
    });
    cmp.logoFile.set(new File([new Uint8Array(1)], 'l.png', { type: 'image/png' }));
    const emitted = jest.spyOn(cmp.roomCreated, 'emit');
    await cmp.submit();
    expect(emitted).toHaveBeenCalledWith('r1');
    expect(cmp.logoWarning()).toMatch(/logo upload failed/i);
  });

  it('maps server validation codes to field errors', async () => {
    rooms.create.mockRejectedValueOnce(
      new HttpErrorResponse({ status: 409, error: { code: 'room_name_taken' } }),
    );
    cmp.form.setValue({
      name: 'taken',
      description: 'hello',
      visibility: 'public',
      capacity: null,
    });
    await cmp.submit();
    expect(cmp.fieldErrors()['name']).toMatch(/already exists/i);
  });

  it('sets a general error for unknown failures', async () => {
    rooms.create.mockRejectedValueOnce(new Error('network'));
    cmp.form.setValue({
      name: 'abc',
      description: 'hello',
      visibility: 'public',
      capacity: null,
    });
    await cmp.submit();
    expect(cmp.generalError()).toMatch(/failed to create room/i);
  });

  it('onLogoChange rejects files larger than 1 MB', () => {
    const huge = new File([new Uint8Array(1_048_577)], 'big.png', { type: 'image/png' });
    const input = document.createElement('input');
    input.type = 'file';
    // jsdom has no real files list, so fake one
    Object.defineProperty(input, 'files', { value: [huge] });
    cmp.onLogoChange({ target: input } as unknown as Event);
    expect(cmp.logoWarning()).toMatch(/1 MB or smaller/i);
    expect(cmp.logoFile()).toBeNull();
  });

  it('clearLogo resets all logo state', () => {
    cmp.logoFile.set(new File([new Uint8Array(1)], 'a.png', { type: 'image/png' }));
    cmp.logoPreview.set('data:');
    cmp.clearLogo();
    expect(cmp.logoFile()).toBeNull();
    expect(cmp.logoPreview()).toBeNull();
  });
});
