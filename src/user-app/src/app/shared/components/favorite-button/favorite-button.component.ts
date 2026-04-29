import { Component, computed, input, output } from '@angular/core';
import { IconButtonComponent } from '../icon-button/icon-button.component';

@Component({
  selector: 'app-favorite-button',
  standalone: true,
  imports: [IconButtonComponent],
  templateUrl: './favorite-button.component.html',
  styleUrl: './favorite-button.component.scss',
})
export class FavoriteButtonComponent {
  readonly saved = input(false);
  readonly itemLabel = input('');
  readonly clicked = output<void>();

  protected readonly ariaLabel = computed(() => {
    const label = this.itemLabel().trim();
    if (this.saved()) {
      return 'Saved. Choose collection';
    }

    return label ? `Save ${label} to collection` : 'Save to collection';
  });

  protected onClick(): void {
    this.clicked.emit();
  }
}
