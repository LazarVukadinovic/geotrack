import { AfterViewInit, Component, computed, ElementRef, inject, OnDestroy, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ExploreScrollStateService } from '../../services/explore-scroll-state.service';
import { AppHeaderComponent } from '../../../../shared/components/app-header/app-header.component';
import { SearchBarComponent } from '../../../../shared/components/search-bar/search-bar.component';
import { WelcomeHeroComponent } from '../../components/welcome-hero/welcome-hero.component';
import { PromoCardComponent } from '../../components/promo-card/promo-card.component';
import { EventsSliderComponent } from '../../components/events-slider/events-slider.component';
import { PopularNearbySliderComponent } from '../../components/popular-nearby-slider/popular-nearby-slider.component';
import { TopStaysSliderComponent } from '../../components/top-stays-slider/top-stays-slider.component';
import { MustSeeSliderComponent } from '../../components/must-see-slider/must-see-slider.component';
import { LandmarksSliderComponent } from '../../components/landmarks-slider/landmarks-slider.component';
import { LocalFavoritesListComponent } from '../../components/local-favorites-list/local-favorites-list.component';
import { ExploreService } from '../../services/explore.service';

@Component({
  selector: 'app-explore-page',
  standalone: true,
  imports: [
    AppHeaderComponent,
    SearchBarComponent,
    WelcomeHeroComponent,
    PromoCardComponent,
    EventsSliderComponent,
    PopularNearbySliderComponent,
    TopStaysSliderComponent,
    MustSeeSliderComponent,
    LandmarksSliderComponent,
    LocalFavoritesListComponent,
  ],
  templateUrl: './explore-page.component.html',
  styleUrl: './explore-page.component.scss',
})
export class ExplorePageComponent implements AfterViewInit, OnDestroy {
  private readonly explore     = inject(ExploreService);
  private readonly router      = inject(Router);
  private readonly elRef       = inject(ElementRef<HTMLElement>);
  private readonly scrollState = inject(ExploreScrollStateService);

  /** Mock hero location — later replace with user / geo service. */
  protected readonly heroCity    = signal('Dubrovnik');
  protected readonly heroCountry = signal('Croatia');

  protected readonly searchQuery = signal('');

  protected readonly promo = computed(() => this.explore.promo());

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
    /* future: router.navigate(['/auth/login']) */
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
