import {
  AfterViewInit,
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  signal,
} from '@angular/core';
import { Router } from '@angular/router';
import { HotelsListingService } from '../../services/hotels-listing.service';
import { HotelsScrollStateService } from '../../services/hotels-scroll-state.service';
import { HOTEL_FILTERS } from '../../models/hotels-listing.model';
import { EXPLORE_IMAGE_FALLBACK } from '../../../../core/constants/explore-media.constants';
import { AnalyticsService } from '../../../../core/services/analytics.service';
import { EmptyStateComponent } from '../../../../shared/components/empty-state/empty-state.component';
import { PageTopNavComponent } from '../../../../shared/components/page-top-nav/page-top-nav.component';

@Component({
  selector: 'app-hotels-listing-page',
  standalone: true,
  imports: [EmptyStateComponent, PageTopNavComponent],
  templateUrl: './hotels-listing-page.component.html',
  styleUrl: './hotels-listing-page.component.scss',
})
export class HotelsListingPageComponent implements AfterViewInit {
  private readonly service     = inject(HotelsListingService);
  private readonly router      = inject(Router);
  private readonly scrollState = inject(HotelsScrollStateService);
  private readonly elRef       = inject(ElementRef<HTMLElement>);
  private readonly analytics   = inject(AnalyticsService);

  protected readonly filters = HOTEL_FILTERS;
  protected readonly activeFilter = signal('all');
  protected readonly loadError = this.service.loadError;
  protected readonly loadMoreError = this.service.loadMoreError;
  protected readonly loading = this.service.loading;
  protected readonly loadingMore = this.service.loadingMore;
  protected readonly hasMore = this.service.hasMore;

  protected readonly filteredHotels = computed(() => this.service.hotels());

  protected readonly imgSrcById = signal<Record<string, string>>({});
  private readonly fallbackUsed = new Set<string>();

  constructor() {
    effect(() => {
      const next: Record<string, string> = {};
      for (const h of this.service.hotels()) {
        next[h.id] = h.image;
      }
      this.imgSrcById.set(next);
      this.fallbackUsed.clear();
    });
  }

  // ── Scroll restoration ──────────────────────────────────────────────────────

  ngAfterViewInit(): void {
    const state = this.scrollState.pop();
    if (!state) return;

    const host = this.elRef.nativeElement as HTMLElement;

    // Double RAF: first frame lets Angular finish any pending change detection
    // after route activation; second frame ensures layout is fully calculated
    // before we set scroll positions.
    requestAnimationFrame(() => {
      requestAnimationFrame(() => {
        const scrollEl  = host.querySelector<HTMLElement>('.hotels-page__scroll');
        const filtersEl = host.querySelector<HTMLElement>('.hotels-page__filters');

        if (scrollEl) {
          // Primary: scroll the clicked card into view so it is definitely visible.
          if (state.clickedCardId) {
            const card = scrollEl.querySelector<HTMLElement>(
              `[data-hotel-id="${state.clickedCardId}"]`,
            );
            if (card) {
              // 'nearest' scrolls the minimum amount needed to make the card visible.
              card.scrollIntoView({ block: 'nearest', behavior: 'instant' });
            } else {
              // Card not in DOM (e.g. filter changed) — fall back to raw position.
              scrollEl.scrollTop = state.scrollTop;
            }
          } else if (state.scrollTop > 0) {
            scrollEl.scrollTop = state.scrollTop;
          }
        }

        if (filtersEl && state.pillsLeft > 0) {
          filtersEl.scrollLeft = state.pillsLeft;
        }
      });
    });
  }

  // ── Interactions ────────────────────────────────────────────────────────────

  protected onImgError(id: string): void {
    if (this.fallbackUsed.has(id)) return;
    this.fallbackUsed.add(id);
    this.imgSrcById.update((m) => ({ ...m, [id]: EXPLORE_IMAGE_FALLBACK }));
  }

  protected setFilter(id: string): void {
    if (this.activeFilter() === id) return;
    this.activeFilter.set(id);
    this.service.setFilter(id);
    this.analytics.track('filter_applied', {
      entityType: 'hotel',
      metadata: { surface: 'hotels_listing', filter: id },
    });
  }

  protected toggleSaved(event: Event, id: string): void {
    event.stopPropagation();
    this.service.toggleSaved(id);
  }

  protected onHotelCard(id: string): void {
    // Save scroll state NOW — synchronously, before router.navigate() fires.
    // This is the only safe moment: the component is fully active, the DOM is
    // stable, and no Angular lifecycle hooks have started yet.
    const host      = this.elRef.nativeElement as HTMLElement;
    const scrollEl  = host.querySelector<HTMLElement>('.hotels-page__scroll');
    const filtersEl = host.querySelector<HTMLElement>('.hotels-page__filters');

    this.scrollState.save(
      scrollEl?.scrollTop  ?? 0,
      filtersEl?.scrollLeft ?? 0,
      id,
    );

    this.router.navigate(['/stays', id]);
  }

  protected loadMore(): void {
    this.service.loadMore();
  }
}
