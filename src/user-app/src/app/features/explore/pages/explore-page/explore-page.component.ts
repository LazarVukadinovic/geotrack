import { AfterViewInit, Component, computed, ElementRef, inject, OnDestroy, signal } from '@angular/core';
import { Location } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../../../core/services/auth.service';
import { DestinationService } from '../../../../core/services/destination.service';
import { LocationService } from '../../../../core/services/location.service';
import { ExploreScrollStateService } from '../../services/explore-scroll-state.service';
import { AppHeaderComponent } from '../../../../shared/components/app-header/app-header.component';
import { SearchBarComponent } from '../../../../shared/components/search-bar/search-bar.component';
import { WelcomeHeroComponent } from '../../components/welcome-hero/welcome-hero.component';
import { PromoCardComponent } from '../../components/promo-card/promo-card.component';
import { CuratedForYouComponent } from '../../components/curated-for-you/curated-for-you.component';
import { EventsSliderComponent } from '../../components/events-slider/events-slider.component';
import { PopularNearbySliderComponent } from '../../components/popular-nearby-slider/popular-nearby-slider.component';
import { TopStaysSliderComponent } from '../../components/top-stays-slider/top-stays-slider.component';
import { MustSeeSliderComponent } from '../../components/must-see-slider/must-see-slider.component';
import { LandmarksSliderComponent } from '../../components/landmarks-slider/landmarks-slider.component';
import { LocalFavoritesListComponent } from '../../components/local-favorites-list/local-favorites-list.component';
import { ExploreService } from '../../services/explore.service';
import { EmptyStateComponent } from '../../../../shared/components/empty-state/empty-state.component';
import { AnalyticsService } from '../../../../core/services/analytics.service';
import { AppNotificationsService } from '../../../../core/services/app-notifications.service';
import { SearchPageComponent } from '../../../search/pages/search-page/search-page.component';

@Component({
  selector: 'app-explore-page',
  standalone: true,
  imports: [
    AppHeaderComponent,
    SearchBarComponent,
    WelcomeHeroComponent,
    PromoCardComponent,
    CuratedForYouComponent,
    EventsSliderComponent,
    PopularNearbySliderComponent,
    TopStaysSliderComponent,
    MustSeeSliderComponent,
    LandmarksSliderComponent,
    LocalFavoritesListComponent,
    EmptyStateComponent,
    SearchPageComponent,
  ],
  templateUrl: './explore-page.component.html',
  styleUrl: './explore-page.component.scss',
})
export class ExplorePageComponent implements AfterViewInit, OnDestroy {
  private readonly explore     = inject(ExploreService);
  private readonly auth        = inject(AuthService);
  private readonly destinationSvc = inject(DestinationService);
  private readonly locationSvc = inject(LocationService);
  private readonly router      = inject(Router);
  private readonly route       = inject(ActivatedRoute);
  private readonly location    = inject(Location);
  private readonly elRef       = inject(ElementRef<HTMLElement>);
  private readonly scrollState = inject(ExploreScrollStateService);
  private readonly analytics   = inject(AnalyticsService);
  private readonly notifications = inject(AppNotificationsService);

  protected readonly isLoggedIn = computed(() => this.auth.isLoggedIn());
  protected readonly hasUnreadNotifications = computed(() => this.notifications.unreadCount() > 0);
  protected readonly loadError  = this.explore.loadError;

  constructor() {
    const routeDestinationId = Number(this.route.snapshot.queryParamMap.get('destinationId'));
    if (Number.isFinite(routeDestinationId) &&
      routeDestinationId > 0 &&
      (!this.destinationSvc.isDestinationMode() || this.destinationSvc.id() !== routeDestinationId)) {
      this.destinationSvc.setDestinationOverride({
        id: routeDestinationId,
        name: this.destinationSvc.name(),
      });
    }

    // For returning users, silently attempt to upgrade location via GPS in background.
    // This handles "allow once" and re-granted permissions without blocking.
    this.locationSvc.tryBackgroundGpsRefresh();
  }

