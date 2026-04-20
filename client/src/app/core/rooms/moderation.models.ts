import { UserSummary } from './rooms.models';

export interface RoomBanEntry {
  banId: string;
  user: UserSummary;
  bannedBy: UserSummary;
  createdAt: string;
}

export interface RoomBansResponse {
  bans: RoomBanEntry[];
}

export interface AuditEntry {
  id: string;
  actor: UserSummary;
  target: UserSummary | null;
  action: string;
  detail: string | null;
  createdAt: string;
}

export interface AuditResponse {
  items: AuditEntry[];
  nextBefore: string | null;
}

export interface RoomMemberChangedPayload {
  roomId: string;
  userId: string;
  change: 'added' | 'removed' | 'role_changed';
  role?: string | null;
}

export interface RoomBannedPayload {
  roomId: string;
  roomName: string;
  bannedBy: UserSummary;
  createdAt: string;
}

export interface RoomDeletedPayload {
  roomId: string;
}

export interface BanUserRequest {
  userId: string;
}

export interface ChangeRoleRequest {
  role: string;
}

export interface UpdateCapacityRequest {
  capacity: number;
}
