import { Component, OnInit, OnDestroy, AfterViewInit, HostListener, ChangeDetectionStrategy, ChangeDetectorRef, ViewChild, ElementRef } from '@angular/core';
import { CommonModule }  from '@angular/common';
import { FormsModule }   from '@angular/forms';
import { Router }        from '@angular/router';
import { Subject }       from 'rxjs';
import { debounceTime, distinctUntilChanged, takeUntil } from 'rxjs/operators';
import { SidebarComponent } from '../../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent }  from '../../../../../shared/components/topbar/topbar.component';
import { EventsListService, EventListItem, EventSortField, EventSortDir } from '../../../../../core/services/events-list.service';

const PAGE_SIZE_INITIAL = 5;
const PAGE_SIZE_STEP    = 5;

@Component({
  selector: 'app-all-events',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './all-events.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AllEventsComponent implements OnInit, AfterViewInit, OnDestroy {

  constructor(
    private router: Router,
    private svc: EventsListService,
    private cdr: ChangeDetectorRef,
  ) {}

  region   = 'Šumadija, Srbija';
  userName = 'Sarah Jenkins';
  userRole = 'Regional Manager';

  items:        EventListItem[] = [];
  totalItems    = 0;
  isLoading     = false;
  isLoadingMore = false;
  displayCount  = PAGE_SIZE_INITIAL;

  searchQuery = '';
  sortField: EventSortField = 'lastUpdated';
  sortDir:   EventSortDir   = 'desc';

  sortOpen = false;
  sortOptions: { label: string; field: EventSortField; dir: EventSortDir }[] = [
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

  applySortOption(opt: { label: string; field: EventSortField; dir: EventSortDir }): void {
    this.sortField    = opt.field;
    this.sortDir      = opt.dir;
    this.displayCount = PAGE_SIZE_INITIAL;
    this.loadItems();
  }

  @ViewChild('scrollContainer') scrollContainer!: ElementRef<HTMLDivElement>;
  private _pendingScrollTop = 0;

  deleteTarget: EventListItem | null = null;
  isDeleting = false;

  private searchSubject = new Subject<string>();
  private destroy$      = new Subject<void>();

  ngOnInit(): void {
    const saved = this.svc.getListState();
    if (saved) {
      this.searchQuery  = saved.searchQuery;
      this.sortField    = saved.sortField as EventSortField;
      this.sortDir      = saved.sortDir   as EventSortDir;
      this.displayCount = saved.displayCount;
      this._pendingScrollTop = saved.scrollTop;
    }

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

  ngAfterViewInit(): void {
    if (this._pendingScrollTop > 0) {
      const st = this._pendingScrollTop;
      this._pendingScrollTop = 0;
      setTimeout(() => {
        if (this.scrollContainer?.nativeElement) {
          this.scrollContainer.nativeElement.scrollTop = st;
        }
      }, 150);
    }
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

  private saveListState(): void {
    this.svc.saveListState({
      searchQuery:  this.searchQuery,
      sortField:    this.sortField,
      sortDir:      this.sortDir,
      displayCount: this.displayCount,
      scrollTop:    this.scrollContainer?.nativeElement?.scrollTop ?? 0,
    });
  }

  navigateToAdd(): void {
    this.router.navigate(['/events/new'], { state: { returnTo: '/events' } });
  }

  viewEvent(item: EventListItem): void {
    this.saveListState();
    this.router.navigate(['events/detail', item.id], { state: item });
  }

  editEvent(item: EventListItem): void {
    this.saveListState();
    const full = this.svc.findById(item.id) ?? item;
    this.router.navigate(['/events/new'], {
      state: { place: full, editMode: true, returnTo: '/events' }
    });
  }

  promptDelete(item: EventListItem, event?: MouseEvent): void {
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

  trackById(_: number, item: EventListItem): string { return item.id; }
}