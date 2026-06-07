import { HttpInterceptorFn } from '@angular/common/http';
import {
  isApiPath,
  isConfiguredApiUrl,
  isMediaPath,
  resolveApiUrl,
  resolveMediaUrl,
} from './api-url';

export const apiUrlInterceptor: HttpInterceptorFn = (req, next) => {
  let url = req.url;
  let includeCredentials = false;

  if (isApiPath(req.url)) {
    url = resolveApiUrl(req.url);
    includeCredentials = true;
  } else if (isConfiguredApiUrl(req.url)) {
    includeCredentials = true;
  } else if (isMediaPath(req.url)) {
    url = resolveMediaUrl(req.url) ?? req.url;
  }

  if (url === req.url && (!includeCredentials || req.withCredentials)) {
    return next(req);
  }

  return next(req.clone({
    url,
    withCredentials: includeCredentials ? true : req.withCredentials,
  }));
};
