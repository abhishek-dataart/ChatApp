export type RoomVisibility = 'public' | 'private';
export type RoomRole = 'member' | 'admin' | 'owner';

export interface UserSummary {
  id: string;
  username: string;
  displayName: string;
  avatarUrl: string | null;
}

export interface RoomSummary {
  id: string;
  name: string;
  description: string;
  visibility: RoomVisibility;
  memberCount: number;
  capacity: number;
  createdAt: string;
  logoUrl?: string | null;
}

export interface CatalogEntry extends RoomSummary {
  isMember: boolean;
}

export interface MyRoomEntry extends RoomSummary {
  role: RoomRole;
  joinedAt: string;
}

export interface RoomMemberEntry {
  user: UserSummary;
  role: RoomRole;
  joinedAt: string;
}

export interface RoomDetailResponse extends RoomSummary {
  owner: UserSummary;
  members: RoomMemberEntry[];
  currentUserRole: RoomRole;
}

export interface CreateRoomRequest {
  name: string;
  description: string;
  visibility: RoomVisibility;
  capacity?: number;
}

export interface UpdateRoomRequest {
  name?: string;
  description?: string;
  visibility?: RoomVisibility;
}
