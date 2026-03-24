import { Component, input, output } from '@angular/core';
import { IconButtonComponent } from '../icon-button/icon-button.component';

@Component({
  selector: 'app-header',
  standalone: true,
  imports: [IconButtonComponent],
  templateUrl: './app-header.component.html',
  styleUrl: './app-header.component.scss',
})
export class AppHeaderComponent {
  readonly title = input.required<string>();
  readonly showNotificationDot = input(true);

  readonly search = output<void>();
  readonly notifications = output<void>();
}
