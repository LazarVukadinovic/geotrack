import { Component, DestroyRef, ElementRef, effect, inject, OnInit, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, NavigationEnd, NavigationStart, Router, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs/operators';
import { BottomNavComponent } from '../../../shared/components/bottom-nav/bottom-nav.component';
import { SaveToCollectionSheetComponent } from '../../../shared/components/save-to-collection-sheet/save-to-collection-sheet.component';
import { DesktopSidebarComponent } from '../desktop-sidebar/desktop-sidebar.component';
import { AuthService } from '../../services/auth.service';
import { TokenStorageService } from '../../services/token-storage.service';
import { TokenRefreshService } from '../../services/token-refresh.service';
import { AnalyticsService } from '../../services/analytics.service';
import { LocationService } from '../../services/location.service';

// URL prefixes that require an authenticated session. When isLoggedIn() flips
// to false mid-session (e.g. an expired token triggers a 401 that the
// authErrorInterceptor catches and calls logout()), any user currently on one
// of these pages is redirected to /profile immediately — no manual refresh needed.
const AUTH_REQUIRED_PREFIXES = [
  '/register/interests',
  '/profile/edit',
  '/profile/collections',
  '/contributions',
  '/settings',
];

type DesktopLayoutMode = 'contained' | 'wide' | 'narrow' | 'full';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, BottomNavComponent, DesktopSidebarComponent, SaveToCollectionSheetComponent],
  templateUrl: './app-shell.component.html',
  styleUrl: './app-shell.component.scss',
})
export class AppShellComponent implements OnInit {
  private readonly router           = inject(Router);
  private readonly destroyRef       = inject(DestroyRef);
  private readonly authService      = inject(AuthService);
  private readonly tokenStorage     = inject(TokenStorageService);
  private readonly tokenRefreshSvc  = inject(TokenRefreshService);
  private readonly analytics        = inject(AnalyticsService);
  private readonly locationService  = inject(LocationService);
  protected readonly showBottomNav  = signal(true);
  protected readonly desktopLayout  = signal<DesktopLayoutMode>('contained');

  private readonly viewport = viewChild<ElementRef<HTMLElement>>('viewport');
  private currentTrigger: 'imperative' | 'popstate' | 'hashchange' = 'imperative';
  private readonly scrollHistory = new Map<string, number>();

  constructor() {
    // React to runtime auth invalidation (expired token → 401 → logout()).
    // Route guards only run on navigation; this effect handles the case where
    // the token expires while the user is already on a protected page.
    effect(() => {
      if (!this.authService.isLoggedIn()) {
        const url = this.router.url;
        if (AUTH_REQUIRED_PREFIXES.some(prefix => url.startsWith(prefix))) {
          this.router.navigate(['/profile']);
        }
      }
    });
  }

  ngOnInit(): void {
    this.locationService.tryBackgroundGpsRefresh({ force: true });

    this.router.events
      .pipe(
        filter((e): e is NavigationStart | NavigationEnd =>
          e instanceof NavigationStart || e instanceof NavigationEnd,
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((event) => {
        if (event instanceof NavigationStart) {
          this.analytics.endCurrentView('navigation');
          this.currentTrigger = event.navigationTrigger ?? 'imperative';
          const el = this.viewport()?.nativeElement;
          if (el) {
            this.scrollHistory.set(this.router.url, el.scrollTop);
          }
        } else {
          this.syncShellRouteState();
          // Fire a page_view for every navigation. AnalyticsService normalizes
          // the URL into a route template (`/places/:id`) and enriches with
          // destination + region context. Failures are swallowed inside the
          // service.
          const loc = this.locationService.location();
          this.analytics.track('page_view', {
            metadata: {
              region: loc?.region,
              source: loc?.source,
            },
          });
          // Proactively refresh when the access token is near expiry so that
          // navigation to any page is never followed immediately by a 401.
          // Only triggered when logged in and token is within the 2-minute buffer.
          if (this.authService.isLoggedIn() && this.tokenStorage.isAccessTokenNearExpiry()) {
            this.tokenRefreshSvc.refresh().subscribe({ error: () => {} });
          }
          const el = this.viewport()?.nativeElement;
          if (!el) return;
          if (this.currentTrigger === 'popstate') {
            const saved = this.scrollHistory.get(event.urlAfterRedirects) ?? 0;
            requestAnimationFrame(() => {
              el.scrollTop = saved;
            });
          } else {
            el.scrollTop = 0;
          }
        }
      });

    // Router tree / snapshots may not be final during the first tick; defer reads slightly.
    queueMicrotask(() => this.syncShellRouteState());
  }

  private syncShellRouteState(): void {
    const root = this.router.routerState?.root;
    if (!root) {
      this.showBottomNav.set(true);
      this.desktopLayout.set('contained');
      return;
    }

    let route: ActivatedRoute = root;
    while (route.firstChild) {
      route = route.firstChild;
    }

    const hideBottomNav = route.snapshot?.data?.['hideBottomNav'] === true;
    this.showBottomNav.set(!hideBottomNav);
    this.desktopLayout.set(this.toDesktopLayout(route.snapshot?.data?.['desktopLayout']));
  }

  private toDesktopLayout(value: unknown): DesktopLayoutMode {
    return value === 'wide' || value === 'narrow' || value === 'full' ? value : 'contained';
  }
}
