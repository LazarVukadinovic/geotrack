import type { Route } from '@angular/router';
import { routes } from './app.routes';

function appShellChild(path: string): Route | undefined {
  return routes.find((route) => route.path === '')?.children?.find((route) => route.path === path);
}

describe('app routes', () => {
  it('redirects the unavailable settings notifications page back to settings', () => {
    const route = appShellChild('settings/notifications');

    expect(route).toBeTruthy();
    expect(route).toMatchObject({
      path: 'settings/notifications',
      pathMatch: 'full',
      redirectTo: 'settings',
    });
    expect(route?.loadComponent).toBeUndefined();
    expect(route?.canActivate).toBeUndefined();
  });
});
