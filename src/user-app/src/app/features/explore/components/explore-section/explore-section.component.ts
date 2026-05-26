import { Component, computed, effect, input, output, signal } from '@angular/core';
import { EXPLORE_IMAGE_FALLBACK } from '../../../../core/constants/explore-media.constants';
import { FavoriteButtonComponent } from '../../../../shared/components/favorite-button/favorite-button.component';
import { SectionHeaderComponent } from '../../../../shared/components/section-header/section-header.component';
import type { ExploreSection, ExploreSectionItem } from '../../models/explore-extras.model';

export interface ExploreSectionSaveEvent {
  section: ExploreSection;
  item: ExploreSectionItem;
}

export interface ExploreSectionItemEvent {
  section: ExploreSection;
  item: ExploreSectionItem;
}

@Component({
  selector: 'app-explore-section',
  standalone: true,
  imports: [SectionHeaderComponent, FavoriteButtonComponent],
  templateUrl: './explore-section.component.html',
  styleUrl: './explore-section.component.scss',
})
export class ExploreSectionComponent {
  readonly section = input.required<ExploreSection>();
  readonly showFavoriteButton = input(false);

  readonly save = output<ExploreSectionSaveEvent>();
  readonly itemActivated = output<ExploreSectionItemEvent>();
  readonly sectionMore = output<ExploreSection>();

  protected readonly imgSrcById = signal<Record<string, string>>({});
  private readonly fallbackUsed = new Set<string>();

  protected readonly sliderId = computed(() => `explore-section-${this.section().key}`);

  protected readonly actionLabel = computed(() =>
    this.section().moreRoute ? 'More' : null
  );

  constructor() {
    effect(() => {
      const next: Record<string, string> = {};

      for (const item of this.section().items) {
        next[item.id] = item.image || EXPLORE_IMAGE_FALLBACK;
      }

      this.imgSrcById.set(next);
      this.fallbackUsed.clear();
    });
  }

  protected onImgError(id: string): void {
    if (this.fallbackUsed.has(id)) return;

    this.fallbackUsed.add(id);
    this.imgSrcById.update((map) => ({
      ...map,
      [id]: EXPLORE_IMAGE_FALLBACK,
    }));
  }

  protected openItem(item: ExploreSectionItem): void {
    this.itemActivated.emit({
      section: this.section(),
      item,
    });
  }

  protected saveItem(item: ExploreSectionItem): void {
    this.save.emit({
      section: this.section(),
      item,
    });
  }

  protected openMore(): void {
    if (!this.section().moreRoute) return;
    this.sectionMore.emit(this.section());
  }

  protected canSave(item: ExploreSectionItem): boolean {
    return item.entityType === 'place' || item.entityType === 'stay' || item.entityType === 'event';
  }

  protected isStay(item: ExploreSectionItem): boolean {
    return item.entityType === 'stay' || this.section().cardType === 'stay-card';
  }

  protected isEvent(item: ExploreSectionItem): boolean {
    return item.entityType === 'event' || this.section().cardType === 'event-card';
  }
}