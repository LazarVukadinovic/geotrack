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
import { EventsListingService } from '../../services/events-listing.service';
import { EventsListingScrollStateService } from '../../services/events-listing-scroll-state.service';
import { EVENTS_LISTING_FILTERS } from '../../models/events-listing.model';
import { EXPLORE_IMAGE_FALLBACK } from '../../../../core/constants/explore-media.constants';
import { AnalyticsService } from '../../../../core/services/analytics.service';
import { EmptyStateComponent } from '../../../../shared/components/empty-state/empty-state.component';
import { PageTopNavComponent } from '../../../../shared/components/page-top-nav/page-top-nav.component';

@Component({
  selector: 'app-events-listing-page',
  standalone: true,
  imports: [EmptyStateComponent, PageTopNavComponent],
  templateUrl: './events-listing-page.component.html',
  styleUrl: './events-listing-page.component.scss',
})
export class EventsListingPageComponent implements AfterViewInit {
  private readonly service     = inject(EventsListingService);
  private readonly router      = inject(Router);
  private readonly scrollState = inject(EventsListingScrollStateService);
  private readonly elRef       = inject(ElementRef<HTMLElement>);
  private readonly analytics   = inject(AnalyticsService);

  protected readonly filters      = EVENTS_LISTING_FILTERS;
  protected readonly activeFilter = signal('all');
  protected readonly loadError    = this.service.loadError;
  protected readonly loadMoreError = this.service.loadMoreError;
  protected readonly loading = this.service.loading;
  protected readonly loadingMore = this.service.loadingMore;
  protected readonly hasMore = this.service.hasMore;

  protected readonly filteredItems = computed(() => this.service.items());

  protected readonly imgSrcById = signal<Record<string, string>>({});
  private readonly fallbackUsed = new Set<string>();

  constructor() {
    effect(() => {
      const next: Record<string, string> = {};
      for (const item of this.service.items()) {
        next[item.id] = item.image;
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

    requestAnimationFrame(() => {
      requestAnimationFrame(() => {
        const scrollEl  = host.querySelector<HTMLElement>('.ev-page__scroll');
        const filtersEl = host.querySelector<HTMLElement>('.ev-page__filters');

        if (scrollEl) {
          if (state.clickedItemId) {
            const card = scrollEl.querySelector<HTMLElement>(
              `[data-ev-id="${state.clickedItemId}"]`,
            );
            if (card) {
              card.scrollIntoView({ block: 'nearest', behavior: 'instant' });
            } else if (state.scrollTop > 0) {
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
      entityType: 'event',
      metadata: { surface: 'events_listing', filter: id },
    });
  }

  protected toggleSaved(event: Event, id: string): void {
    event.stopPropagation();
    this.service.toggleSaved(id);
  }

  protected onItemCard(id: string): void {
    // Save state synchronously before navigation — guaranteed stable DOM.
    const host      = this.elRef.nativeElement as HTMLElement;
    const scrollEl  = host.querySelector<HTMLElement>('.ev-page__scroll');
    const filtersEl = host.querySelector<HTMLElement>('.ev-page__filters');

    this.scrollState.save(
      scrollEl?.scrollTop   ?? 0,
      filtersEl?.scrollLeft ?? 0,
      id,
    );

    // Events route to /events/:id (Event Detail), not /places/:id.
    this.router.navigate(['/events', id]);
  }

  protected loadMore(): void {
    this.service.loadMore();
  }
}
