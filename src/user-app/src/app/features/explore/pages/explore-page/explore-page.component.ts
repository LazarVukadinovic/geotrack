import { AfterViewInit, Component, computed, ElementRef, inject, OnDestroy, signal, viewChild } from '@angular/core';
import { Router } from '@angular/router';
import type { ExploreCategoryFilter } from '../../../../core/models/explore-item.model';
import { ExploreScrollStateService } from '../../services/explore-scroll-state.service';
import { EXPLORE_CATEGORY_FILTERS } from '../../../../core/constants/explore-ui.constants';
import { AppHeaderComponent } from '../../../../shared/components/app-header/app-header.component';
import { SearchBarComponent } from '../../../../shared/components/search-bar/search-bar.component';
import { FilterPillsComponent } from '../../../../shared/components/filter-pills/filter-pills.component';
import { WelcomeHeroComponent } from '../../components/welcome-hero/welcome-hero.component';
import { PromoCardComponent } from '../../components/promo-card/promo-card.component';
import { EventsSliderComponent } from '../../components/events-slider/events-slider.component';
import { PopularNearbySliderComponent } from '../../components/popular-nearby-slider/popular-nearby-slider.component';
import { TopStaysSliderComponent } from '../../components/top-stays-slider/top-stays-slider.component';
import { MustSeeSliderComponent } from '../../components/must-see-slider/must-see-slider.component';
import { LandmarksSliderComponent } from '../../components/landmarks-slider/landmarks-slider.component';
import { LocalFavoritesListComponent } from '../../components/local-favorites-list/local-favorites-list.component';
import { ExploreService } from '../../services/explore.service';
import { itemMatchesExploreFilters } from '../../utils/explore-filter';
import { NearMePopupComponent, type NearMeSettings } from '../../components/near-me-popup/near-me-popup.component';

@Component({
  selector: 'app-explore-page',
  standalone: true,
  imports: [
    AppHeaderComponent,
    SearchBarComponent,
    FilterPillsComponent,
    WelcomeHeroComponent,
    PromoCardComponent,
    EventsSliderComponent,
    PopularNearbySliderComponent,
    TopStaysSliderComponent,
    MustSeeSliderComponent,
    LandmarksSliderComponent,
    LocalFavoritesListComponent,
    NearMePopupComponent,
  ],
  templateUrl: './explore-page.component.html',
  styleUrl: './explore-page.component.scss',
})
export class ExplorePageComponent implements AfterViewInit, OnDestroy {
  private readonly explore      = inject(ExploreService);
  private readonly router       = inject(Router);
  private readonly elRef        = inject(ElementRef<HTMLElement>);
  private readonly scrollState  = inject(ExploreScrollStateService);
  private readonly searchBar    = viewChild(SearchBarComponent);

  /** Mock hero location — later replace with user / geo service. */
  protected readonly heroCity = signal('Dubrovnik');
  protected readonly heroCountry = signal('Croatia');

  protected readonly categoryOptions = EXPLORE_CATEGORY_FILTERS;
  protected readonly searchQuery = signal('');
  protected readonly activeCategory = signal<ExploreCategoryFilter>('all');

  protected readonly promo = computed(() => this.explore.promo());

  protected readonly eventsThisWeek = computed(() => {
    const q = this.searchQuery().trim().toLowerCase();
    if (!q) return this.explore.eventsThisWeek();
    return this.explore.eventsThisWeek().filter((ev) =>
      [ev.title, ev.subtitle, ev.dateLabel].join(' ').toLowerCase().includes(q),
    );
  });

  protected readonly popularNearby = computed(() => this.explore.popularNearby());
  protected readonly topStays = computed(() => this.explore.topStays());
  protected readonly mustSeeAttractions = computed(() => this.explore.mustSeeAttractions());
  protected readonly landmarkCategories = computed(() => this.explore.landmarkCategories());

  protected readonly filteredLocalFavorites = computed(() => {
    const q = this.searchQuery();
    const cat = this.activeCategory();
    return this.explore.localFavorites().filter((item) => itemMatchesExploreFilters(item, q, cat));
  });

  protected onSearchChange(value: string): void {
    this.searchQuery.set(value);
  }

  protected onCategoryChange(id: ExploreCategoryFilter): void {
    this.activeCategory.set(id);
  }

  protected onHeaderSearch(): void {
    this.searchBar()?.focusInput();
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

  protected onPromoCta(): void {
    /* future: router.navigate(['/auth/register']) */
  }

  /**
   * Events are time-bound occurrences, not place entities.
   * They do not navigate to Place Detail.
   * future: router.navigate(['/events', id]) when event detail is implemented.
   */
  protected onEvent(_id: string): void {}

  /**
   * Landmark tiles are category browse entries (Beaches, Museums, Parks, Markets).
   * They are not individual place entries and do not navigate to Place Detail.
   * future: router.navigate(['/explore/category', id]) for category browsing.
   */
  protected onLandmarkTile(_id: string): void {}

  // ─── Scroll state preservation ────────────────────────────────────────────

  ngAfterViewInit(): void {
    const state = this.scrollState.pop();
    if (!state) return;

    const host = this.elRef.nativeElement as HTMLElement;
    const viewport = host.closest<HTMLElement>('.app-shell__viewport');

    // Restore vertical scroll immediately (first rAF — pre-paint).
    if (viewport && state.scrollTop > 0) {
      requestAnimationFrame(() => {
        viewport.scrollTop = state.scrollTop;
      });
    }

    // Slider effects (imgSrcById) fire asynchronously after view init and can
    // reset DOM state. We wait for two animation frames so those effects have
    // fully settled before we write scrollLeft or invoke scrollIntoView.
    requestAnimationFrame(() => {
      requestAnimationFrame(() => {
        // 1. Restore all slider scrollLeft values from the saved snapshot.
        for (const [id, left] of Object.entries(state.sliders)) {
          const track = host.querySelector<HTMLElement>(`[data-slider-id="${id}"]`);
          if (track) track.scrollLeft = left;
        }

        // 2. If a specific card was clicked, use it as the authoritative anchor.
        //    scrollIntoView with nearest/center keeps it visible and overrides
        //    any remaining imprecision from the raw scrollLeft restore above.
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

  // ─── Near me popup state ───────────────────────────────────────────────────

  protected readonly nearMePopupOpen   = signal(false);
  protected readonly nearMeRadius      = signal(2.5);
  protected readonly nearMeEnabled     = signal(true);
  protected readonly nearMePopupTop  = signal('0px');
  protected readonly nearMePopupLeft = signal('0px');

  protected openNearMePopup(event: MouseEvent): void {
    if (this.nearMePopupOpen()) {
      this.nearMePopupOpen.set(false);
      return;
    }
    const btn  = event.currentTarget as HTMLElement;
    const rect = btn.getBoundingClientRect();
    const gap  = 12;
    this.nearMePopupTop.set(`${rect.bottom + gap}px`);
    this.nearMePopupLeft.set(`${rect.left}px`);
    this.nearMePopupOpen.set(true);
  }

  protected closeNearMePopup(): void {
    this.nearMePopupOpen.set(false);
  }

  protected onNearMeApply(settings: NearMeSettings): void {
    this.nearMeRadius.set(settings.radius);
    this.nearMeEnabled.set(settings.enabled);
    this.nearMePopupOpen.set(false);
  }
}
