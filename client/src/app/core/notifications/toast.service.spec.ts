import { TestBed } from '@angular/core/testing';
import { ToastService } from './toast.service';

describe('ToastService', () => {
  let svc: ToastService;

  beforeEach(() => {
    jest.useFakeTimers();
    TestBed.configureTestingModule({});
    svc = TestBed.inject(ToastService);
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it('starts empty', () => {
    expect(svc.toasts()).toEqual([]);
  });

  it('adds a toast with the given severity and message', () => {
    svc.show({ severity: 'warn', message: 'careful' });
    expect(svc.toasts()).toHaveLength(1);
    expect(svc.toasts()[0]).toMatchObject({ severity: 'warn', message: 'careful' });
    expect(svc.toasts()[0].id).toBeTruthy();
  });

  it('auto-dismisses after the default 5s', () => {
    svc.show({ severity: 'info', message: 'hi' });
    expect(svc.toasts()).toHaveLength(1);
    jest.advanceTimersByTime(5000);
    expect(svc.toasts()).toHaveLength(0);
  });

  it('respects a custom durationMs', () => {
    svc.show({ severity: 'info', message: 'x', durationMs: 1000 });
    jest.advanceTimersByTime(999);
    expect(svc.toasts()).toHaveLength(1);
    jest.advanceTimersByTime(1);
    expect(svc.toasts()).toHaveLength(0);
  });

  it('dismiss removes a toast by id', () => {
    svc.show({ severity: 'info', message: 'a' });
    svc.show({ severity: 'info', message: 'b' });
    const firstId = svc.toasts()[0].id;
    svc.dismiss(firstId);
    expect(svc.toasts().map((t) => t.message)).toEqual(['b']);
  });
});
