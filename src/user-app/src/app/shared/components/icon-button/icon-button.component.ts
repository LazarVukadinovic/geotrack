import { Component, input, output } from '@angular/core';

@Component({
  selector: 'app-icon-button',
  standalone: true,
  templateUrl: './icon-button.component.html',
  styleUrl: './icon-button.component.scss',
})
export class IconButtonComponent {
  readonly ariaLabel = input.required<string>();
  readonly pressed = input(false);
  /** High-contrast pill for use on top of photography. */
  readonly variant = input<'default' | 'on-media'>('default');
  /** Slightly smaller tap target for dense cards. */
  readonly compact = input(false);
  readonly stopPropagation = input(false);
  readonly clicked = output<MouseEvent>();

  protected onClick(event: MouseEvent): void {
    if (this.stopPropagation()) {
      event.stopPropagation();
    }
    this.clicked.emit(event);
  }
}
