import { Injectable, Signal, computed, effect, inject, signal } from '@angular/core';
import { SignalrService } from '../signalr/signalr.service';
import { ActivityTrackerService } from './activity-tracker.service';
import {
  PresenceChangedEvent,
  PresenceSnapshotEvent,
  PresenceState,
} from './presence.models';

const HEARTBEAT_FALLBACK_MS = 15_000;

@Injectable({ providedIn: 'root' })
export class PresenceService {
  private readonly signalr = inject(SignalrService);
  private readonly activity = inject(ActivityTrackerService);

  private readonly _stateByUserId = signal<ReadonlyMap<string, PresenceState>>(new Map());
  private heartbeatInterval: ReturnType<typeof setInterval> | null = null;
  private lastSentActive: boolean | null = null;
  private started = false;

  constructor() {
    // Register the hub-event handlers eagerly, BEFORE signalr.start() is
    // called. The server fires PresenceSnapshot from OnConnectedAsync the
    // moment the hub connects; if we register the handler later (e.g. inside
    // start()), SignalR JS silently drops the invocation and the initial
    // contact-presence map is lost — which is why friends appeared offline
    // on login/refresh.
    const conn = this.signalr.presence;

    conn.on('PresenceChanged', (event: PresenceChangedEvent) => {
      this._stateByUserId.update((map) => {
        const next = new Map(map);
        if (event.state === 'offline') {
          next.delete(event.userId);
        } else {
          next.set(event.userId, event.state);
        }
        return next;
      });
    });

    conn.on('PresenceSnapshot', (event: PresenceSnapshotEvent) => {
      // Merge into existing state so any PresenceChanged events that landed
      // first (server -> client ordering) aren't clobbered.
      this._stateByUserId.update((map) => {
        const next = new Map(map);
        for (const entry of event.entries) {
          if (entry.state === 'offline') {
            next.delete(entry.userId);
          } else {
            next.set(entry.userId, entry.state);
          }
        }
        return next;
      });
    });

    conn.onreconnected(() => {
      // Server may have re-sent the snapshot on reconnect. Force a heartbeat
      // so the server learns this connection's current activity state again.
      this.lastSentActive = null;
      if (this.started) this.sendHeartbeat();
    });

    // Push a heartbeat the moment activity state flips. Combined with the
    // server's immediate broadcast on heartbeat, AFK <-> Online transitions
    // surface to peers within ~1s. Effect must be created in the injection
    // context (ctor); it is a no-op until start() runs.
    effect(() => {
      const active = this.activity.isActive();
      if (!this.started) return;
      if (this.lastSentActive === active) return;
      this.lastSentActive = active;
      this.sendHeartbeat(active);
    });
  }

  stateOf(userId: string): Signal<PresenceState> {
    return computed(() => this._stateByUserId().get(userId) ?? 'offline');
  }

  start(): void {
    if (this.started) return;
    this.started = true;

    // Send an initial heartbeat so the server learns this connection's
    // activity state without waiting up to 15s for the fallback tick.
    this.lastSentActive = this.activity.isActiveNow();
    this.sendHeartbeat(this.lastSentActive);

    this.heartbeatInterval = setInterval(() => this.sendHeartbeat(), HEARTBEAT_FALLBACK_MS);
  }

  stop(): void {
    if (!this.started) return;
    this.started = false;
    if (this.heartbeatInterval !== null) {
      clearInterval(this.heartbeatInterval);
      this.heartbeatInterval = null;
    }

    this._stateByUserId.set(new Map());
    this.lastSentActive = null;
    // Do NOT detach handlers here — the same PresenceService instance
    // persists across logout/login cycles, and the next login's handlers
    // must be live before signalr.start().
  }

  private sendHeartbeat(activeOverride?: boolean): void {
    const conn = this.signalr.presence;
    const isActive = activeOverride ?? this.activity.isActiveNow();
    conn.invoke('Heartbeat', isActive).catch(() => {});
  }
}
