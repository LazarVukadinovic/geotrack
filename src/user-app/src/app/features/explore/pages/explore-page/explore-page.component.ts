import { AfterViewInit, Component, computed, ElementRef, inject, OnDestroy, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../../../core/services/auth.service';
import { DestinationService } from '../../../../core/services/destination.service';
import { LocationService } from '../../../../core/services/location.service';
import { EXPLORE_IMAGE_FALLBACK } from '../../../../core/constants/explore-media.constants';
import { ExploreScrollStateService } from '../../services/explore-scroll-state.service';
import { AppHeaderComponent } from '../../../../shared/components/app-header/app-header.component';
import { SearchBarComponent } from '../../../../shared/components/search-bar/search-bar.component';
import { WelcomeHeroComponent } from '../../components/welcome-hero/welcome-hero.component';
import { PromoCardComponent } from '../../components/promo-card/promo-card.component';
import { ExploreSectionComponent, type ExploreSectionItemEvent, type ExploreSectionSaveEvent } from '../../components/explore-section/explore-section.component';
import { ExploreService, type ExploreSearchResultItem } from '../../services/explore.service';
import { EmptyStateComponent } from '../../../../shared/components/empty-state/empty-state.component';
import { AnalyticsService } from '../../../../core/services/analytics.service';
import { AppNotificationsService } from '../../../../core/services/app-notifications.service';
import type { ExploreSection } from '../../models/explore-extras.model';

const MIN_EXPLORE_SEARCH_LENGTH = 2;
const EXPLORE_SEARCH_TAKE = 20;
const EXPLORE_SEARCH_MAX_DISTANCE_KM = 15;
const EXPLORE_CONTENT_MAX_DISTANCE_KM = 75;
const EXPLORE_SEARCH_DEBOUNCE_MS = 180;

@Component({
  selector: 'app-explore-page',
  standalone: true,
  imports: [
    AppHeaderComponent,
    SearchBarComponent,
    WelcomeHeroComponent,
    PromoCardComponent,
    ExploreSectionComponent,
    EmptyStateComponent,
  ],
  templateUrl: './explore-page.component.html',
  styleUrl: './explore-page.component.scss',
})
export class ExplorePageComponent implements AfterViewInit, OnDestroy {
  private readonly explore = inject(ExploreService);
  private readonly auth = inject(AuthService);
  private readonly destinationSvc = inject(DestinationService);
  private readonly locationSvc = inject(LocationService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly elRef = inject(ElementRef<HTMLElement>);
  private readonly scrollState = inject(ExploreScrollStateService);
  private readonly analytics = inject(AnalyticsService);
  private readonly notifications = inject(AppNotificationsService);

  protected readonly imageFallback = EXPLORE_IMAGE_FALLBACK;
  protected readonly isLoggedIn = computed(() => this.auth.isLoggedIn());
  protected readonly hasUnreadNotifications = computed(() => this.notifications.unreadCount() > 0);
  protected readonly loadError = this.explore.loadError;
  protected readonly loading = this.explore.loading;
  protected readonly loaded = this.explore.loaded;
  protected readonly promo = computed(() => this.explore.promo());

  protected readonly searchQuery = signal('');
  protected readonly searchResults = signal<ExploreSearchResultItem[]>([]);
  protected readonly searchLoading = signal(false);
  protected readonly searchError = signal<string | null>(null);
  protected readonly searchHasMore = signal(false);
  protected readonly exactLocationLoading = signal(false);
  protected readonly exactLocationError = signal<string | null>(null);
  protected readonly isDestinationMode = computed(() => this.destinationSvc.isDestinationMode());
  protected readonly sections = computed(() => this.explore.sections());
  protected readonly selectedDestination = computed(() => this.explore.selectedDestination());

  protected readonly canUseExploreSearch = computed(() => {
    if (this.destinationSvc.isDestinationMode()) {
      return true;
    }

    const selectedDestination = this.explore.selectedDestination();
    const currentLocation = this.locationSvc.location();

    if (!selectedDestination || !currentLocation) {
      return false;
    }

    const distanceKm = this.haversineKm(
      currentLocation.latitude,
      currentLocation.longitude,
      selectedDestination.latitude,
      selectedDestination.longitude,
    );

    return distanceKm <= EXPLORE_SEARCH_MAX_DISTANCE_KM;
  });

  protected readonly noNearbyDestination = computed(() => {
    if (this.destinationSvc.isDestinationMode()) {
      return false;
    }

    if (this.loading() || !this.loaded() || this.loadError()) {
      return false;
    }

    const selectedDestination = this.explore.selectedDestination();
    const currentLocation = this.locationSvc.location();

    if (!selectedDestination) {
      return true;
    }

    if (!currentLocation) {
      return false;
    }

    const distanceKm = this.haversineKm(
      currentLocation.latitude,
      currentLocation.longitude,
      selectedDestination.latitude,
      selectedDestination.longitude,
    );

    return distanceKm > EXPLORE_CONTENT_MAX_DISTANCE_KM;
  });

  protected readonly noNearbyLocationLabel = computed(() => {
    const loc = this.locationSvc.location();
    if (loc?.displayLabel?.trim()) return loc.displayLabel.trim();
    if (loc?.city?.trim()) return loc.city.trim();
    if (loc?.region?.trim()) return loc.region.trim();
    if (loc?.country?.trim()) return loc.country.trim();
    return 'your current area';
  });

  protected readonly showSearchPanel = computed(() =>
    this.canUseExploreSearch() && this.searchQuery().trim().length >= MIN_EXPLORE_SEARCH_LENGTH,
  );

  protected readonly topSearchResult = computed(() => this.searchResults()[0] ?? null);

  protected readonly placeSearchResults = computed(() => {
    const top = this.topSearchResult();
    return this.searchResults().filter(item =>
      item !== top && item.entityType === 'place',
    );
  });

  protected readonly areaSearchResults = computed(() => {
    const top = this.topSearchResult();
    return this.searchResults().filter(item =>
      item !== top && item.entityType === 'location',
    );
  });

  private searchTimeout?: ReturnType<typeof setTimeout>;
  private searchNextSkip = 0;
  private searchRequestVersion = 0;

  constructor() {
    const routeDestinationId = Number(this.route.snapshot.queryParamMap.get('destinationId'));
    if (
      Number.isFinite(routeDestinationId) &&
      routeDestinationId > 0 &&
      (!this.destinationSvc.isDestinationMode() || this.destinationSvc.id() !== routeDestinationId)
    ) {
      this.destinationSvc.setDestinationOverride({
        id: routeDestinationId,
        name: this.destinationSvc.name(),
      });
    }

    // Explore uses approximate IP location by default.
    // Browser GPS permission is requested only when the user clicks "Use exact location".
    void this.locationSvc.ensureIpFallback();
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

  protected readonly heroCity = computed(() => this.heroLocation().city);
  protected readonly heroCountry = computed(() => this.heroLocation().country);
  protected readonly contextLine = computed(() => {
    if (this.destinationSvc.isDestinationMode()) {
      const name = this.destinationSvc.name() || 'this destination';
      return `Exploring ${name}. Results are based on this destination.`;
    }

    const source = this.locationSvc.location()?.source;
    if (source === 'gps') return 'Using exact location.';
    if (source === 'manual') return 'Using selected location.';
    if (source === 'approximate') return 'Using approximate location from IP.';
    if (this.locationSvc.loading()) return 'Resolving approximate location from IP.';
    return 'Location unavailable.';
  });

  protected readonly showLocationUpgrade = computed(() =>
    !this.destinationSvc.isDestinationMode() &&
    this.locationSvc.location()?.source !== 'gps',
  );

  protected readonly useLocationModeActionLabel = computed(() =>
    this.locationSvc.location()?.source === 'gps' ? 'Use location' : 'Use IP location',
  );

  protected readonly displayedDestinationActionLabel = computed(() => {
    const name = this.selectedDestination()?.name?.trim();
    return name ? `Explore ${name}` : 'Explore other destinations';
  });

  protected onExploreSearchChange(value: string): void {
    if (!this.canUseExploreSearch()) {
      this.searchQuery.set('');
      this.resetSearchResults();
      return;
    }

    this.searchQuery.set(value);

    if (this.searchTimeout) {
      clearTimeout(this.searchTimeout);
    }

    const query = value.trim();
    if (query.length < MIN_EXPLORE_SEARCH_LENGTH) {
      this.resetSearchResults();
      return;
    }

    this.searchTimeout = setTimeout(() => {
      this.runExploreSearch(query, 0, false);
    }, EXPLORE_SEARCH_DEBOUNCE_MS);
  }

  protected onExploreSearchSubmit(value: string): void {
    if (!this.canUseExploreSearch()) {
      this.searchQuery.set('');
      this.resetSearchResults();
      return;
    }

    const query = value.trim();
    if (query.length < MIN_EXPLORE_SEARCH_LENGTH) return;

    if (this.searchTimeout) {
      clearTimeout(this.searchTimeout);
    }

    this.runExploreSearch(query, 0, false);
  }

  protected onExploreSearchCleared(): void {
    if (this.searchTimeout) {
      clearTimeout(this.searchTimeout);
    }

    this.searchQuery.set('');
    this.resetSearchResults();
  }

  protected loadMoreExploreSearch(): void {
    if (!this.canUseExploreSearch()) {
      this.searchQuery.set('');
      this.resetSearchResults();
      return;
    }

    const query = this.searchQuery().trim();
    if (query.length < MIN_EXPLORE_SEARCH_LENGTH || this.searchLoading()) return;

    this.runExploreSearch(query, this.searchNextSkip, true);
  }

  protected openExploreSearchResult(item: ExploreSearchResultItem): void {
    this.trackExploreSearchCommit(item.entityType, String(item.id), this.searchResults().length);

    if (item.entityType === 'location' && item.latitude != null && item.longitude != null) {
      void this.router.navigate(['/map'], {
        state: {
          mapFocus: {
            id: item.id,
            entityType: 'location',
            title: item.title,
            latitude: item.latitude,
            longitude: item.longitude,
            imageUrl: item.image,
          },
        },
      });
      return;
    }

    void this.router.navigateByUrl(item.detailRoute);
  }

  protected useCurrentLocation(): void {
    this.searchQuery.set('');
    this.resetSearchResults();
    this.exactLocationError.set(null);

    this.destinationSvc.useLocationMode();
    void this.locationSvc.ensureIpFallback().then((loc) => {
      if (loc) {
        void this.explore.loadForLocation(loc.latitude, loc.longitude);
      }
    });

    void this.router.navigate(['/explore'], { replaceUrl: true });
  }

  protected useExactLocation(): void {
    if (this.exactLocationLoading()) return;

    this.exactLocationLoading.set(true);
    this.exactLocationError.set(null);

    this.locationSvc.requestGps()
      .then(({ latitude, longitude }) => {
        this.destinationSvc.useLocationMode();
        return this.explore.loadForLocation(latitude, longitude);
      })
      .catch(() => {
        this.exactLocationError.set('Exact location is unavailable. GeoTrack will keep using approximate IP location.');
      })
      .finally(() => {
        this.exactLocationLoading.set(false);
      });
  }

  protected openDisplayedDestinationOrDiscovery(): void {
    const destination = this.selectedDestination();

    if (!destination?.id) {
      this.openDestinationDiscovery();
      return;
    }

    this.searchQuery.set('');
    this.resetSearchResults();
    this.destinationSvc.setDestinationOverride({
      id: destination.id,
      name: destination.name,
      latitude: destination.latitude,
      longitude: destination.longitude,
    });
    void this.router.navigate(['/explore'], {
      queryParams: { destinationId: destination.id },
    });
  }

  protected openDestinationDiscovery(): void {
    this.searchQuery.set('');
    this.resetSearchResults();
    void this.router.navigate(['/destinations']);
  }

  protected openMap(): void {
    this.searchQuery.set('');
    this.resetSearchResults();

    const destination = this.selectedDestination();
    if (destination?.name && typeof destination.latitude === 'number' && typeof destination.longitude === 'number') {
      void this.router.navigate(['/map'], {
        state: {
          name: destination.name,
          lat: destination.latitude,
          lon: destination.longitude,
          fromExplore: true,
        },
      });
      return;
    }

    void this.router.navigate(['/map']);
  }

  protected onPromoCta(): void {
    void this.router.navigate(['/profile']);
  }

  protected onNotifications(): void {
    void this.router.navigate(['/notifications']);
  }

  protected onSectionSave(event: ExploreSectionSaveEvent): void {
    this.explore.toggleSaved(event.item, event.section.key);
  }

  protected onSectionItem(event: ExploreSectionItemEvent): void {
    const sliderId = this.sliderId(event.section);
    this.scrollState.markClickedItem(sliderId, event.item.id);

    const route = event.item.detailRoute?.trim() || this.defaultRoute(event.item.entityType, event.item.id);
    void this.router.navigateByUrl(route);
  }

  protected onSectionMore(section: ExploreSection): void {
    if (!section.moreRoute) return;
    void this.router.navigateByUrl(section.moreRoute);
  }

  protected sliderId(section: ExploreSection): string {
    return `explore-section-${section.key}`;
  }

  ngAfterViewInit(): void {
    const state = this.scrollState.pop();
    if (!state) return;

    const host = this.elRef.nativeElement as HTMLElement;
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
          const card = track?.querySelector<HTMLElement>(`[data-item-id="${itemId}"]`);
          card?.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'instant' });
        }
      });
    });
  }

  ngOnDestroy(): void {
    if (this.searchTimeout) {
      clearTimeout(this.searchTimeout);
    }

    const host = this.elRef.nativeElement as HTMLElement;
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

  private currentExploreDestinationId(): number | null {
    return this.explore.selectedDestination()?.id
      ?? this.destinationSvc.id()
      ?? null;
  }

  private runExploreSearch(query: string, skip: number, append: boolean): void {
    if (!this.canUseExploreSearch()) {
      this.searchQuery.set('');
      this.resetSearchResults();
      return;
    }

    const destinationId = this.currentExploreDestinationId();

    if (destinationId == null || destinationId <= 0) {
      this.searchError.set('Destination is still loading. Try again in a moment.');
      this.searchLoading.set(false);
      return;
    }

    const requestVersion = ++this.searchRequestVersion;
    this.searchLoading.set(true);
    this.searchError.set(null);

    this.explore.searchCurrentDestination(query, destinationId, skip, EXPLORE_SEARCH_TAKE)
      .subscribe({
        next: (response) => {
          if (requestVersion !== this.searchRequestVersion) return;

          this.searchResults.set(
            append
              ? [...this.searchResults(), ...response.items]
              : response.items,
          );

          this.searchNextSkip = response.nextSkip;
          this.searchHasMore.set(response.hasMore);
          this.searchLoading.set(false);
        },
        error: () => {
          if (requestVersion !== this.searchRequestVersion) return;

          if (!append) {
            this.searchResults.set([]);
          }

          this.searchHasMore.set(false);
          this.searchError.set('Search failed. Try again.');
          this.searchLoading.set(false);
        },
      });
  }

  private resetSearchResults(): void {
    this.searchResults.set([]);
    this.searchError.set(null);
    this.searchHasMore.set(false);
    this.searchNextSkip = 0;
  }

  private haversineKm(lat1: number, lon1: number, lat2: number, lon2: number): number {
    const earthRadiusKm = 6371;
    const toRad = (value: number) => (value * Math.PI) / 180;

    const deltaLat = toRad(lat2 - lat1);
    const deltaLon = toRad(lon2 - lon1);
    const lat1Rad = toRad(lat1);
    const lat2Rad = toRad(lat2);

    const a =
      Math.sin(deltaLat / 2) ** 2 +
      Math.cos(lat1Rad) *
        Math.cos(lat2Rad) *
        Math.sin(deltaLon / 2) ** 2;

    return earthRadiusKm * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
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

  private defaultRoute(entityType: string, id: string): string {
    if (entityType === 'event') return `/events/${id}`;
    if (entityType === 'stay') return `/stays/${id}`;
    if (entityType === 'destination') return `/explore?destinationId=${id}`;
    return `/places/${id}`;
  }

  private trackExploreSearchCommit(entityType: string, id: string, resultCount: number): void {
    const query = this.searchQuery().trim();
    if (query.length < MIN_EXPLORE_SEARCH_LENGTH) return;

    const normalizedType: 'place' | 'event' = entityType === 'event' ? 'event' : 'place';
    const entityId = parseInt(id, 10);
    this.analytics.track('search_performed', {
      entityType: normalizedType,
      entityId: isNaN(entityId) ? undefined : entityId,
      metadata: {
        surface: 'explore-scoped',
        queryLength: query.length,
        resultCount,
        committed: true,
      },
    });
  }
}
