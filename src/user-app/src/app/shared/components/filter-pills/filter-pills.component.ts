import { Component, input, output } from '@angular/core';
import type { ExploreCategoryFilter } from '../../../core/models/explore-item.model';
import type { CategoryFilterOption } from '../../../core/constants/explore-ui.constants';

@Component({
  selector: 'app-filter-pills',
  standalone: true,
  templateUrl: './filter-pills.component.html',
  styleUrl: './filter-pills.component.scss',
})
export class FilterPillsComponent {
  readonly options = input.required<CategoryFilterOption[]>();
  readonly activeId = input.required<ExploreCategoryFilter>();

  readonly activeIdChange = output<ExploreCategoryFilter>();

  protected select(id: ExploreCategoryFilter): void {
    this.activeIdChange.emit(id);
  }
}
