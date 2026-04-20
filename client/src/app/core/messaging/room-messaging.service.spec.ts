import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { SignalrServiceStub } from '../../../testing/signalr-stub';
import { SignalrService } from '../signalr/signalr.service';
import { MessageResponse } from './messaging.models';
import { RoomMessagingService } from './room-messaging.service';

function makeMsg(partial: Partial<MessageResponse> = {}): MessageResponse {
  return {
    id: partial.id ?? 'm1',
    scope: 'room',
    personalChatId: null,
    roomId: 'r1',
    authorId: 'u1',
    authorUsername: 'a',
    authorDisplayName: 'A',
    authorAvatarUrl: null,
    body: 'hi',
    replyToId: null,
    replyToBody: null,
    replyToAuthorDisplayName: null,
    createdAt: '2026-01-01T00:00:00Z',
    editedAt: null,
    attachments: [],
    ...partial,
  };
}

describe('RoomMessagingService', () => {
  let svc: RoomMessagingService;
  let http: HttpTestingController;
  let signalr: SignalrServiceStub;

  const urlMatch = (suffix: string) => (r: { url: string }) =>
    r.url === `${environment.apiBase}/chats/room/${suffix}`;
  /** For POST/PUT/DELETE — no query string, exact url works. */
  const url = (suffix: string) => `${environment.apiBase}/chats/room/${suffix}`;

  beforeEach(() => {
    signalr = new SignalrServiceStub();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SignalrService, useValue: signalr },
      ],
    });
    svc = TestBed.inject(RoomMessagingService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loadHistory stores messages and flags hasMore when full page returned', async () => {
    const page = Array.from({ length: 50 }, (_, i) => makeMsg({ id: `m${i}` }));
    const p = svc.loadHistory('r1');
    const req = http.expectOne((r) => r.url.endsWith('/chats/room/r1/messages'));
    expect(req.request.params.get('limit')).toBe('50');
    req.flush(page);
    await p;
    expect(svc.messages()).toHaveLength(50);
    expect(svc.hasMoreHistory()).toBe(true);
  });

  it('loadHistory clears hasMore when the first page is short', async () => {
    const p = svc.loadHistory('r1');
    http.expectOne(urlMatch('r1/messages')).flush([makeMsg()]);
    await p;
    expect(svc.hasMoreHistory()).toBe(false);
  });

  it('loadOlder uses keyset (beforeCreatedAt/beforeId) from the oldest message', async () => {
    const first = svc.loadHistory('r1');
    http
      .expectOne(urlMatch('r1/messages'))
      .flush([makeMsg({ id: 'old', createdAt: '2026-01-01T00:00:00Z' })]);
    await first;
    // force hasMore true so loadOlder will run
    (svc as unknown as { _hasMoreHistory: { set: (v: boolean) => void } })._hasMoreHistory.set(
      true,
    );
    const p = svc.loadOlder();
    const req = http.expectOne((r) => r.url.endsWith('/chats/room/r1/messages'));
    expect(req.request.params.get('beforeCreatedAt')).toBe('2026-01-01T00:00:00Z');
    expect(req.request.params.get('beforeId')).toBe('old');
    req.flush([makeMsg({ id: 'older' })]);
    await p;
    expect(svc.messages().map((m) => m.id)).toEqual(['older', 'old']);
    expect(svc.hasMoreHistory()).toBe(false);
  });

  it('send appends the returned message optimistically without duplication', async () => {
    const first = svc.loadHistory('r1');
    http.expectOne(urlMatch('r1/messages')).flush([]);
    await first;

    const p = svc.send('r1', 'hello');
    const post = http.expectOne(url('r1/messages'));
    expect(post.request.method).toBe('POST');
    const result = makeMsg({ id: 'new', body: 'hello' });
    post.flush(result);
    await p;
    expect(svc.messages().map((m) => m.id)).toEqual(['new']);

    // simulate the SignalR echo — must not duplicate
    signalr.chat.emit('MessageCreated', result);
    expect(svc.messages()).toHaveLength(1);
  });

  it('subscribe wires hub handlers and joins the room group', () => {
    svc.subscribe('r1');
    expect(signalr.chat.hasHandler('MessageCreated')).toBe(true);
    expect(signalr.chat.hasHandler('MessageEdited')).toBe(true);
    expect(signalr.chat.hasHandler('MessageDeleted')).toBe(true);
    expect(signalr.joinRoomGroup).toHaveBeenCalledWith('r1');
  });

  it('MessageCreated handler ignores events for other rooms', async () => {
    svc.subscribe('r1');
    const p = svc.loadHistory('r1');
    http.expectOne(urlMatch('r1/messages')).flush([]);
    await p;
    signalr.chat.emit('MessageCreated', makeMsg({ id: 'x', roomId: 'r2' }));
    expect(svc.messages()).toHaveLength(0);
  });

  it('MessageEdited handler updates the matching message in place', async () => {
    svc.subscribe('r1');
    const p = svc.loadHistory('r1');
    http.expectOne(urlMatch('r1/messages')).flush([makeMsg({ id: 'm1', body: 'old' })]);
    await p;
    signalr.chat.emit('MessageEdited', makeMsg({ id: 'm1', body: 'new' }));
    expect(svc.messages()[0].body).toBe('new');
  });

  it('MessageDeleted handler removes the message', async () => {
    svc.subscribe('r1');
    const p = svc.loadHistory('r1');
    http.expectOne(urlMatch('r1/messages')).flush([makeMsg({ id: 'm1' })]);
    await p;
    signalr.chat.emit('MessageDeleted', {
      id: 'm1',
      scope: 'room',
      personalChatId: null,
      roomId: 'r1',
    });
    expect(svc.messages()).toHaveLength(0);
  });

  it('unsubscribe resets state and detaches handlers', () => {
    svc.subscribe('r1');
    svc.unsubscribe();
    expect(signalr.chat.hasHandler('MessageCreated')).toBe(false);
    expect(svc.messages()).toEqual([]);
    expect(svc.hasMoreHistory()).toBe(true);
  });

  it('replyingTo signal is managed by setReplyTo/clearReplyTo', () => {
    const m = makeMsg();
    svc.setReplyTo(m);
    expect(svc.replyingTo()).toBe(m);
    svc.clearReplyTo();
    expect(svc.replyingTo()).toBeNull();
  });
});
