import { RoomSummary, UserSummary } from './rooms.models';

export interface InvitationEntry {
  invitationId: string;
  room: RoomSummary;
  inviter: UserSummary;
  note: string | null;
  createdAt: string;
}

export interface OutgoingInvitationEntry {
  invitationId: string;
  invitee: UserSummary;
  inviter: UserSummary;
  note: string | null;
  createdAt: string;
}

export interface IncomingInvitationsResponse {
  incoming: InvitationEntry[];
}

export interface RoomInvitationsResponse {
  invitations: OutgoingInvitationEntry[];
}

export interface SendInvitationRequest {
  username: string;
  note?: string;
}
