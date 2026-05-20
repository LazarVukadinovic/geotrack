import { CommonModule } from '@angular/common';
import { Component, computed, inject, input } from '@angular/core';
import { Router } from '@angular/router';

@Component({
  selector: 'app-settings-subpage-shell',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './settings-subpage-shell.component.html',
  styleUrl: './settings-subpage-shell.component.css',
})
export class SettingsSubpageShellComponent {
  readonly title = input.required<string>();
  readonly description = input<string>('');
  readonly headerDescription = input<string | null>(null);
  readonly fallbackRoute = input('/settings');

  protected readonly resolvedHeaderDescription = computed(() => this.headerDescription() ?? this.description());
  private readonly router = inject(Router);

  protected goBack(): void {
    const navigationId = Number(globalThis.history?.state?.navigationId ?? 0);

    if (navigationId > 1) {
      globalThis.history.back();
      return;
    }

    void this.router.navigateByUrl(this.fallbackRoute());
  }
}
