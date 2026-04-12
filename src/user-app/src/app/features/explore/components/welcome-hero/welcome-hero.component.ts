import { Component, input } from '@angular/core';

@Component({
  selector: 'app-welcome-hero',
  standalone: true,
  templateUrl: './welcome-hero.component.html',
  styleUrl: './welcome-hero.component.scss',
})
export class WelcomeHeroComponent {
  /** Primary destination name shown with strong emphasis. */
  readonly city = input.required<string>();
  /** Country / region line in accent color. */
  readonly country = input.required<string>();
}
