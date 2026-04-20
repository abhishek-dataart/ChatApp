import { Routes } from '@angular/router';
import { anonGuard, authGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'app' },
  {
    path: 'login',
    canActivate: [anonGuard],
    loadComponent: () =>
      import('./features/auth/login/login.component').then((m) => m.LoginComponent),
  },
  {
    path: 'forgot-password',
    canActivate: [anonGuard],
    loadComponent: () =>
      import('./features/auth/forgot-password/forgot-password.component').then(
        (m) => m.ForgotPasswordComponent,
      ),
  },
  {
    path: 'reset-password',
    canActivate: [anonGuard],
    loadComponent: () =>
      import('./features/auth/reset-password/reset-password.component').then(
        (m) => m.ResetPasswordComponent,
      ),
  },
  {
    path: 'register',
    canActivate: [anonGuard],
    loadComponent: () =>
      import('./features/auth/register/register.component').then((m) => m.RegisterComponent),
  },
  {
    path: 'app',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/app-shell/app-shell.component').then((m) => m.AppShellComponent),
    children: [
      {
        path: 'profile',
        loadComponent: () =>
          import('./features/profile/profile.component').then((m) => m.ProfileComponent),
      },
      {
        path: 'sessions',
        loadComponent: () =>
          import('./features/sessions/sessions.component').then((m) => m.SessionsComponent),
      },
      {
        path: 'contacts',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/contacts/contacts.component').then((m) => m.ContactsComponent),
      },
      {
        path: 'dms/:chatId',
        loadComponent: () =>
          import('./features/dms/dms.component').then((m) => m.DmsComponent),
      },
      {
        path: 'rooms',
        canActivate: [authGuard],
        children: [
          { path: '', pathMatch: 'full', redirectTo: 'public' },
          {
            path: 'public',
            loadComponent: () =>
              import('./features/rooms/public-rooms/public-rooms.component').then(
                (m) => m.PublicRoomsComponent,
              ),
          },
          {
            path: 'private',
            loadComponent: () =>
              import('./features/rooms/private-rooms/private-rooms.component').then(
                (m) => m.PrivateRoomsComponent,
              ),
          },
        ],
      },
      {
        path: 'rooms/:id',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/rooms/room-detail/room-detail.component').then(
            (m) => m.RoomDetailComponent,
          ),
      },
      {
        path: 'rooms/:id/manage',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/manage-room/manage-room.component').then(
            (m) => m.ManageRoomComponent,
          ),
      },
    ],
  },
  { path: '**', redirectTo: 'app' },
];
