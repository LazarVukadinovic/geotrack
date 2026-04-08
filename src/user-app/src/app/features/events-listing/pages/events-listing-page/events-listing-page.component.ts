import {
  AfterViewInit,
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  signal,
} from '@angular/core';
import { Location } from '@angular/common';
import { Router } from '@angular/router';
import { EventsListingService } from '../../services/events-listing.service';
import { EventsListingScrollStateService } from '../../services/events-listing-scroll-state.service';
import { EVENTS_LISTING_FILTERS } from '../../models/events-listing.model';
import { EXPLORE_IMAGE_FALLBACK } from '../../../../core/constants/explore-media.constants';

@Component({
  selector: 'app-events-listing-page',
  standalone: true,
  imports: [],
  templateUrl: './events-listing-page.component.html',
  styleUrl: './events-listing-page.component.scss',
})
export class EventsListingPageComponent implements AfterViewInit {
  private readonly service     = inject(EventsListingService);
  private readonly location    = inject(Location);
  private readonly router      = inject(Router);
  private readonly scrollState = inject(EventsListingScrollStateService);
  private readonly elRef       = inject(ElementRef<HTMLElement>);

  protected readonly filters      = EVENTS_LISTING_FILTERS;
  protected readonly activeFilter = signal('all');

  protected readonly filteredItems = computed(() => {
    const filter = this.activeFilter();
    const items  = this.service.items();
    if (filter === 'all') return items;
    return items.filter((item) => item.tags.includes(filter));
  });

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
    this.activeFilter.set(id);
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

  protected goBack(): void {
    this.location.back();
  }
}
