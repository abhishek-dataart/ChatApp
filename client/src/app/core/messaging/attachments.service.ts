import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { UploadAttachmentResponse } from './messaging.models';

export class AttachmentTooLargeError extends Error {
  constructor(
    readonly filename: string,
    readonly sizeBytes: number,
    readonly limitBytes: number,
  ) {
    super(`Attachment "${filename}" exceeds the ${limitBytes}-byte limit.`);
    this.name = 'AttachmentTooLargeError';
  }
}

@Injectable({ providedIn: 'root' })
export class AttachmentsService {
  private readonly http = inject(HttpClient);

  pickKind(file: File): 'image' | 'file' {
    return file.type.startsWith('image/') ? 'image' : 'file';
  }

  limitFor(file: File): number {
    return file.type.startsWith('image/')
      ? environment.attachmentLimits.imageBytes
      : environment.attachmentLimits.fileBytes;
  }

  async upload(file: File, comment?: string): Promise<UploadAttachmentResponse> {
    const limit = this.limitFor(file);
    if (file.size > limit) {
      throw new AttachmentTooLargeError(file.name, file.size, limit);
    }
    const formData = new FormData();
    formData.append('file', file);
    formData.append('kind', this.pickKind(file));
    if (comment) formData.append('comment', comment);
    return firstValueFrom(
      this.http.post<UploadAttachmentResponse>(`${environment.apiBase}/attachments`, formData),
    );
  }
}
