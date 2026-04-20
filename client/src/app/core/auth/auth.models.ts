export interface MeResponse {
  id: string;
  email: string;
  username: string;
  displayName: string;
  avatarUrl: string | null;
  soundOnMessage: boolean;
  currentSessionId: string;
}

export interface RegisterRequest {
  email: string;
  username: string;
  displayName: string;
  password: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

export interface ProblemDetails {
  title?: string;
  status?: number;
  code?: string;
  detail?: string;
}
