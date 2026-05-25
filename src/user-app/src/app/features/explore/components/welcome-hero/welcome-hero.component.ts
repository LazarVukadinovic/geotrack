import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-welcome-hero',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './welcome-hero.component.html',
  styleUrl: './welcome-hero.component.scss',
})
export class WelcomeHeroComponent {
  readonly city = input.required<string>();
  readonly country = input.required<string>();
  readonly destinationRoute = input<string | null>(null);
  readonly countryRoute = input<string | null>(null);
}
