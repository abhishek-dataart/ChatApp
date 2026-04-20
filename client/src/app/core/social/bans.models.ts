import { UserSummary } from './friendships.models';

export interface BannedUserEntry {
  banId: string;
  user: UserSummary;
  createdAt: string;
}

export interface BanListResponse {
  bans: BannedUserEntry[];
}

export interface BanStatusResponse {
  bannedByMe: boolean;
  bannedByThem: boolean;
}
