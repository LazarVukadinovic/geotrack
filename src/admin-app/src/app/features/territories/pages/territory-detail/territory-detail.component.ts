import { CommonModule, Location } from '@angular/common';
import { ChangeDetectionStrategy, ChangeDetectorRef, Component, OnDestroy, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { TerritoriesService, TerritoryDetail } from '../../../../core/services/national/territories.service';

@Component({
  selector: 'app-territory-detail',
  standalone: true,
  imports: [CommonModule, SidebarComponent, TopbarComponent],
  templateUrl: './territory-detail.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TerritoryDetailComponent implements OnInit, OnDestroy {
  private readonly destroy$ = new Subject<void>();

  territory: TerritoryDetail | null = null;
  isLoading = true;
  notFound = false;
  userName = '';
  userRole = '';

  constructor(
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly location: Location,
    private readonly svc: TerritoriesService,
    private readonly cdr: ChangeDetectorRef,
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    const state = history.state as { territoryName?: string } | undefined;

    if (!id) {
      this.notFound = true;
      this.isLoading = false;
      return;
    }

    this.svc.getDetail$(id)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: territory => {
          this.territory = territory;
          this.isLoading = false;
          this.notFound = false;
          this.cdr.markForCheck();
        },
        error: () => {
          this.territory = state?.territoryName
            ? { id: Number(id), name: state.territoryName, regions: [] }
            : null;
          this.notFound = !this.territory;
          this.isLoading = false;
          this.cdr.markForCheck();
        },
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  goToRegion(region: { id: number; name: string }): void {
    this.router.navigate(['/regions', region.id], {
      state: {
        regionId: region.id,
        regionName: region.name,
        territoryId: this.territory?.id,
        territoryName: this.territory?.name,
      },
    });
  }

  goBack(): void {
    this.location.back();
  }
}
