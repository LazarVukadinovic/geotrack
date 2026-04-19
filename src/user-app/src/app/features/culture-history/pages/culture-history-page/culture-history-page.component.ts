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
import { CultureHistoryService } from '../../services/culture-history.service';
import { CultureHistoryScrollStateService } from '../../services/culture-history-scroll-state.service';
import { CULTURE_HISTORY_FILTERS } from '../../models/culture-history.model';
import { EXPLORE_IMAGE_FALLBACK } from '../../../../core/constants/explore-media.constants';
import { EmptyStateComponent } from '../../../../shared/components/empty-state/empty-state.component';

@Component({
  selector: 'app-culture-history-page',
  standalone: true,
  imports: [EmptyStateComponent],
  templateUrl: './culture-history-page.component.html',
  styleUrl: './culture-history-page.component.scss',
})
export class CultureHistoryPageComponent implements AfterViewInit {
  private readonly service     = inject(CultureHistoryService);
  private readonly location    = inject(Location);
  private readonly router      = inject(Router);
  private readonly scrollState = inject(CultureHistoryScrollStateService);
  private readonly elRef       = inject(ElementRef<HTMLElement>);

  protected readonly filters      = CULTURE_HISTORY_FILTERS;
  protected readonly activeFilter = signal('all');
  protected readonly loadError    = this.service.loadError;

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
        const scrollEl  = host.querySelector<HTMLElement>('.ch-page__scroll');
        const filtersEl = host.querySelector<HTMLElement>('.ch-page__filters');

        if (scrollEl) {
          if (state.clickedItemId) {
            const card = scrollEl.querySelector<HTMLElement>(
              `[data-ch-id="${state.clickedItemId}"]`,
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
    const scrollEl  = host.querySelector<HTMLElement>('.ch-page__scroll');
    const filtersEl = host.querySelector<HTMLElement>('.ch-page__filters');

    this.scrollState.save(
      scrollEl?.scrollTop   ?? 0,
      filtersEl?.scrollLeft ?? 0,
      id,
    );

    // Culture & History items are place-type content.
    this.router.navigate(['/places', id]);
  }

  protected goBack(): void {
    this.location.back();
  }
}
