import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  HostListener,
  OnDestroy,
  OnInit,
  inject,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Observable, Subject, finalize, interval } from 'rxjs';
import { debounceTime, distinctUntilChanged, switchMap, takeUntil } from 'rxjs/operators';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { AdminDashboardService } from '../../../../core/services/administrator/admin-dashboard.service';
import { AdminSupportTicketsService } from '../../../../core/services/administrator/admin-support-tickets.service';
import {
  AdminSupportTicketCategoryFilter,
  AdminSupportTicketDetail,
  AdminSupportTicketListCounts,
  AdminSupportTicketListItem,
  AdminSupportTicketPriority,
  AdminSupportTicketPriorityFilter,
  AdminSupportTicketSort,
  AdminSupportTicketStatus,
  AdminSupportTicketStatusFilter,
} from '../../../../core/models/administrator/admin-support-ticket.model';

const PAGE_SIZE = 25;

type TicketAction = 'resolve' | 'dismiss' | 'reopen' | 'saveNote';
type TicketActionTarget = AdminSupportTicketListItem | AdminSupportTicketDetail;

@Component({
  selector: 'app-support-tickets',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './support-tickets.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SupportTicketsComponent implements OnInit, OnDestroy {
  private readonly ticketsSvc = inject(AdminSupportTicketsService);
  private readonly dashboardSvc = inject(AdminDashboardService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly route = inject(ActivatedRoute);
  private readonly destroy$ = new Subject<void>();
  private readonly searchSubject = new Subject<string>();

  private loadSequence = 0;
  private detailLoadSequence = 0;
  private successTimer: ReturnType<typeof setTimeout> | null = null;

  readonly pageSize = PAGE_SIZE;
  readonly statusTabs: { label: string; value: AdminSupportTicketStatusFilter }[] = [
    { label: 'Open', value: 'open' },
    { label: 'Resolved', value: 'resolved' },
    { label: 'Dismissed', value: 'dismissed' },
    { label: 'All', value: 'all' },
  ];
  readonly categories: { label: string; value: AdminSupportTicketCategoryFilter }[] = [
    { label: 'All types', value: 'all' },
    { label: 'Account access', value: 'account-access' },
    { label: 'Saved places', value: 'saved-places-and-collections' },
    { label: 'Location and maps', value: 'location-and-maps' },
    { label: 'Ask Agent', value: 'ask-agent' },
    { label: 'Events and stays', value: 'events-and-stays' },
    { label: 'Privacy and data', value: 'privacy-and-data' },
  ];
  readonly priorities: { label: string; value: AdminSupportTicketPriorityFilter }[] = [
    { label: 'All priorities', value: 'all' },
    { label: 'Low', value: 'low' },
    { label: 'Normal', value: 'normal' },
    { label: 'High', value: 'high' },
    { label: 'Critical', value: 'critical' },
  ];
  readonly sortOptions: { label: string; value: AdminSupportTicketSort }[] = [
    { label: 'Newest First', value: 'newest' },
    { label: 'Oldest First', value: 'oldest' },
    { label: 'Priority', value: 'priorityDesc' },
    { label: 'Recently Updated', value: 'updatedDesc' },
  ];

  tickets: AdminSupportTicketListItem[] = [];
  counts: AdminSupportTicketListCounts = {
    openCount: 0,
    resolvedCount: 0,
    dismissedCount: 0,
    allCount: 0,
  };
  totalCount = 0;
  nextSkip: number | null = null;
  hasMore = false;
  supportReportsCount = 0;

  isLoading = false;
  isLoadingMore = false;
  errorMessage = '';
  successMessage = '';

  searchQuery = '';
  statusFilter: AdminSupportTicketStatusFilter = 'open';
  categoryFilter: AdminSupportTicketCategoryFilter = 'all';
  priorityFilter: AdminSupportTicketPriorityFilter = 'all';
  sort: AdminSupportTicketSort = 'newest';
  sortOpen = false;
  categoryOpen = false;
  priorityOpen = false;

  openDropdownId: number | null = null;
  dropdownPos = { top: 0, right: 0 };

  detailTarget: AdminSupportTicketListItem | null = null;
  detailTicket: AdminSupportTicketDetail | null = null;
  detailInternalNote = '';
  isDetailLoading = false;
  detailErrorMessage = '';
  actionKey: string | null = null;

  ngOnInit(): void {
    this.loadSupportReportsBadge();
    this.route.queryParams.pipe(takeUntil(this.destroy$)).subscribe(params => {
      const ticketId = this.toPositiveInt(params['ticketId'] ?? params['id']);
      if (ticketId) {
        this.openDetailFromRoute(ticketId);
      }
    });
    this.resetAndLoad();

    this.searchSubject.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      takeUntil(this.destroy$),
    ).subscribe(() => this.resetAndLoad());

    // Poll every 30 seconds to keep counts reactive without page refresh
    interval(30_000).pipe(
      takeUntil(this.destroy$),
      switchMap(() => this.ticketsSvc.getPage$({
        status: this.statusFilter,
        category: this.categoryFilter,
        priority: this.priorityFilter,
        search: this.searchQuery,
        sort: this.sort,
        skip: 0,
        take: PAGE_SIZE,
      })),
    ).subscribe({
      next: page => {
        if (this.actionKey) return; // don't overwrite while action is running
        this.tickets = page.items;
        this.totalCount = page.totalCount;
        this.nextSkip = page.nextSkip;
        this.hasMore = page.hasMore;
        this.counts = page.counts;
        this.supportReportsCount = page.counts.openCount;
        this.cdr.markForCheck();
      },
    });
  }

  ngOnDestroy(): void {
    if (this.successTimer) {
      clearTimeout(this.successTimer);
    }

    this.destroy$.next();
    this.destroy$.complete();
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    let changed = false;

    if (this.openDropdownId !== null) {
      this.openDropdownId = null;
      changed = true;
    }
    if (this.sortOpen) {
      this.sortOpen = false;
      changed = true;
    }
    if (this.categoryOpen) {
      this.categoryOpen = false;
      changed = true;
    }
    if (this.priorityOpen) {
      this.priorityOpen = false;
      changed = true;
    }

    if (changed) {
      this.cdr.markForCheck();
    }
  }

  @HostListener('document:keydown.escape')
  onEscapeKey(): void {
    if (this.detailTarget && !this.actionKey) {
      this.closeDetail();
      return;
    }

    if (this.openDropdownId !== null || this.sortOpen || this.categoryOpen || this.priorityOpen) {
      this.openDropdownId = null;
      this.sortOpen = false;
      this.categoryOpen = false;
      this.priorityOpen = false;
      this.cdr.markForCheck();
    }
  }

  get unresolvedCount(): number {
    return this.counts.openCount || this.supportReportsCount;
  }

  get categoryFilterLabel(): string {
    return this.categories.find(option => option.value === this.categoryFilter)?.label ?? 'All types';
  }

  get priorityFilterLabel(): string {
    return this.priorities.find(option => option.value === this.priorityFilter)?.label ?? 'All priorities';
  }

  get currentSortLabel(): string {
    return this.sortOptions.find(option => option.value === this.sort)?.label ?? 'Newest First';
  }

  statusCount(status: AdminSupportTicketStatusFilter): number {
    switch (status) {
      case 'open':
        return this.counts.openCount;
      case 'resolved':
        return this.counts.resolvedCount;
      case 'dismissed':
        return this.counts.dismissedCount;
      default:
        return this.counts.allCount;
    }
  }

  statusCountClass(status: AdminSupportTicketStatusFilter): string {
    switch (status) {
      case 'open':
        return 'bg-red-100 text-red-600';
      case 'resolved':
        return 'bg-emerald-100 text-emerald-700';
      case 'dismissed':
        return 'bg-gray-200 text-gray-600';
      default:
        return 'bg-slate-200 text-slate-600';
    }
  }

  onSearchChange(): void {
    this.searchSubject.next(this.searchQuery);
  }

  setStatusFilter(status: AdminSupportTicketStatusFilter): void {
    if (this.statusFilter === status) {
      return;
    }

    this.statusFilter = status;
    this.openDropdownId = null;
    this.resetAndLoad();
  }

  toggleCategory(): void {
    const next = !this.categoryOpen;
    this.categoryOpen = next;
    if (next) { this.priorityOpen = false; this.sortOpen = false; }
    this.cdr.markForCheck();
  }

  togglePriority(): void {
    const next = !this.priorityOpen;
    this.priorityOpen = next;
    if (next) { this.categoryOpen = false; this.sortOpen = false; }
    this.cdr.markForCheck();
  }

  toggleSort(): void {
    const next = !this.sortOpen;
    this.sortOpen = next;
    if (next) { this.categoryOpen = false; this.priorityOpen = false; }
    this.cdr.markForCheck();
  }

  applyCategory(value: AdminSupportTicketCategoryFilter): void {
    this.categoryFilter = value;
    this.categoryOpen = false;
    this.resetAndLoad();
  }

  applyPriority(value: AdminSupportTicketPriorityFilter): void {
    this.priorityFilter = value;
    this.priorityOpen = false;
    this.resetAndLoad();
  }

  applySort(value: AdminSupportTicketSort): void {
    this.sort = value;
    this.sortOpen = false;
    this.resetAndLoad();
  }

  retry(): void {
    this.resetAndLoad();
  }

  loadMore(): void {
    if (!this.hasMore || this.isLoadingMore || this.nextSkip === null) {
      return;
    }

    this.loadTickets(false);
  }

  openDetail(ticket: AdminSupportTicketListItem, event?: Event): void {
    event?.stopPropagation();
    this.openDropdownId = null;
    this.sortOpen = false;
    this.categoryOpen = false;
    this.priorityOpen = false;
    this.detailTarget = ticket;
    this.detailTicket = null;
    this.detailInternalNote = '';
    this.detailErrorMessage = '';
    this.loadDetail(ticket.ticketId);
    this.cdr.markForCheck();
  }

  closeDetail(): void {
    if (this.actionKey) {
      return;
    }

    this.detailLoadSequence++;
    this.detailTarget = null;
    this.detailTicket = null;
    this.detailInternalNote = '';
    this.isDetailLoading = false;
    this.detailErrorMessage = '';
    this.cdr.markForCheck();
  }

  retryDetail(): void {
    const target = this.detailTarget;
    if (!target) {
      return;
    }

    this.loadDetail(target.ticketId);
  }

  toggleDropdown(ticketId: number, event: MouseEvent): void {
    event.stopPropagation();

    if (this.openDropdownId === ticketId) {
      this.openDropdownId = null;
      this.cdr.markForCheck();
      return;
    }

    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const dropdownHeight = 220;
    const spaceBelow = window.innerHeight - rect.bottom;
    const top = spaceBelow < dropdownHeight ? Math.max(8, rect.top - dropdownHeight - 4) : rect.bottom + 4;
    this.dropdownPos = { top, right: window.innerWidth - rect.right };
    this.openDropdownId = ticketId;
    this.cdr.markForCheck();
  }

  resolveTicket(ticket: TicketActionTarget, event?: Event): void {
    event?.stopPropagation();
    this.runTicketAction('resolve', ticket, this.noteForAction(ticket));
  }

  dismissTicket(ticket: TicketActionTarget, event?: Event): void {
    event?.stopPropagation();
    this.runTicketAction('dismiss', ticket, this.noteForAction(ticket));
  }

  reopenTicket(ticket: TicketActionTarget, event?: Event): void {
    event?.stopPropagation();
    this.runTicketAction('reopen', ticket, this.noteForAction(ticket));
  }

  saveInternalNote(ticket: AdminSupportTicketDetail): void {
    this.runTicketAction('saveNote', ticket, this.detailInternalNote);
  }

  hasTicketActions(ticket: TicketActionTarget): boolean {
    const permissions = ticket.permissions;
    return permissions.canResolve ||
      permissions.canDismiss ||
      permissions.canReopen;
  }

  isActionRunning(ticketId: number, action: TicketAction): boolean {
    return this.actionKey === this.actionKeyFor(ticketId, action);
  }

  isAnyActionRunning(ticketId: number): boolean {
    return this.actionKey?.startsWith(`${ticketId}:`) ?? false;
  }

  statusLabel(status: AdminSupportTicketStatus): string {
    return status;
  }

  statusClass(status: AdminSupportTicketStatus): string {
    switch (status) {
      case 'Resolved':
        return 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200';
      case 'Dismissed':
        return 'bg-gray-100 text-gray-600 ring-1 ring-gray-200';
      default:
        return 'bg-red-50 text-red-700 ring-1 ring-red-200';
    }
  }

  priorityClass(priority: AdminSupportTicketPriority): string {
    switch (priority) {
      case 'Critical':
        return 'bg-red-100 text-red-700 ring-1 ring-red-200';
      case 'High':
        return 'bg-amber-50 text-amber-700 ring-1 ring-amber-200';
      case 'Low':
        return 'bg-gray-100 text-gray-600 ring-1 ring-gray-200';
      default:
        return 'bg-blue-50 text-blue-700 ring-1 ring-blue-200';
    }
  }

  categoryClass(category: string): string {
    switch (category) {
      case 'account-access':
        return 'bg-blue-50 text-blue-700 ring-1 ring-blue-200';
      case 'location-and-maps':
        return 'bg-violet-50 text-violet-700 ring-1 ring-violet-200';
      case 'ask-agent':
        return 'bg-cyan-50 text-cyan-700 ring-1 ring-cyan-200';
      case 'events-and-stays':
        return 'bg-amber-50 text-amber-700 ring-1 ring-amber-200';
      case 'privacy-and-data':
        return 'bg-rose-50 text-rose-700 ring-1 ring-rose-200';
      default:
        return 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200';
    }
  }

  formatDate(value: string | null | undefined): string {
    return this.ticketsSvc.formatDate(value);
  }

  formatDatetime(value: string | null | undefined): string {
    return this.ticketsSvc.formatDatetime(value);
  }

  trackByTicketId(_: number, ticket: AdminSupportTicketListItem): number {
    return ticket.ticketId;
  }

  private openDetailFromRoute(ticketId: number): void {
    if (this.detailTarget?.ticketId === ticketId || this.detailTicket?.ticketId === ticketId) {
      return;
    }

    this.openDropdownId = null;
    this.detailTarget = {
      ticketId,
      referenceCode: `GT-${String(ticketId).padStart(6, '0')}`,
      reporter: {
        displayName: 'Support reporter',
        email: '',
        initials: 'SR',
        isAnonymous: true,
        avatarColor: '#64748b',
      },
      category: 'other',
      categoryLabel: 'Support request',
      subject: 'Support report',
      messagePreview: '',
      status: 'Open',
      priority: 'Normal',
      createdAt: '',
      hasAttachment: false,
      permissions: {
        canView: true,
        canResolve: false,
        canDismiss: false,
        canReopen: false,
        canAddNote: false,
      },
    };
    this.detailTicket = null;
    this.detailInternalNote = '';
    this.detailErrorMessage = '';
    this.loadDetail(ticketId);
    this.cdr.markForCheck();
  }

  private resetAndLoad(): void {
    this.nextSkip = null;
    this.hasMore = false;
    this.tickets = [];
    this.loadTickets(true);
  }

  private loadTickets(reset: boolean): void {
    const skip = reset ? 0 : this.nextSkip ?? this.tickets.length;
    const sequence = ++this.loadSequence;
    this.errorMessage = '';

    if (reset) {
      this.isLoading = true;
    } else {
      this.isLoadingMore = true;
    }

    this.ticketsSvc.getPage$({
      status: this.statusFilter,
      category: this.categoryFilter,
      priority: this.priorityFilter,
      search: this.searchQuery,
      sort: this.sort,
      skip,
      take: PAGE_SIZE,
    }).pipe(
      takeUntil(this.destroy$),
      finalize(() => {
        if (sequence === this.loadSequence) {
          this.isLoading = false;
          this.isLoadingMore = false;
          this.cdr.markForCheck();
        }
      }),
    ).subscribe({
      next: page => {
        if (sequence !== this.loadSequence) {
          return;
        }

        this.tickets = reset ? page.items : [...this.tickets, ...page.items];
        this.totalCount = page.totalCount;
        this.nextSkip = page.nextSkip;
        this.hasMore = page.hasMore;
        this.counts = page.counts;
        this.supportReportsCount = page.counts.openCount;
      },
      error: error => {
        if (sequence !== this.loadSequence) {
          return;
        }

        this.errorMessage = this.resolveErrorMessage(error);
      },
    });
  }

  private loadDetail(ticketId: number, showLoading = true): void {
    const sequence = ++this.detailLoadSequence;
    this.detailErrorMessage = '';

    if (showLoading) {
      this.isDetailLoading = true;
    }

    this.ticketsSvc.getDetail$(ticketId).pipe(
      takeUntil(this.destroy$),
      finalize(() => {
        if (sequence === this.detailLoadSequence) {
          this.isDetailLoading = false;
          this.cdr.markForCheck();
        }
      }),
    ).subscribe({
      next: ticket => {
        if (sequence !== this.detailLoadSequence) {
          return;
        }

        this.detailTicket = ticket;
        this.detailInternalNote = ticket.internalNote ?? '';
      },
      error: error => {
        if (sequence !== this.detailLoadSequence) {
          return;
        }

        this.detailErrorMessage = this.resolveErrorMessage(error);
      },
    });
  }

  private runTicketAction(action: TicketAction, ticket: TicketActionTarget, internalNote?: string | null): void {
    if (this.actionKey) {
      return;
    }

    const note = internalNote?.trim() || null;
    const ticketId = ticket.ticketId;
    this.actionKey = this.actionKeyFor(ticketId, action);
    this.openDropdownId = null;
    this.errorMessage = '';
    this.detailErrorMessage = '';

    this.actionRequest(action, ticketId, note).pipe(
      takeUntil(this.destroy$),
      finalize(() => {
        this.actionKey = null;
        this.cdr.markForCheck();
      }),
    ).subscribe({
      next: detail => {
        this.showSuccess(this.actionSuccessMessage(action));
        if (this.detailTarget?.ticketId === ticketId || this.detailTicket?.ticketId === ticketId) {
          this.detailTarget = detail;
          this.detailTicket = detail;
          this.detailInternalNote = detail.internalNote ?? '';
        }
        this.resetAndLoad();
      },
      error: error => {
        const message = this.resolveErrorMessage(error);
        this.errorMessage = message;
        if (this.detailTarget?.ticketId === ticketId || this.detailTicket?.ticketId === ticketId) {
          this.detailErrorMessage = message;
        }
      },
    });
  }

  private actionRequest(action: TicketAction, ticketId: number, internalNote: string | null): Observable<AdminSupportTicketDetail> {
    switch (action) {
      case 'resolve':
        return this.ticketsSvc.resolve$(ticketId, internalNote);
      case 'dismiss':
        return this.ticketsSvc.dismiss$(ticketId, internalNote);
      case 'reopen':
        return this.ticketsSvc.reopen$(ticketId, internalNote);
      case 'saveNote':
        return this.ticketsSvc.saveNote$(ticketId, internalNote ?? '');
    }
  }

  private noteForAction(ticket: TicketActionTarget): string | null {
    return this.detailTicket?.ticketId === ticket.ticketId ? this.detailInternalNote : null;
  }

  private actionKeyFor(ticketId: number, action: TicketAction): string {
    return `${ticketId}:${action}`;
  }

  private loadSupportReportsBadge(): void {
    this.dashboardSvc.loadDashboard()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: dashboard => {
          this.supportReportsCount = dashboard.badges.supportReportsCount;
          this.cdr.markForCheck();
        },
        error: () => {
          this.supportReportsCount = 0;
          this.cdr.markForCheck();
        },
      });
  }

  private showSuccess(message: string): void {
    if (this.successTimer) {
      clearTimeout(this.successTimer);
    }

    this.successMessage = message;
    this.successTimer = setTimeout(() => {
      this.successMessage = '';
      this.cdr.markForCheck();
    }, 3500);
  }

  private actionSuccessMessage(action: TicketAction): string {
    switch (action) {
      case 'resolve':
        return 'Support ticket resolved.';
      case 'dismiss':
        return 'Support ticket dismissed.';
      case 'reopen':
        return 'Support ticket reopened.';
      case 'saveNote':
        return 'Internal note saved.';
    }
  }

  private resolveErrorMessage(error: unknown): string {
    const status = typeof error === 'object' && error && 'status' in error ? (error as { status?: number }).status : 0;

    if (status === 401 || status === 403) {
      return 'Your administrator session cannot perform this action.';
    }
    if (status === 404) {
      return 'That support ticket could not be found.';
    }

    return 'Unable to load support tickets right now. Please try again.';
  }

  private toPositiveInt(value: unknown): number | null {
    const parsed = Number(value);
    return Number.isInteger(parsed) && parsed > 0 ? parsed : null;
  }
}