  private readonly heroLocation = computed(() => {
    if (this.destinationSvc.isDestinationMode()) {
      return {
        city: this.destinationSvc.name() || 'Explore',
        country: '',
      };
    }

    const loc = this.locationSvc.location();
    const label = loc?.displayLabel ?? (this.locationSvc.loading() ? 'Explore' : this.locationSvc.displayLabel());
    const displayLabel = this.splitDisplayLabel(label);
    const city = loc?.city?.trim()
      || loc?.region?.trim()
      || loc?.country?.trim()
      || displayLabel.city;
    const country = loc?.country?.trim() ?? '';

    return {
      city,
      country: this.countryForHero(city, country || displayLabel.country),
    };
  });

  protected readonly heroCity    = computed(() => this.heroLocation().city);
  protected readonly heroCountry = computed(() => this.heroLocation().country);
  protected readonly contextLine = computed(() => {
    if (this.destinationSvc.isDestinationMode()) {
      const name = this.destinationSvc.name() || 'this destination';
      return `Exploring ${name}. Results are based on this destination.`;
    }

    const source = this.locationSvc.location()?.source;
    if (source === 'gps') return 'Using exact location.';
    if (source === 'manual') return 'Using selected location.';
    if (source === 'ip') return 'Using nearby location.';
    if (this.locationSvc.loading()) return 'Resolving location.';
    return 'Location unavailable.';
  });

  protected readonly searchQuery = signal('');
  protected readonly isDestinationMode = computed(() => this.destinationSvc.isDestinationMode());
  protected readonly searchOverlayOpen = signal(this.route.snapshot.data['searchOverlay'] === true);

  protected readonly promo        = computed(() => this.explore.promo());
  protected readonly curatedItems = computed(() => this.explore.curatedForYou());

  protected readonly eventsThisWeek = computed(() => {
    const q = this.searchQuery().trim().toLowerCase();
    if (!q) return this.explore.eventsThisWeek();
    return this.explore.eventsThisWeek().filter((ev) =>
      [ev.title, ev.subtitle, ev.dateLabel].join(' ').toLowerCase().includes(q),
    );
  });

  protected readonly popularNearby      = computed(() => this.explore.popularNearby());
  protected readonly topStays           = computed(() => this.explore.topStays());
  protected readonly mustSeeAttractions = computed(() => this.explore.mustSeeAttractions());
  protected readonly landmarkCategories = computed(() => this.explore.landmarkCategories());

  protected readonly filteredLocalFavorites = computed(() => {
    const q = this.searchQuery().trim().toLowerCase();
    if (!q) return this.explore.localFavorites();
    return this.explore.localFavorites().filter((item) =>
      [item.title, item.subtitle, item.description, item.location].join(' ').toLowerCase().includes(q),
    );
  });

  protected openSearch(): void {
    this.router.navigate(['/search'], { state: { searchFromExplore: true } });
  }

  protected closeSearch(): void {
    this.searchOverlayOpen.set(false);

    if (this.router.url.startsWith('/search') && history.state?.searchFromExplore) {
      this.location.back();
      return;
    }

    if (this.router.url.startsWith('/search')) {
      this.router.navigate(['/explore'], { replaceUrl: true });
    }
  }

  protected useCurrentLocation(): void {
    this.searchQuery.set('');
    this.explore.useCurrentLocation();
    this.router.navigate(['/explore'], { replaceUrl: true });
  }

  private splitDisplayLabel(label: string): { city: string; country: string } {
    const parts = label
      .split(',')
      .map(part => part.trim())
      .filter(Boolean);

    if (parts.length === 0) {
      return { city: 'Explore', country: '' };
    }

    return {
      city: parts[0],
      country: parts.slice(1).join(', '),
    };
  }

  private countryForHero(city: string, country: string): string {
    if (!country) return '';
    return city.trim().toLowerCase() === country.trim().toLowerCase() ? '' : country;
  }

  protected toggleNearbySaved(id: string): void {
    this.explore.toggleSaved(id, 'popularNearby');
  }

  protected toggleStaySaved(id: string): void {
    this.explore.toggleSaved(id, 'topStays');
  }

  protected toggleLocalSaved(id: string): void {
    this.explore.toggleSaved(id, 'localFavorites');
  }

  /** Navigate to Place Detail for non-hotel local favorites. */
  protected onLocalCard(id: string): void {
    const item = this.explore.localFavorites().find((i) => i.id === id);
    if (!item || item.category === 'hotel') return;
    this.scrollState.markClickedItem('local-favorites', id);
    this.trackExploreSearchCommit('place', id);
    this.router.navigate(['/places', id]);
  }

