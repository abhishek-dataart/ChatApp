import { TestBed } from '@angular/core/testing';
import { SignalrServiceStub } from '../../../testing/signalr-stub';
import { SignalrService } from '../signalr/signalr.service';
import { ActivityTrackerService } from './activity-tracker.service';
import { PresenceService } from './presence.service';

describe('PresenceService', () => {
  let svc: PresenceService;
  let signalr: SignalrServiceStub;
  let activityIsActive: boolean;

  beforeEach(() => {
    jest.useFakeTimers();
    signalr = new SignalrServiceStub();
    activityIsActive = true;
    TestBed.configureTestingModule({
      providers: [
        { provide: SignalrService, useValue: signalr },
        {
          provide: ActivityTrackerService,
          useValue: {
            isActive: () => activityIsActive,
            isActiveNow: () => activityIsActive,
          },
        },
      ],
    });
    svc = TestBed.inject(PresenceService);
  });

  afterEach(() => jest.useRealTimers());

  it('registers PresenceChanged and PresenceSnapshot handlers on construction', () => {
    expect(signalr.presence.hasHandler('PresenceChanged')).toBe(true);
    expect(signalr.presence.hasHandler('PresenceSnapshot')).toBe(true);
  });

  it('PresenceChanged "online" sets user to online', () => {
    signalr.presence.emit('PresenceChanged', { userId: 'u1', state: 'online' });
    expect(svc.stateOf('u1')()).toBe('online');
  });

  it('PresenceChanged "offline" removes the user (stateOf falls back to offline)', () => {
    signalr.presence.emit('PresenceChanged', { userId: 'u1', state: 'online' });
    signalr.presence.emit('PresenceChanged', { userId: 'u1', state: 'offline' });
    expect(svc.stateOf('u1')()).toBe('offline');
  });

  it('PresenceSnapshot merges entries without clobbering prior state', () => {
    signalr.presence.emit('PresenceChanged', { userId: 'u1', state: 'online' });
    signalr.presence.emit('PresenceSnapshot', {
      entries: [
        { userId: 'u2', state: 'afk' },
        { userId: 'u3', state: 'offline' },
      ],
    });
    expect(svc.stateOf('u1')()).toBe('online');
    expect(svc.stateOf('u2')()).toBe('afk');
    expect(svc.stateOf('u3')()).toBe('offline');
  });

  it('start() invokes an immediate Heartbeat and schedules fallback ticks', () => {
    svc.start();
    expect(signalr.presence.invoke).toHaveBeenCalledWith('Heartbeat', true);
    signalr.presence.invoke.mockClear();
    jest.advanceTimersByTime(15_000);
    expect(signalr.presence.invoke).toHaveBeenCalledWith('Heartbeat', expect.any(Boolean));
  });

  it('stop() clears state and halts fallback ticks', () => {
    svc.start();
    signalr.presence.invoke.mockClear();
    svc.stop();
    jest.advanceTimersByTime(30_000);
    expect(signalr.presence.invoke).not.toHaveBeenCalled();
  });

  it('reconnected after start re-sends a heartbeat', () => {
    svc.start();
    signalr.presence.invoke.mockClear();
    signalr.presence.triggerReconnected();
    expect(signalr.presence.invoke).toHaveBeenCalledWith('Heartbeat', true);
  });
});
