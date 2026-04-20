import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { SignalrServiceStub } from '../../../testing/signalr-stub';
import { SignalrService } from '../signalr/signalr.service';
import { UnreadService } from './unread.service';

describe('UnreadService', () => {
  let svc: UnreadService;
  let http: HttpTestingController;
  let signalr: SignalrServiceStub;

  beforeEach(() => {
    signalr = new SignalrServiceStub();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SignalrService, useValue: signalr },
      ],
    });
    svc = TestBed.inject(UnreadService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loadAll stores per-scope counts keyed by "scope:id"', async () => {
    const p = svc.loadAll();
    http.expectOne(`${environment.apiBase}/chats/unread`).flush([
      { scope: 'room', scopeId: 'r1', unreadCount: 3 },
      { scope: 'personal', scopeId: 'p1', unreadCount: 2 },
    ]);
    await p;
    expect(svc.countFor('room', 'r1')).toBe(3);
    expect(svc.countFor('personal', 'p1')).toBe(2);
    expect(svc.countFor('room', 'missing')).toBe(0);
  });

  it('subscribe wires UnreadChanged', () => {
    svc.subscribe();
    expect(signalr.chat.hasHandler('UnreadChanged')).toBe(true);
  });

  it('UnreadChanged updates the count for non-active scope', () => {
    svc.subscribe();
    signalr.chat.emit('UnreadChanged', { scope: 'room', scopeId: 'r9', unreadCount: 7 });
    expect(svc.countFor('room', 'r9')).toBe(7);
  });

  it('UnreadChanged on the active scope keeps count at 0 and calls read endpoint', () => {
    svc.subscribe();
    svc.setActive('room', 'r1');
    // setActive synchronously issues a POST to /read — handle it first
    http
      .expectOne(`${environment.apiBase}/chats/room/r1/read`)
      .flush({});

    signalr.chat.emit('UnreadChanged', { scope: 'room', scopeId: 'r1', unreadCount: 4 });
    expect(svc.countFor('room', 'r1')).toBe(0);
    http
      .expectOne(`${environment.apiBase}/chats/room/r1/read`)
      .flush({});
  });

  it('markRead zeroes the count and POSTs', async () => {
    const p = svc.markRead('room', 'rX');
    expect(svc.countFor('room', 'rX')).toBe(0);
    http.expectOne(`${environment.apiBase}/chats/room/rX/read`).flush({});
    await p;
  });

  it('clearActive unsets the active key', () => {
    svc.setActive('personal', 'p1');
    http.expectOne(`${environment.apiBase}/chats/personal/p1/read`).flush({});
    svc.clearActive();
    expect(svc.activeKey()).toBeNull();
  });

  it('chat.onreconnected callback triggers a reload', () => {
    expect(signalr.chat.hasHandler).toBeDefined();
    // trigger the reconnected callback registered in the ctor
    signalr.chat.triggerReconnected();
    http.expectOne(`${environment.apiBase}/chats/unread`).flush([]);
  });
});
