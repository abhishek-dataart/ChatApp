import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { SignalrServiceStub } from '../../../testing/signalr-stub';
import { SignalrService } from '../signalr/signalr.service';
import { DmService } from './dm.service';
import { MessageResponse } from './messaging.models';

function makeDm(partial: Partial<MessageResponse> = {}): MessageResponse {
  return {
    id: partial.id ?? 'dm1',
    scope: 'personal',
    personalChatId: 'p1',
    roomId: null,
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

describe('DmService', () => {
  let svc: DmService;
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
    svc = TestBed.inject(DmService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const path = `${environment.apiBase}/chats/personal/p1/messages`;
  const byPath = (r: { url: string }) => r.url === path;

  it('loadHistory loads into the messages signal', async () => {
    const p = svc.loadHistory('p1');
    http.expectOne(byPath).flush([makeDm()]);
    await p;
    expect(svc.messages()).toHaveLength(1);
    expect(svc.hasMoreHistory()).toBe(false);
  });

  it('subscribe joins the personal-chat group', () => {
    svc.subscribe('p1');
    expect(signalr.joinPersonalChatGroup).toHaveBeenCalledWith('p1');
  });

  it('only MessageCreated events for the current chat are applied', async () => {
    svc.subscribe('p1');
    const p = svc.loadHistory('p1');
    http.expectOne(byPath).flush([]);
    await p;
    signalr.chat.emit('MessageCreated', makeDm({ id: 'x', personalChatId: 'other' }));
    expect(svc.messages()).toHaveLength(0);
    signalr.chat.emit('MessageCreated', makeDm({ id: 'y' }));
    expect(svc.messages()).toHaveLength(1);
  });

  it('send POSTs and appends on current chat', async () => {
    const first = svc.loadHistory('p1');
    http.expectOne(byPath).flush([]);
    await first;
    const p = svc.send('p1', 'hey');
    const req = http.expectOne(path);
    expect(req.request.method).toBe('POST');
    req.flush(makeDm({ id: 'new', body: 'hey' }));
    await p;
    expect(svc.messages().map((m) => m.id)).toEqual(['new']);
  });

  it('edit/deleteMessage hit /messages/:id', async () => {
    const editP = svc.edit('mX', 'new body');
    const editReq = http.expectOne(`${environment.apiBase}/messages/mX`);
    expect(editReq.request.method).toBe('PUT');
    editReq.flush({});
    await editP;

    const delP = svc.deleteMessage('mX');
    const delReq = http.expectOne(`${environment.apiBase}/messages/mX`);
    expect(delReq.request.method).toBe('DELETE');
    delReq.flush({});
    await delP;
  });
});
