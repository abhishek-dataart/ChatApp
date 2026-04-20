export interface UserSummary {
  id: string;
  username: string;
  displayName: string;
  avatarUrl: string | null;
}

export interface FriendSummary {
  friendshipId: string;
  personalChatId: string;
  user: UserSummary;
  acceptedAt: string;
}

export interface PendingFriendship {
  friendshipId: string;
  user: UserSummary;
  note: string | null;
  createdAt: string;
}

export interface FriendshipListResponse {
  friends: FriendSummary[];
  incoming: PendingFriendship[];
  outgoing: PendingFriendship[];
}
