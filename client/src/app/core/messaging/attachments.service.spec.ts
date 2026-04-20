import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { AttachmentTooLargeError, AttachmentsService } from './attachments.service';

function makeFile(
  name: string,
  type: string,
  sizeBytes: number,
): File {
  const blob = new Blob([new Uint8Array(sizeBytes)], { type });
  return new File([blob], name, { type });
}

describe('AttachmentsService', () => {
  let svc: AttachmentsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    svc = TestBed.inject(AttachmentsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('pickKind returns "image" for image/* types and "file" otherwise', () => {
    expect(svc.pickKind(makeFile('x.png', 'image/png', 1))).toBe('image');
    expect(svc.pickKind(makeFile('x.pdf', 'application/pdf', 1))).toBe('file');
  });

  it('limitFor honours image vs. file limits', () => {
    expect(svc.limitFor(makeFile('x.png', 'image/png', 1))).toBe(
      environment.attachmentLimits.imageBytes,
    );
    expect(svc.limitFor(makeFile('x.bin', 'application/octet-stream', 1))).toBe(
      environment.attachmentLimits.fileBytes,
    );
  });

  it('upload throws AttachmentTooLargeError when file exceeds the limit', async () => {
    const huge = makeFile('big.png', 'image/png', environment.attachmentLimits.imageBytes + 1);
    await expect(svc.upload(huge)).rejects.toBeInstanceOf(AttachmentTooLargeError);
    http.expectNone(`${environment.apiBase}/attachments`);
  });

  it('upload POSTs multipart form with file, kind, and optional comment', async () => {
    const file = makeFile('a.png', 'image/png', 10);
    const p = svc.upload(file, 'caption');
    const req = http.expectOne(`${environment.apiBase}/attachments`);
    expect(req.request.method).toBe('POST');
    const body = req.request.body as FormData;
    expect(body.get('file')).toBeInstanceOf(File);
    expect(body.get('kind')).toBe('image');
    expect(body.get('comment')).toBe('caption');
    req.flush({
      id: 'att-1',
      kind: 'image',
      originalFilename: 'a.png',
      mime: 'image/png',
      sizeBytes: 10,
      comment: 'caption',
      thumbUrl: null,
      downloadUrl: '/files/a.png',
      createdAt: '2026-01-01T00:00:00Z',
    });
    const res = await p;
    expect(res.id).toBe('att-1');
  });

  it('upload omits comment when not provided', async () => {
    const file = makeFile('a.bin', 'application/octet-stream', 10);
    const p = svc.upload(file);
    const req = http.expectOne(`${environment.apiBase}/attachments`);
    const body = req.request.body as FormData;
    expect(body.get('comment')).toBeNull();
    req.flush({
      id: 'att-2',
      kind: 'file',
      originalFilename: 'a.bin',
      mime: 'application/octet-stream',
      sizeBytes: 10,
      comment: null,
      thumbUrl: null,
      downloadUrl: '/files/a.bin',
      createdAt: '2026-01-01T00:00:00Z',
    });
    await p;
  });
});
