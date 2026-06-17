import { Injectable } from '@angular/core';
import { ActivatedRouteSnapshot, BaseRouteReuseStrategy } from '@angular/router';

/**
 * Route reuse strategy that, for a single explicitly-flagged navigation, forces Angular to
 * destroy and re-create the destination leaf component instead of reusing the existing
 * instance.
 *
 * Most admin detail pages read their target (id / region) from `route.snapshot` inside
 * `ngOnInit`. When the user navigates between two instances of the same route (for example
 * clicking one notification, then another, while already sitting on the first one's page),
 * Angular reuses the component, `ngOnInit` never runs again, and the page keeps showing the
 * previous target. Flagging the next navigation here guarantees the page reloads with the new
 * target regardless of how the individual component reads the route.
 *
 * The shell route (empty path) is never force-recreated, so the surrounding topbar/sidebar
 * stay mounted and only the page inside the router-outlet reloads.
 */
@Injectable({ providedIn: 'root' })
export class NotificationReuseStrategy extends BaseRouteReuseStrategy {
  private forceReload = false;

  /** Force the very next navigation to re-create the destination leaf component. */
  forceReloadOnNextNavigation(): void {
    this.forceReload = true;
  }

  /** Clear the one-shot flag once the flagged navigation has settled. */
  reset(): void {
    this.forceReload = false;
  }

  override shouldReuseRoute(future: ActivatedRouteSnapshot, curr: ActivatedRouteSnapshot): boolean {
    const sameConfig = future.routeConfig === curr.routeConfig;

    // Only override reuse for the matching leaf route; keep the shell (empty path) mounted.
    if (this.forceReload && sameConfig && future.routeConfig?.path !== '') {
      return false;
    }

    return super.shouldReuseRoute(future, curr);
  }
}
