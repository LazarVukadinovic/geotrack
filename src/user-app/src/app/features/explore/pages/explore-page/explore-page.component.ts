import { AfterViewInit, Component, computed, ElementRef, inject, OnDestroy, signal } from '@angular/core';
import { Router } from '@angular/router';
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
  private readonly elRef       = inject(ElementRef<HTMLElement>);
  private readonly scrollState = inject(ExploreScrollStateService);
  private readonly analytics   = inject(AnalyticsService);
  private searchAnalyticsTimeout?: ReturnType<typeof setTimeout>;

  protected readonly isLoggedIn = computed(() => this.auth.isLoggedIn());
  protected readonly loadError  = this.explore.loadError;

  constructor() {
    // For returning users, silently attempt to upgrade location via GPS in background.
    // This handles "allow once" and re-granted permissions without blocking.
    this.locationSvc.tryBackgroundGpsRefresh();
  }

  protected readonly heroCity    = computed(() => this.locationSvc.displayCity());
  protected readonly heroCountry = computed(() => this.locationSvc.displayCountry());
  protected readonly contextLine = computed(() => {
    const destinationName = this.destinationSvc.name();
    const heroCity = this.heroCity().trim().toLowerCase();
    const normalizedDestination = destinationName.trim().toLowerCase();
    const sourceLabel = this.locationSvc.sourceLabel();

    if (!destinationName) {
      return `Using ${sourceLabel}.`;
    }

    if (heroCity && heroCity === normalizedDestination) {
      return `Using ${sourceLabel}.`;
    }

    return `Using ${sourceLabel}. Nearby content from ${destinationName}.`;
  });

  protected readonly searchQuery = signal('');

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

  protected onSearchChange(value: string): void {
    this.searchQuery.set(value);
    clearTimeout(this.searchAnalyticsTimeout);
    const query = value.trim();
    if (query.length < 2) return;

    this.searchAnalyticsTimeout = setTimeout(() => {
      this.analytics.track('search_performed', {
        metadata: {
          surface: 'explore',
          queryLength: query.length,
          localMatches: this.filteredLocalFavorites().length,
          eventMatches: this.eventsThisWeek().length,
        },
      });
    }, 600);
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

  protected toggleCuratedSaved(id: string): void {
    this.explore.toggleSaved(id, 'curatedForYou');
  }

  protected onCuratedCard(id: string): void {
    this.scrollState.markClickedItem('curated', id);
    this.router.navigate(['/places', id]);
  }

  protected onCuratedMore(): void {
    this.router.navigate(['/culture-and-history']);
  }

  protected onEvent(id: string): void {
    this.scrollState.markClickedItem('events', id);
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
    clearTimeout(this.searchAnalyticsTimeout);

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
}