  /** Navigate to Place Detail for popular-nearby cards (all are places). */
  protected onNearbyCard(id: string): void {
    this.scrollState.markClickedItem('nearby', id);
    this.router.navigate(['/places', id]);
  }

  /** Navigate to Place Detail for must-see attraction cards. */
  protected onAttractionCard(id: string): void {
    this.scrollState.markClickedItem('must-see', id);
    this.router.navigate(['/places', id]);
  }

  /** Navigate to Hotel/Apartment Detail for top-stays cards. */
  protected onStayCard(id: string): void {
    this.scrollState.markClickedItem('stays', id);
    this.router.navigate(['/stays', id]);
  }

  protected onStaysMore(): void {
    this.router.navigate(['/hotels']);
  }

  protected onLocalMore(): void {
    this.router.navigate(['/food-and-music']);
  }

  protected onAttractionsMore(): void {
    this.router.navigate(['/culture-and-history']);
  }

  protected onNatureMore(): void {
    this.router.navigate(['/nature']);
  }

  protected onEventsMore(): void {
    this.router.navigate(['/events']);
  }

  protected onPromoCta(): void {
    this.router.navigate(['/profile']);
  }

  protected onNotifications(): void {
    this.router.navigate(['/notifications']);
  }

  protected toggleCuratedSaved(id: string): void {
    this.explore.toggleSaved(id, 'curatedForYou');
  }

  protected onCuratedCard(id: string): void {
    this.scrollState.markClickedItem('curated', id);
    this.router.navigate(['/places', id]);
  }

  protected onCuratedMore(): void {
    this.router.navigate(['/recommendations']);
  }

  protected onEvent(id: string): void {
    this.scrollState.markClickedItem('events', id);
    this.trackExploreSearchCommit('event', id);
    this.router.navigate(['/events', id]);
  }

  protected onLandmarkTile(categoryKey: string): void {
    this.router.navigate(['/local-places', categoryKey]);
  }

  protected onLandmarksMore(): void {
    this.router.navigate(['/local-places']);
  }

  // ─── Scroll state preservation ────────────────────────────────────────────

  ngAfterViewInit(): void {
    const state = this.scrollState.pop();
    if (!state) return;

    const host     = this.elRef.nativeElement as HTMLElement;
    const viewport = host.closest<HTMLElement>('.app-shell__viewport');

    if (viewport && state.scrollTop > 0) {
      requestAnimationFrame(() => {
        viewport.scrollTop = state.scrollTop;
      });
    }

    requestAnimationFrame(() => {
      requestAnimationFrame(() => {
        for (const [id, left] of Object.entries(state.sliders)) {
          const track = host.querySelector<HTMLElement>(`[data-slider-id="${id}"]`);
          if (track) track.scrollLeft = left;
        }

        if (state.clickedItem) {
          const { sliderId, itemId } = state.clickedItem;
          const track = host.querySelector<HTMLElement>(`[data-slider-id="${sliderId}"]`);
          const card  = track?.querySelector<HTMLElement>(`[data-item-id="${itemId}"]`);
          card?.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'instant' });
        }
      });
    });
  }

  ngOnDestroy(): void {
    const host     = this.elRef.nativeElement as HTMLElement;
    const viewport = host.closest<HTMLElement>('.app-shell__viewport');
    const scrollTop = viewport?.scrollTop ?? 0;

    const tracks = host.querySelectorAll<HTMLElement>('[data-slider-id]');
    const sliders: Record<string, number> = {};
    tracks.forEach((track) => {
      const id = track.dataset['sliderId'];
      if (id) sliders[id] = track.scrollLeft;
    });

    this.scrollState.save(scrollTop, sliders);
  }

  private trackExploreSearchCommit(entityType: 'place' | 'event', id: string): void {
    const query = this.searchQuery().trim();
    if (query.length < 2) return;

    const entityId = parseInt(id, 10);
    this.analytics.track('search_performed', {
      entityType,
      entityId: isNaN(entityId) ? undefined : entityId,
      metadata: {
        surface: 'explore',
        queryLength: query.length,
        localMatches: this.filteredLocalFavorites().length,
        eventMatches: this.eventsThisWeek().length,
        committed: true,
      },
    });
  }
}
