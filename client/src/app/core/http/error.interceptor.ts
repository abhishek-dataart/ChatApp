import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { ToastService } from '../notifications/toast.service';

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const toast = inject(ToastService);
  const router = inject(Router);

  return next(req).pipe(
    catchError((err) => {
      if (err.status === 429) {
        toast.show({ severity: 'warn', message: 'Too many requests — slow down a bit.' });
      } else if (err.status === 403 && err.error?.error === 'invalid_csrf_token') {
        toast.show({
          severity: 'error',
          message: 'Session security error — please refresh the page.',
        });
        router.navigateByUrl('/login');
      }
      return throwError(() => err);
    }),
  );
};
