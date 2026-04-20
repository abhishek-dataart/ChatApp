import { TestBed } from '@angular/core/testing';
import { HubConnectionState } from '@microsoft/signalr';
import { MockHubConnection } from '../../../testing/mock-hub';

const builtConnections: MockHubConnection[] = [];

jest.mock('@microsoft/signalr', () => {
  // Re-require inside the mock factory to avoid hoisting issues.
  const realEnum = { Connected: 'Connected', Disconnected: 'Disconnected', Reconnecting: 'Reconnecting' };
  class HubConnectionBuilder {
    withUrl(): this {
      return this;
    }
    withAutomaticReconnect(): this {
      return this;
    }
    build(): unknown {
      // eslint-disable-next-line @typescript-eslint/no-require-imports
      const { MockHubConnection: Mock } = require('../../../testing/mock-hub');
      const conn = new Mock();
      builtConnections.push(conn);
      return conn;
    }
  }
  return {
    HubConnectionBuilder,
    HubConnectionState: realEnum,
  };
});

// Imported AFTER mock so service picks up the mocked module.
// eslint-disable-next-line import/first
import { SignalrService } from './signalr.service';

describe('SignalrService', () => {
  let svc: SignalrService;

  beforeEach(() => {
    builtConnections.length = 0;
    TestBed.configureTestingModule({});
    svc = TestBed.inject(SignalrService);
  });

  it('builds two hub connections (presence + chat)', () => {
    expect(builtConnections.length).toBe(2);
  });

  it('start() subscribes to room/friendship/invitation events and starts both hubs', async () => {
    const presence = builtConnections[0];
    const chat = builtConnections[1];
    await svc.start();
    expect(presence.start).toHaveBeenCalled();
    expect(chat.start).toHaveBeenCalled();
    expect(chat.hasHandler('RoomMemberChanged')).toBe(true);
    expect(chat.hasHandler('RoomBanned')).toBe(true);
    expect(chat.hasHandler('RoomDeleted')).toBe(true);
    expect(chat.hasHandler('FriendshipChanged')).toBe(true);
    expect(chat.hasHandler('InvitationChanged')).toBe(true);
  });

  it('forwards RoomMemberChanged payloads to the observable', async () => {
    const chat = builtConnections[1];
    await svc.start();
    const events: unknown[] = [];
    const sub = svc.roomMemberChanged$.subscribe((e) => events.push(e));
    chat.emit('RoomMemberChanged', { roomId: 'r1', userId: 'u1', kind: 'joined' });
    expect(events).toEqual([{ roomId: 'r1', userId: 'u1', kind: 'joined' }]);
    sub.unsubscribe();
  });

  it('joinRoomGroup invokes only when connected', async () => {
    const chat = builtConnections[1];
    await svc.joinRoomGroup('r1');
    expect(chat.invoke).not.toHaveBeenCalled();
    chat.state = HubConnectionState.Connected;
    await svc.joinRoomGroup('r1');
    expect(chat.invoke).toHaveBeenCalledWith('JoinRoomGroup', 'r1');
  });

  it('joinPersonalChatGroup invokes when connected', async () => {
    const chat = builtConnections[1];
    chat.state = HubConnectionState.Connected;
    await svc.joinPersonalChatGroup('p1');
    expect(chat.invoke).toHaveBeenCalledWith('JoinPersonalChatGroup', 'p1');
  });

  it('leaveRoomGroup swallows invoke errors', async () => {
    const chat = builtConnections[1];
    chat.state = HubConnectionState.Connected;
    chat.invoke.mockRejectedValueOnce(new Error('boom'));
    await expect(svc.leaveRoomGroup('r1')).resolves.toBeUndefined();
  });

  it('stop() stops both hubs', async () => {
    await svc.stop();
    expect(builtConnections[0].stop).toHaveBeenCalled();
    expect(builtConnections[1].stop).toHaveBeenCalled();
  });
});
