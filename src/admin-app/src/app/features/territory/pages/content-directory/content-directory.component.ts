import { ChangeDetectionStrategy, ChangeDetectorRef, Component, OnDestroy, OnInit, inject } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, debounceTime, distinctUntilChanged, finalize, takeUntil } from 'rxjs';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { AuthService } from '../../../../core/services/auth.service';
import {
  TerritoryAssignedTerritory,
  TerritoryContentDirectoryItem,
} from '../../../../core/models/dashboard.model';
import { TerritoryDashboardService } from '../../../../core/services/territory-dashboard.service';

type TerritoryContentType = 'destinations' | 'locations' | 'places' | 'events';

@Component({
  selector: 'app-territory-content-directory',
  standalone: true,
  imports: [CommonModule, DatePipe, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './content-directory.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TerritoryContentDirectoryComponent implements OnInit, OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly territoryDashboard = inject(TerritoryDashboardService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly destroy$ = new Subject<void>();
  private readonly search$ = new Subject<string>();

  readonly contentTypes: { value: TerritoryContentType; label: string }[] = [
    { value: 'destinations', label: 'Destinations' },
    { value: 'locations', label: 'Locations' },
    { value: 'places', label: 'Places' },
    { value: 'events', label: 'Events' },
  ];

  assignedTerritories: TerritoryAssignedTerritory[] = [];
  activeTerritory: TerritoryAssignedTerritory | null = null;
  activeTerritoryId: number | null = null;
  items: TerritoryContentDirectoryItem[] = [];
  totalCount = 0;
  contentType: TerritoryContentType = 'destinations';
  searchQuery = '';
  isLoading = false;
  isTerritoryLoading = false;
  errorMessage = '';

  get userName(): string { return this.auth.fullName; }
  get userRole(): string { return this.auth.getUser()?.role ?? ''; }
  get title(): string {
    return this.contentTypes.find(type => type.value === this.contentType)?.label ?? 'Content';
  }

  ngOnInit(): void {
    this.route.queryParamMap
      .pipe(takeUntil(this.destroy$))
      .subscribe(params => {
        this.contentType = this.normalizeType(params.get('type'));
        if (this.activeTerritoryId || this.assignedTerritories.length === 0) {
          this.loadContent();
        }
      });

    this.search$
      .pipe(debounceTime(250), distinctUntilChanged(), takeUntil(this.destroy$))
      .subscribe(() => this.loadContent());

    this.loadTerritories();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  selectType(type: TerritoryContentType): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { type },
      queryParamsHandling: 'merge',
    });
  }

  onSearchChange(): void {
    this.search$.next(this.searchQuery);
  }

  onTerritoryChange(territoryId: number): void {
    this.activeTerritory = this.assignedTerritories.find(item => item.id === territoryId) ?? null;
    this.activeTerritoryId = territoryId;
    this.territoryDashboard.setActiveTerritoryId(territoryId);
    this.loadContent();
  }

  formatViews(value: number): string {
    return value.toLocaleString();
  }

  private loadTerritories(): void {
    this.isTerritoryLoading = true;
    this.territoryDashboard.getAssignedTerritories()
      .pipe(finalize(() => {
        this.isTerritoryLoading = false;
        this.cdr.markForCheck();
      }))
      .subscribe({
        next: territories => {
          this.assignedTerritories = territories;
          const storedId = this.territoryDashboard.getActiveTerritoryId();
          this.activeTerritory = storedId
            ? territories.find(territory => territory.id === storedId) ?? territories[0] ?? null
            : territories[0] ?? null;
          this.activeTerritoryId = this.activeTerritory?.id ?? null;
          if (this.activeTerritoryId) {
            this.territoryDashboard.setActiveTerritoryId(this.activeTerritoryId);
          }
          this.loadContent();
        },
        error: () => {
          this.errorMessage = 'Unable to load assigned territories.';
          this.items = [];
          this.totalCount = 0;
        },
      });
  }

  private loadContent(): void {
    this.isLoading = true;
    this.errorMessage = '';
    this.territoryDashboard.getContent({
      territoryId: this.activeTerritoryId,
      type: this.contentType,
      search: this.searchQuery,
      sort: this.contentType === 'events' ? 'upcoming' : 'views',
      skip: 0,
      take: 50,
    }).pipe(finalize(() => {
      this.isLoading = false;
      this.cdr.markForCheck();
    })).subscribe({
      next: response => {
        this.items = response.items;
        this.totalCount = response.totalCount;
      },
      error: error => {
        this.items = [];
        this.totalCount = 0;
        this.errorMessage = error?.error?.message ?? 'Unable to load territory content.';
      },
    });
  }

  private normalizeType(value: string | null): TerritoryContentType {
    return value === 'locations' || value === 'places' || value === 'events'
      ? value
      : 'destinations';
  }
}
