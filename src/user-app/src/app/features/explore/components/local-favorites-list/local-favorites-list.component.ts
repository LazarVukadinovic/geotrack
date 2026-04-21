import { Component, input, output } from '@angular/core';
import type { ExploreItem } from '../../../../core/models/explore-item.model';
import { DestinationCardComponent } from '../../../../shared/components/destination-card/destination-card.component';
import { SectionHeaderComponent } from '../../../../shared/components/section-header/section-header.component';
import { EmptyStateComponent } from '../../../../shared/components/empty-state/empty-state.component';

@Component({
  selector: 'app-local-favorites-list',
  standalone: true,
  imports: [SectionHeaderComponent, DestinationCardComponent, EmptyStateComponent],
  templateUrl: './local-favorites-list.component.html',
  styleUrl: './local-favorites-list.component.scss',
})
export class LocalFavoritesListComponent {
  readonly items = input.required<ExploreItem[]>();
  readonly showFavoriteButton = input(false);

  readonly bookmarkToggled = output<string>();
  readonly cardActivated = output<string>();
  readonly sectionMore = output<void>();
}
