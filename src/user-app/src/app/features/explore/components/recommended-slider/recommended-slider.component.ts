import { Component, input, output } from '@angular/core';
import type { ExploreItem } from '../../../../core/models/explore-item.model';
import { FeaturedCardComponent } from '../../../../shared/components/featured-card/featured-card.component';
import { SectionHeaderComponent } from '../../../../shared/components/section-header/section-header.component';
import { EmptyStateComponent } from '../../../../shared/components/empty-state/empty-state.component';

@Component({
  selector: 'app-recommended-slider',
  standalone: true,
  imports: [SectionHeaderComponent, FeaturedCardComponent, EmptyStateComponent],
  templateUrl: './recommended-slider.component.html',
  styleUrl: './recommended-slider.component.scss',
})
export class RecommendedSliderComponent {
  readonly items = input.required<ExploreItem[]>();

  readonly bookmarkToggled = output<string>();
  readonly moreClicked = output<string>();
  readonly sectionMore = output<void>();
}
