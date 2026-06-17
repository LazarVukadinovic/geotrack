import { ActivatedRouteSnapshot, Route } from '@angular/router';

import { NotificationReuseStrategy } from './notification-reuse-strategy';

describe('NotificationReuseStrategy', () => {
  let strategy: NotificationReuseStrategy;

  const snapshotFor = (config: Route): ActivatedRouteSnapshot =>
    ({ routeConfig: config } as ActivatedRouteSnapshot);

  beforeEach(() => {
    strategy = new NotificationReuseStrategy();
  });

  it('reuses matching routes by default', () => {
    const leaf: Route = { path: 'territory/regions/:id' };

    expect(strategy.shouldReuseRoute(snapshotFor(leaf), snapshotFor(leaf))).toBe(true);
  });

  it('does not reuse the matching leaf route for a flagged navigation', () => {
    const leaf: Route = { path: 'territory/regions/:id' };
    strategy.forceReloadOnNextNavigation();

    expect(strategy.shouldReuseRoute(snapshotFor(leaf), snapshotFor(leaf))).toBe(false);
  });

  it('keeps the shell route mounted even when a reload is flagged', () => {
    const shell: Route = { path: '' };
    strategy.forceReloadOnNextNavigation();

    expect(strategy.shouldReuseRoute(snapshotFor(shell), snapshotFor(shell))).toBe(true);
  });

  it('reuses matching routes again after the flag is reset', () => {
    const leaf: Route = { path: 'territory/regions/:id' };
    strategy.forceReloadOnNextNavigation();
    strategy.reset();

    expect(strategy.shouldReuseRoute(snapshotFor(leaf), snapshotFor(leaf))).toBe(true);
  });

  it('leaves the default behavior unchanged for routes with different configs', () => {
    const current: Route = { path: 'territory/regions/:id' };
    const next: Route = { path: 'territory/users' };
    strategy.forceReloadOnNextNavigation();

    // Different configs are never reused regardless of the flag (default Angular behavior).
    expect(strategy.shouldReuseRoute(snapshotFor(next), snapshotFor(current))).toBe(false);
  });
});
