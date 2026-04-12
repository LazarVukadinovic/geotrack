import { Component, input } from '@angular/core';

@Component({
  selector: 'app-placeholder-view',
  standalone: true,
  templateUrl: './placeholder-view.component.html',
  styleUrl: './placeholder-view.component.scss',
})
export class PlaceholderViewComponent {
  readonly title = input.required<string>();
  readonly hint = input('Coming soon.');
}
