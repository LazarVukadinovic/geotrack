import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs/operators';
import { BottomNavComponent } from '../../../shared/components/bottom-nav/bottom-nav.component';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, BottomNavComponent],
  templateUrl: './app-shell.component.html',
  styleUrl: './app-shell.component.scss',
})
export class AppShellComponent implements OnInit {
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly showBottomNav = signal(true);

  ngOnInit(): void {
    this.router.events
      .pipe(
        filter((e): e is NavigationEnd => e instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => this.syncBottomNav());

    // Router tree / snapshots may not be final during the first tick; defer reads slightly.
    queueMicrotask(() => this.syncBottomNav());
  }

  private syncBottomNav(): void {
    const root = this.router.routerState?.root;
    if (!root) {
      this.showBottomNav.set(true);
      return;
    }

    let route: ActivatedRoute = root;
    while (route.firstChild) {
      route = route.firstChild;
    }

    const hideBottomNav = route.snapshot?.data?.['hideBottomNav'] === true;
    this.showBottomNav.set(!hideBottomNav);
  }
}
