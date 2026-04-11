import { Component, effect, input, output, signal } from '@angular/core';
import { EXPLORE_IMAGE_FALLBACK } from '../../../../core/constants/explore-media.constants';
import type { ExploreEvent } from '../../models/explore-extras.model';
import { SectionHeaderComponent } from '../../../../shared/components/section-header/section-header.component';

@Component({
  selector: 'app-events-slider',
  standalone: true,
  imports: [SectionHeaderComponent],
  templateUrl: './events-slider.component.html',
  styleUrl: './events-slider.component.scss',
})
export class EventsSliderComponent {
  readonly items = input.required<ExploreEvent[]>();
  readonly sectionMore = output<void>();
  readonly eventActivated = output<string>();

  protected readonly imgSrcById = signal<Record<string, string>>({});
  private readonly fallbackUsed = new Set<string>();

  constructor() {
    effect(() => {
      const next: Record<string, string> = {};
      for (const ev of this.items()) {
        next[ev.id] = ev.image;
      }
      this.imgSrcById.set(next);
      this.fallbackUsed.clear();
    });
  }

  protected onImgError(id: string): void {
    if (this.fallbackUsed.has(id)) {
      return;
    }
    this.fallbackUsed.add(id);
    this.imgSrcById.update((m) => ({ ...m, [id]: EXPLORE_IMAGE_FALLBACK }));
  }

  protected open(id: string): void {
    this.eventActivated.emit(id);
  }
}

