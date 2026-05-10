import { Component, OnInit, OnDestroy, HostListener, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule }  from '@angular/forms';
import { Router }       from '@angular/router';
import { Subject }      from 'rxjs';
import { debounceTime, distinctUntilChanged, takeUntil } from 'rxjs/operators';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { RegionsService, RegionListItem, RegionSortField, RegionSortDir } from '../../../../core/services/national/regions.service';

const PAGE_SIZE_INITIAL = 5;
const PAGE_SIZE_STEP    = 5;

@Component({
  selector: 'all-regions',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './regions.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AllRegionsComponent implements OnInit, OnDestroy {

  constructor(
    private router: Router,
    private svc: RegionsService,
    private cdr: ChangeDetectorRef,
  ) {}

  @HostListener('document:click')
  onDocumentClick(): void {
    if (this.sortOpen) { this.sortOpen = false; this.cdr.markForCheck(); }
  }

  userName = 'Sarah Jenkins';
  userRole = 'National Manager';

  items:        RegionListItem[] = [];
  totalItems    = 0;
  isLoading     = false;
  isLoadingMore = false;
  displayCount  = PAGE_SIZE_INITIAL;

  searchQuery = '';
  sortField: RegionSortField = 'status';
  sortDir:   RegionSortDir   = 'asc';
  sortOpen = false;

  sortOptions: { label: string; field: RegionSortField; dir: RegionSortDir }[] = [
    { label: 'Status: Active First',  field: 'status',          dir: 'asc'  },
    { label: 'Status: Inactive First',field: 'status',          dir: 'desc' },
    { label: 'Most Persons',          field: 'assignedPersons', dir: 'desc' },
    { label: 'Fewest Persons',        field: 'assignedPersons', dir: 'asc'  },
    { label: 'A–Z',                   field: 'name',            dir: 'asc'  },
    { label: 'Z–A',                   field: 'name',            dir: 'desc' },
  ];

  get currentSortLabel(): string {
    return this.sortOptions.find(
      o => o.field === this.sortField && o.dir === this.sortDir
    )?.label ?? 'Status: Active First';
  }

  applySortOption(opt: { label: string; field: RegionSortField; dir: RegionSortDir }): void {
    this.sortField    = opt.field;
    this.sortDir      = opt.dir;
    this.displayCount = PAGE_SIZE_INITIAL;
    this.loadItems();
  }

  private searchSubject = new Subject<string>();
  private destroy$      = new Subject<void>();

  ngOnInit(): void {
    this.searchSubject.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      takeUntil(this.destroy$),
    ).subscribe(() => {
      this.displayCount = PAGE_SIZE_INITIAL;
      this.loadItems();
    });
    this.loadItems();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  loadItems(): void {
    this.isLoading = true;
    this.svc.getPage$({
      page: 1, pageSize: this.displayCount,
      search: this.searchQuery,
      sortField: this.sortField, sortDir: this.sortDir,
    }).pipe(takeUntil(this.destroy$)).subscribe(page => {
      this.items      = page.items;
      this.totalItems = page.total;
      this.isLoading  = false;
      this.cdr.markForCheck();
    });
  }

  loadMore(): void {
    this.isLoadingMore = true;
    this.displayCount += PAGE_SIZE_STEP;
    this.svc.getPage$({
      page: 1, pageSize: this.displayCount,
      search: this.searchQuery,
      sortField: this.sortField, sortDir: this.sortDir,
    }).pipe(takeUntil(this.destroy$)).subscribe(page => {
      this.items         = page.items;
      this.totalItems    = page.total;
      this.isLoadingMore = false;
      this.cdr.markForCheck();
    });
  }

  get hasMore(): boolean { return this.items.length < this.totalItems; }

  onSearchChange(): void { this.searchSubject.next(this.searchQuery); }

  navigateToAdd(): void { this.router.navigate(['/regions/new']); }

  editRegion(item: RegionListItem, event?: MouseEvent): void {
    event?.stopPropagation();
    this.router.navigate(['/regions', item.id], {
      state: { region: item, editMode: true },
    });
  }
  viewRegion(item: RegionListItem): void {
    this.router.navigate(['/regions', item.id], {
      state: { region: item, editMode: false },
    });
  }
  statusClass(status: string): string {
    if (status === 'Active')   return 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200';
    if (status === 'Inactive') return 'bg-gray-100 text-gray-500 ring-1 ring-gray-200';
    return '';
  }

  trackById(_: number, item: RegionListItem): string { return item.id; }

  deleteTarget: RegionListItem | null = null;
  isDeleting = false;

  promptDelete(item: RegionListItem, event?: MouseEvent): void {
    event?.stopPropagation();
    this.deleteTarget = item;
    this.cdr.markForCheck();
  }

  cancelDelete(): void {
    this.deleteTarget = null;
    this.cdr.markForCheck();
  }

  confirmDelete(): void {
    if (!this.deleteTarget) return;
    this.isDeleting = true;
    this.svc.delete$(this.deleteTarget.id)
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        this.isDeleting   = false;
        this.deleteTarget = null;
        if (this.displayCount > PAGE_SIZE_INITIAL) {
          this.displayCount = Math.max(PAGE_SIZE_INITIAL, this.displayCount - 1);
        }
        this.totalItems--;
        this.loadItems();
      });
  }
}