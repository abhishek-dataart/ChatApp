import { Injectable, signal } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  IRetryPolicy,
  RetryContext,
} from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { environment } from '../../../environments/environment';
import { RoomBannedPayload, RoomDeletedPayload, RoomMemberChangedPayload } from '../rooms/moderation.models';

export interface FriendshipChangedPayload {
  friendshipId: string;
  kind: string;
}

export interface InvitationChangedPayload {
  invitationId: string;
  kind: string;
}

const retryPolicy: IRetryPolicy = {
  nextRetryDelayInMilliseconds(ctx: RetryContext): number {
    return Math.min(1000 * Math.pow(2, ctx.previousRetryCount), 30_000);
  },
};

@Injectable({ providedIn: 'root' })
export class SignalrService {
  private readonly presenceConn: HubConnection = new HubConnectionBuilder()
    .withUrl(`${environment.hubBase}/presence`, { withCredentials: true })
    .withAutomaticReconnect(retryPolicy)
    .build();

  private readonly chatConn: HubConnection = new HubConnectionBuilder()
    .withUrl(`${environment.hubBase}/chat`, { withCredentials: true })
    .withAutomaticReconnect(retryPolicy)
    .build();

  readonly presenceState = signal<HubConnectionState>(this.presenceConn.state);
  readonly chatState = signal<HubConnectionState>(this.chatConn.state);

  private readonly _roomMemberChanged = new Subject<RoomMemberChangedPayload>();
  private readonly _roomBanned = new Subject<RoomBannedPayload>();
  private readonly _roomDeleted = new Subject<RoomDeletedPayload>();
  private readonly _friendshipChanged = new Subject<FriendshipChangedPayload>();
  private readonly _invitationChanged = new Subject<InvitationChangedPayload>();

  readonly roomMemberChanged$ = this._roomMemberChanged.asObservable();
  readonly roomBanned$ = this._roomBanned.asObservable();
  readonly roomDeleted$ = this._roomDeleted.asObservable();
  readonly friendshipChanged$ = this._friendshipChanged.asObservable();
  readonly invitationChanged$ = this._invitationChanged.asObservable();

  constructor() {
    this.presenceConn.onreconnecting(() =>
      this.presenceState.set(HubConnectionState.Reconnecting),
    );
    this.presenceConn.onreconnected(() =>
      this.presenceState.set(HubConnectionState.Connected),
    );
    this.presenceConn.onclose(() =>
      this.presenceState.set(HubConnectionState.Disconnected),
    );

    this.chatConn.onreconnecting(() =>
      this.chatState.set(HubConnectionState.Reconnecting),
    );
    this.chatConn.onreconnected(() =>
      this.chatState.set(HubConnectionState.Connected),
    );
    this.chatConn.onclose(() =>
      this.chatState.set(HubConnectionState.Disconnected),
    );
  }

  async start(): Promise<void> {
    this.chatConn.on('RoomMemberChanged', (payload: RoomMemberChangedPayload) =>
      this._roomMemberChanged.next(payload),
    );
    this.chatConn.on('RoomBanned', (payload: RoomBannedPayload) =>
      this._roomBanned.next(payload),
    );
    this.chatConn.on('RoomDeleted', (payload: RoomDeletedPayload) =>
      this._roomDeleted.next(payload),
    );
    this.chatConn.on('FriendshipChanged', (payload: FriendshipChangedPayload) =>
      this._friendshipChanged.next(payload),
    );
    this.chatConn.on('InvitationChanged', (payload: InvitationChangedPayload) =>
      this._invitationChanged.next(payload),
    );

    await Promise.all([
      this.presenceConn
        .start()
        .then(() => this.presenceState.set(HubConnectionState.Connected)),
      this.chatConn
        .start()
        .then(() => this.chatState.set(HubConnectionState.Connected)),
    ]);
  }

  async stop(): Promise<void> {
    await Promise.all([this.presenceConn.stop(), this.chatConn.stop()]);
  }

  async joinRoomGroup(roomId: string): Promise<void> {
    if (this.chatConn.state !== HubConnectionState.Connected) return;
    try {
      await this.chatConn.invoke('JoinRoomGroup', roomId);
    } catch {
      // hub may have torn down; ignore
    }
  }

  async leaveRoomGroup(roomId: string): Promise<void> {
    if (this.chatConn.state !== HubConnectionState.Connected) return;
    try {
      await this.chatConn.invoke('LeaveRoomGroup', roomId);
    } catch {
      // ignore
    }
  }

  async joinPersonalChatGroup(chatId: string): Promise<void> {
    if (this.chatConn.state !== HubConnectionState.Connected) return;
    try {
      await this.chatConn.invoke('JoinPersonalChatGroup', chatId);
    } catch {
      // ignore
    }
  }

  get presence(): HubConnection {
    return this.presenceConn;
  }

  get chat(): HubConnection {
    return this.chatConn;
  }
}
