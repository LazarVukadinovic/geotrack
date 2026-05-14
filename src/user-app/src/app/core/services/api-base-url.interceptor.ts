import { HttpInterceptorFn } from '@angular/common/http';
import { apiUrl, isConfiguredApiRequest } from './api-url';

export const apiBaseUrlInterceptor: HttpInterceptorFn = (req, next) => {
  if (!isConfiguredApiRequest(req.url)) {
    return next(req);
  }

  return next(req.clone({
    url: apiUrl(req.url),
    withCredentials: true,
  }));
};
