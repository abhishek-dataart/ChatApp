export interface AttachmentSummary {
  id: string;
  kind: 'image' | 'file';
  originalFilename: string;
  mime: string;
  sizeBytes: number;
  comment: string | null;
  thumbUrl: string | null;
  downloadUrl: string;
  createdAt: string;
}

export interface UploadAttachmentResponse {
  id: string;
  kind: string;
  originalFilename: string;
  mime: string;
  sizeBytes: number;
  comment: string | null;
  thumbUrl: string | null;
  downloadUrl: string;
  createdAt: string;
}

export interface PendingAttachment {
  localId: string;
  file: File;
  previewUrl: string | null;
  status: 'uploading' | 'ready' | 'failed';
  uploadId?: string;
  comment: string;
}

export interface MessageResponse {
  id: string;
  scope: 'personal' | 'room';
  personalChatId: string | null;
  roomId: string | null;
  authorId: string;
  authorUsername: string;
  authorDisplayName: string;
  authorAvatarUrl: string | null;
  body: string;
  replyToId: string | null;
  replyToBody: string | null;
  replyToAuthorDisplayName: string | null;
  createdAt: string;
  editedAt: string | null;
  attachments: AttachmentSummary[];
}

export interface SendMessageRequest {
  body: string;
  replyToId?: string | null;
  attachmentIds?: string[];
}

export interface EditMessageRequest {
  body: string;
}

export interface MessageDeletedPayload {
  id: string;
  scope: 'personal' | 'room';
  personalChatId: string | null;
  roomId: string | null;
}

export interface MessageCursor {
  createdAt: string;
  id: string;
}

export interface UnreadChangedPayload {
  scope: 'personal' | 'room';
  scopeId: string;
  unreadCount: number;
}

export interface UnreadResponse {
  scope: 'personal' | 'room';
  scopeId: string;
  unreadCount: number;
}
