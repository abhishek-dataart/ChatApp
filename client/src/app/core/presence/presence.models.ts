export type PresenceState = 'online' | 'afk' | 'offline';

export interface PresenceChangedEvent {
  userId: string;
  state: PresenceState;
}

export interface PresenceSnapshotEvent {
  entries: { userId: string; state: PresenceState }[];
}
