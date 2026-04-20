import { HubConnectionState } from '@microsoft/signalr';

type Handler = (...args: unknown[]) => void;

export class MockHubConnection {
  private readonly handlers = new Map<string, Set<Handler>>();
  private readonly reconnectedCallbacks: Array<(id?: string) => void> = [];
  private readonly reconnectingCallbacks: Array<(err?: Error) => void> = [];
  private readonly closedCallbacks: Array<(err?: Error) => void> = [];

  readonly invoke = jest.fn<Promise<unknown>, [string, ...unknown[]]>().mockResolvedValue(undefined);
  readonly send = jest.fn<Promise<void>, [string, ...unknown[]]>().mockResolvedValue(undefined);
  readonly start = jest.fn<Promise<void>, []>().mockImplementation(async () => {
    this.state = HubConnectionState.Connected;
  });
  readonly stop = jest.fn<Promise<void>, []>().mockImplementation(async () => {
    this.state = HubConnectionState.Disconnected;
  });

  state: HubConnectionState = HubConnectionState.Disconnected;

  on(event: string, handler: Handler): void {
    if (!this.handlers.has(event)) this.handlers.set(event, new Set());
    this.handlers.get(event)!.add(handler);
  }

  off(event: string, handler?: Handler): void {
    if (!handler) {
      this.handlers.delete(event);
      return;
    }
    this.handlers.get(event)?.delete(handler);
  }

  onreconnected(cb: (id?: string) => void): void {
    this.reconnectedCallbacks.push(cb);
  }
  onreconnecting(cb: (err?: Error) => void): void {
    this.reconnectingCallbacks.push(cb);
  }
  onclose(cb: (err?: Error) => void): void {
    this.closedCallbacks.push(cb);
  }

  /** Test helper: fire a hub event to all registered listeners. */
  emit(event: string, ...args: unknown[]): void {
    const set = this.handlers.get(event);
    if (!set) return;
    for (const h of Array.from(set)) h(...args);
  }

  /** Test helper: trigger onreconnected callbacks. */
  triggerReconnected(id = 'conn-1'): void {
    this.state = HubConnectionState.Connected;
    for (const cb of this.reconnectedCallbacks) cb(id);
  }

  triggerReconnecting(err?: Error): void {
    this.state = HubConnectionState.Reconnecting;
    for (const cb of this.reconnectingCallbacks) cb(err);
  }

  triggerClose(err?: Error): void {
    this.state = HubConnectionState.Disconnected;
    for (const cb of this.closedCallbacks) cb(err);
  }

  hasHandler(event: string): boolean {
    return (this.handlers.get(event)?.size ?? 0) > 0;
  }
}

export function createMockHubConnection(): MockHubConnection {
  return new MockHubConnection();
}
