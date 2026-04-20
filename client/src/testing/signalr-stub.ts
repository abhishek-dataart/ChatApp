import { MockHubConnection, createMockHubConnection } from './mock-hub';

/**
 * Lightweight stand-in for SignalrService — exposes the same surface used by
 * consuming services so tests can wire it in without importing the real
 * @microsoft/signalr transport.
 */
export class SignalrServiceStub {
  readonly presence = createMockHubConnection();
  readonly chat = createMockHubConnection();

  readonly joinRoomGroup = jest.fn<Promise<void>, [string]>().mockResolvedValue(undefined);
  readonly leaveRoomGroup = jest.fn<Promise<void>, [string]>().mockResolvedValue(undefined);
  readonly joinPersonalChatGroup = jest
    .fn<Promise<void>, [string]>()
    .mockResolvedValue(undefined);
  readonly start = jest.fn<Promise<void>, []>().mockResolvedValue(undefined);
  readonly stop = jest.fn<Promise<void>, []>().mockResolvedValue(undefined);
}

export type { MockHubConnection };
