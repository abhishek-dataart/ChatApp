import { TestBed } from '@angular/core/testing';
import { ActivityTrackerService } from './activity-tracker.service';

describe('ActivityTrackerService', () => {
  let svc: ActivityTrackerService;
  let nowMs: number;

  beforeEach(() => {
    jest.useFakeTimers();
    nowMs = 0;
    jest.spyOn(performance, 'now').mockImplementation(() => nowMs);
    TestBed.configureTestingModule({});
    svc = TestBed.inject(ActivityTrackerService);
  });

  afterEach(() => {
    jest.useRealTimers();
    jest.restoreAllMocks();
  });

  it('starts active when the tab is visible and there is fresh activity', () => {
    expect(svc.isActive()).toBe(true);
    expect(svc.isActiveNow()).toBe(true);
  });

  it('goes inactive after the 60s AFK threshold with no input', () => {
    nowMs = 61_000;
    // advance past two poll intervals so the `now` signal refreshes
    jest.advanceTimersByTime(10_000);
    expect(svc.isActive()).toBe(false);
  });

  it('stays active when a keydown arrives within the window', () => {
    nowMs = 30_000;
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'a' }));
    nowMs = 80_000;
    jest.advanceTimersByTime(5_000);
    // lastActivity was 30_000, now is 80_000 — diff 50_000 < 60_000
    expect(svc.isActive()).toBe(true);
  });

  it('reports inactive when document becomes hidden', () => {
    Object.defineProperty(document, 'hidden', { configurable: true, value: true });
    document.dispatchEvent(new Event('visibilitychange'));
    expect(svc.isActive()).toBe(false);
    Object.defineProperty(document, 'hidden', { configurable: true, value: false });
    document.dispatchEvent(new Event('visibilitychange'));
    expect(svc.isActive()).toBe(true);
  });
});
