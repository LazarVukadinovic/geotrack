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
import { ExploreSectionComponent, type ExploreSectionItemEvent, type ExploreSectionSaveEvent } from '../../components/explore-section/explore-section.component';
import { ExploreService } from '../../services/explore.service';
import { EmptyStateComponent } from '../../../../shared/components/empty-state/empty-state.component';
import { AnalyticsService } from '../../../../core/services/analytics.service';
import { AppNotificationsService } from '../../../../core/services/app-notifications.service';
import { SearchPageComponent } from '../../../search/pages/search-page/search-page.component';
import type { ExploreSection } from '../../models/explore-extras.model';

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
    SearchPageComponent,
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
  private readonly location = inject(Location);
  private readonly elRef = inject(ElementRef<HTMLElement>);
  private readonly scrollState = inject(ExploreScrollStateService);
  private readonly analytics = inject(AnalyticsService);
  private readonly notifications = inject(AppNotificationsService);

  protected readonly isLoggedIn = computed(() => this.auth.isLoggedIn());
  protected readonly hasUnreadNotifications = computed(() => this.notifications.unreadCount() > 0);
  protected readonly loadError = this.explore.loadError;
  protected readonly loading = this.explore.loading;
  protected readonly loaded = this.explore.loaded;
  protected readonly promo = computed(() => this.explore.promo());

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

    void this.locationSvc.resolveLocationForProductFlow();
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
    if (source === 'approximate') return 'Using approximate location.';
    if (this.locationSvc.loading()) return 'Resolving location.';
    return 'Location unavailable.';
  });

  protected readonly searchQuery = signal('');
  protected readonly isDestinationMode = computed(() => this.destinationSvc.isDestinationMode());
  protected readonly searchOverlayOpen = signal(this.route.snapshot.data['searchOverlay'] === true);

  protected readonly sections = computed(() => {
    const q = this.searchQuery().trim().toLowerCase();
    const sections = this.explore.sections();

    if (!q) return sections;

    return sections
      .map(section => ({
        ...section,
        items: section.items.filter(item => this.matchesSearch(item, q)),
      }))
      .filter(section => section.items.length > 0);
  });

  protected openSearch(): void {
    void this.router.navigate(['/search'], { state: { searchFromExplore: true } });
  }

  protected closeSearch(): void {
    this.searchOverlayOpen.set(false);

    if (this.router.url.startsWith('/search') && history.state?.['searchFromExplore']) {
      this.location.back();
      return;
    }

    if (this.router.url.startsWith('/search')) {
      void this.router.navigate(['/explore'], { replaceUrl: true });
    }
  }

  protected useCurrentLocation(): void {
    this.searchQuery.set('');
    this.explore.useCurrentLocation();
    void this.router.navigate(['/explore'], { replaceUrl: true });
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
    this.trackExploreSearchCommit(event.item.entityType, event.item.id);

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

  private matchesSearch(item: { title?: string; subtitle?: string; description?: string; categoryLabel?: string; dateLabel?: string }, q: string): boolean {
    return [item.title, item.subtitle, item.description, item.categoryLabel, item.dateLabel]
      .join(' ')
      .toLowerCase()
      .includes(q);
  }

  private defaultRoute(entityType: string, id: string): string {
    if (entityType === 'event') return `/events/${id}`;
    if (entityType === 'stay') return `/stays/${id}`;
    if (entityType === 'destination') return `/explore?destinationId=${id}`;
    return `/places/${id}`;
  }

  private trackExploreSearchCommit(entityType: string, id: string): void {
    const query = this.searchQuery().trim();
    if (query.length < 2) return;

    const normalizedType: 'place' | 'event' = entityType === 'event' ? 'event' : 'place';
    const entityId = parseInt(id, 10);
    this.analytics.track('search_performed', {
      entityType: normalizedType,
      entityId: isNaN(entityId) ? undefined : entityId,
      metadata: {
        surface: 'explore',
        queryLength: query.length,
        localMatches: this.sections().reduce((sum, section) => sum + section.items.length, 0),
        committed: true,
      },
    });
  }
}