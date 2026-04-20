export const environment = {
  production: true,
  apiBase: '/api',
  hubBase: '/hub',
  // Must be kept in sync with server-side AttachmentsOptions (server/ChatApp.Api/appsettings.json).
  attachmentLimits: {
    imageBytes: 3_145_728,
    fileBytes: 20_971_520,
    maxPerMessage: 10,
  },
};
