import { Location } from '@angular/common';
import { Component, inject, input } from '@angular/core';
import { Router } from '@angular/router';

@Component({
  selector: 'app-page-top-nav',
  standalone: true,
  templateUrl: './page-top-nav.component.html',
  styleUrl: './page-top-nav.component.scss',
})
export class PageTopNavComponent {
  readonly title = input.required<string>();
  readonly fallbackRoute = input('/explore');

  private readonly location = inject(Location);
  private readonly router = inject(Router);

  protected goBack(): void {
    const navigationId = Number(globalThis.history?.state?.navigationId ?? 0);

    if (navigationId > 1) {
      this.location.back();
      return;
    }

    void this.router.navigateByUrl(this.fallbackRoute());
  }
}
