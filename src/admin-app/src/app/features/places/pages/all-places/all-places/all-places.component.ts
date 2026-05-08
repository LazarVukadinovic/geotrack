import { Component, OnInit, OnDestroy, HostListener, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { CommonModule }  from '@angular/common';
import { FormsModule }   from '@angular/forms';
import { Router }        from '@angular/router';
import { Subject }       from 'rxjs';
import { debounceTime, distinctUntilChanged, takeUntil } from 'rxjs/operators';
import { SidebarComponent } from '../../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent }  from '../../../../../shared/components/topbar/topbar.component';
import { PlacesListService, PlaceListItem, PlaceSortField, PlaceSortDir } from '../../../../../core/services/places-list.service';

const PAGE_SIZE_INITIAL = 5;
const PAGE_SIZE_STEP    = 5;

@Component({
  selector: 'app-all-places',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './all-places.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AllPlacesComponent implements OnInit, OnDestroy {

  constructor(
    private router: Router,
    private svc: PlacesListService,
    private cdr: ChangeDetectorRef,
  ) {}

  region   = 'Šumadija, Srbija';
  userName = 'Sarah Jenkins';
  userRole = 'Regional Manager';

  items:        PlaceListItem[] = [];
  totalItems    = 0;
  isLoading     = false;
  isLoadingMore = false;
  displayCount  = PAGE_SIZE_INITIAL;

  searchQuery = '';
  sortField: PlaceSortField = 'lastUpdated';
  sortDir:   PlaceSortDir   = 'desc';

  sortOpen = false;
  sortOptions: { label: string; field: PlaceSortField; dir: PlaceSortDir }[] = [
    { label: 'Newest First', field: 'lastUpdated', dir: 'desc' },
    { label: 'Oldest First', field: 'lastUpdated', dir: 'asc'  },
    { label: 'A–Z',          field: 'name',        dir: 'asc'  },
    { label: 'Z–A',          field: 'name',        dir: 'desc' },
  ];

  get currentSortLabel(): string {
    return this.sortOptions.find(
      o => o.field === this.sortField && o.dir === this.sortDir
    )?.label ?? 'Newest First';
  }

  applySortOption(opt: { label: string; field: PlaceSortField; dir: PlaceSortDir }): void {
    this.sortField    = opt.field;
    this.sortDir      = opt.dir;
    this.displayCount = PAGE_SIZE_INITIAL;
    this.loadItems();
  }

  deleteTarget: PlaceListItem | null = null;
  isDeleting = false;

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

  @HostListener('document:click')
  onDocumentClick(): void {
    if (this.sortOpen) { this.sortOpen = false; this.cdr.markForCheck(); }
  }

  navigateToAdd(): void { this.router.navigate(['/places/new']); }

  viewPlace(item: PlaceListItem): void {
    this.router.navigate(['/places', item.id]);
  }

  editPlace(item: PlaceListItem): void {
    this.router.navigate(['/places/new'], { state: { place: item } });
  }

  promptDelete(item: PlaceListItem, event?: MouseEvent): void {
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

  statusClass(status: string): string {
    if (status === 'Published') return 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200';
    if (status === 'Draft')     return 'bg-amber-50  text-amber-700  ring-1 ring-amber-200';
    if (status === 'Archived')  return 'bg-gray-100  text-gray-500   ring-1 ring-gray-200';
    return '';
  }

  trackById(_: number, item: PlaceListItem): string { return item.id; }
}