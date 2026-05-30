import type { Route } from '@angular/router';
import { routes } from './app.routes';

function appShellChild(path: string): Route | undefined {
  return routes.find((route) => route.path === '')?.children?.find((route) => route.path === path);
}

describe('app routes', () => {
  it('routes the settings notifications page to the notification preferences component', () => {
    const route = appShellChild('settings/notifications');

    expect(route).toBeTruthy();
    expect(route).toMatchObject({
      path: 'settings/notifications',
    });
    expect(route?.loadComponent).toBeTruthy();
    expect(route?.redirectTo).toBeUndefined();
    expect(route?.canActivate).toBeTruthy();
  });
});
