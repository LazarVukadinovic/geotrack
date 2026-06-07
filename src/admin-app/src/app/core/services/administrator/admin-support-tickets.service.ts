import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable, map, tap } from 'rxjs';
import {
  AdminSupportTicketAdminSummary,
  AdminSupportTicketDetail,
  AdminSupportTicketListCounts,
  AdminSupportTicketListItem,
  AdminSupportTicketListResponse,
  AdminSupportTicketPageRequest,
  AdminSupportTicketPermissions,
  AdminSupportTicketPriority,
  AdminSupportTicketReporter,
  AdminSupportTicketStatus,
  AdminSupportTicketStatusUpdateRequest,
  AdminSupportTicketsPage,
} from '../../models/administrator/admin-support-ticket.model';
import { buildAvatarDisplayUrl } from '../../utils/avatar-url';

const AVATAR_COLORS = ['#2563eb', '#059669', '#7c3aed', '#d97706', '#dc2626', '#0891b2', '#be185d', '#0f766e'];

@Injectable({ providedIn: 'root' })
export class AdminSupportTicketsService {
  private readonly endpoint = '/api/admin/support-tickets';
  readonly lastPage$ = new BehaviorSubject<AdminSupportTicketsPage | null>(null);

  constructor(private readonly http: HttpClient) {}

  getPage$(req: AdminSupportTicketPageRequest): Observable<AdminSupportTicketsPage> {
    let params = new HttpParams()
      .set('status', req.status)
      .set('category', req.category)
      .set('priority', req.priority)
      .set('sort', req.sort)
      .set('skip', String(Math.max(0, req.skip)))
      .set('take', String(Math.max(1, req.take)));

    if (req.search.trim()) {
      params = params.set('search', req.search.trim());
    }

    return this.http.get<AdminSupportTicketListResponse>(this.endpoint, { params }).pipe(
      map(response => this.normalizePage(response)),
      tap(page => this.lastPage$.next(page)),
    );
  }

  getDetail$(ticketId: number): Observable<AdminSupportTicketDetail> {
    return this.http
      .get<AdminSupportTicketDetail>(`${this.endpoint}/${encodeURIComponent(String(ticketId))}`)
      .pipe(map(ticket => this.normalizeDetail(ticket)));
  }

  updateStatus$(ticketId: number, request: AdminSupportTicketStatusUpdateRequest): Observable<AdminSupportTicketDetail> {
    return this.http
      .post<AdminSupportTicketDetail>(`${this.endpoint}/${encodeURIComponent(String(ticketId))}/status`, request)
      .pipe(map(ticket => this.normalizeDetail(ticket)));
  }

  markInProgress$(ticketId: number, internalNote?: string | null): Observable<AdminSupportTicketDetail> {
    return this.updateStatus$(ticketId, { status: 'InProgress', internalNote });
  }

  resolve$(ticketId: number, internalNote?: string | null): Observable<AdminSupportTicketDetail> {
    return this.updateStatus$(ticketId, { status: 'Resolved', internalNote });
  }

  dismiss$(ticketId: number, internalNote?: string | null): Observable<AdminSupportTicketDetail> {
    return this.updateStatus$(ticketId, { status: 'Dismissed', internalNote });
  }

  reopen$(ticketId: number, internalNote?: string | null): Observable<AdminSupportTicketDetail> {
    return this.updateStatus$(ticketId, { status: 'Open', internalNote });
  }

  saveNote$(ticketId: number, internalNote: string): Observable<AdminSupportTicketDetail> {
    return this.http
      .post<AdminSupportTicketDetail>(`${this.endpoint}/${encodeURIComponent(String(ticketId))}/note`, { internalNote })
      .pipe(map(ticket => this.normalizeDetail(ticket)));
  }

  formatDate(value: string | null | undefined): string {
    if (!value) {
      return '';
    }

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return value;
    }

    return date.toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    });
  }

  formatDatetime(value: string | null | undefined): string {
    if (!value) {
      return '';
    }

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return value;
    }

    return date.toLocaleString('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  private normalizePage(response: AdminSupportTicketListResponse | null | undefined): AdminSupportTicketsPage {
    const safe = response ?? {
      items: [],
      totalCount: 0,
      skip: 0,
      take: 25,
      nextSkip: null,
      hasMore: false,
      counts: this.emptyCounts(),
    };

    return {
      items: (safe.items ?? []).map(ticket => this.normalizeListItem(ticket)),
      totalCount: this.toNumber(safe.totalCount),
      total: this.toNumber(safe.totalCount),
      skip: this.toNumber(safe.skip),
      take: this.toNumber(safe.take, 25),
      nextSkip: this.toNullableNumber(safe.nextSkip),
      hasMore: !!safe.hasMore,
      counts: this.normalizeCounts(safe.counts),
    };
  }

  private normalizeDetail(value: AdminSupportTicketDetail): AdminSupportTicketDetail {
    const listItem = this.normalizeListItem(value);

    return {
      ...value,
      ...listItem,
      message: value.message ?? '',
      userAgent: value.userAgent ?? null,
      deviceInfo: value.deviceInfo ?? null,
      browser: value.browser ?? null,
      platform: value.platform ?? null,
      attachmentUrl: value.attachmentUrl ?? null,
      resolvedBy: this.normalizeAdminSummary(value.resolvedBy),
      assignedAdmin: this.normalizeAdminSummary(value.assignedAdmin),
      internalNote: value.internalNote ?? null,
      resolutionMessage: value.resolutionMessage ?? null,
      statusHistory: (value.statusHistory ?? []).map(item => ({
        action: item.action || 'Updated',
        actor: this.normalizeAdminSummary(item.actor),
        createdAt: item.createdAt ?? '',
        note: item.note ?? null,
      })),
    };
  }

  private normalizeListItem(value: AdminSupportTicketListItem): AdminSupportTicketListItem {
    const ticketId = this.toNumber(value.ticketId);

    return {
      ...value,
      ticketId,
      referenceCode: value.referenceCode || `GT-${String(ticketId).padStart(6, '0')}`,
      reporter: this.normalizeReporter(value.reporter),
      category: value.category || 'other',
      categoryLabel: value.categoryLabel || this.categoryLabel(value.category),
      subject: value.subject || 'Support request',
      messagePreview: value.messagePreview ?? '',
      status: this.normalizeStatus(value.status),
      priority: this.normalizePriority(value.priority),
      pageUrl: value.pageUrl ?? value.appRoute ?? null,
      appRoute: value.appRoute ?? value.pageUrl ?? null,
      createdAt: value.createdAt ?? '',
      updatedAt: value.updatedAt ?? null,
      resolvedAt: value.resolvedAt ?? null,
      hasAttachment: !!value.hasAttachment,
      internalNotePreview: value.internalNotePreview ?? null,
      permissions: this.normalizePermissions(value.permissions),
    };
  }

  private normalizeReporter(value: AdminSupportTicketReporter | null | undefined): AdminSupportTicketReporter {
    const safe = value ?? {
      displayName: 'Anonymous user',
      email: '',
      initials: 'AU',
      isAnonymous: true,
    };
    const displayName = safe.displayName?.trim() || safe.email?.trim() || 'Anonymous user';
    const initials = safe.initials?.trim() || this.buildInitials(displayName, safe.email ?? '');

    return {
      userId: this.toNullableNumber(safe.userId),
      displayName,
      email: safe.email ?? '',
      avatarUrl: buildAvatarDisplayUrl(safe.avatarUrl, null) || null,
      initials,
      isAnonymous: !!safe.isAnonymous,
      avatarColor: safe.avatarColor || this.avatarColorFor(String(safe.userId || safe.email || displayName)),
    };
  }

  private normalizeAdminSummary(value: AdminSupportTicketAdminSummary | null | undefined): AdminSupportTicketAdminSummary | null {
    if (!value) {
      return null;
    }

    return {
      userId: this.toNumber(value.userId),
      displayName: value.displayName?.trim() || value.email?.trim() || 'Administrator',
      email: value.email ?? '',
    };
  }

  private normalizePermissions(value: AdminSupportTicketPermissions | null | undefined): AdminSupportTicketPermissions {
    return {
      canView: value?.canView !== false,
      canMarkInProgress: !!value?.canMarkInProgress,
      canResolve: !!value?.canResolve,
      canDismiss: !!value?.canDismiss,
      canReopen: !!value?.canReopen,
      canAddNote: !!value?.canAddNote,
    };
  }

  private normalizeCounts(value: AdminSupportTicketListCounts | null | undefined): AdminSupportTicketListCounts {
    return {
      openCount: this.toNumber(value?.openCount),
      inProgressCount: this.toNumber(value?.inProgressCount),
      resolvedCount: this.toNumber(value?.resolvedCount),
      dismissedCount: this.toNumber(value?.dismissedCount),
      allCount: this.toNumber(value?.allCount),
    };
  }

  private emptyCounts(): AdminSupportTicketListCounts {
    return {
      openCount: 0,
      inProgressCount: 0,
      resolvedCount: 0,
      dismissedCount: 0,
      allCount: 0,
    };
  }

  private normalizeStatus(value: unknown): AdminSupportTicketStatus {
    switch (String(value ?? '').trim()) {
      case 'InProgress':
        return 'InProgress';
      case 'Resolved':
        return 'Resolved';
      case 'Dismissed':
        return 'Dismissed';
      default:
        return 'Open';
    }
  }

  private normalizePriority(value: unknown): AdminSupportTicketPriority {
    switch (String(value ?? '').trim()) {
      case 'Low':
        return 'Low';
      case 'High':
        return 'High';
      case 'Critical':
        return 'Critical';
      default:
        return 'Normal';
    }
  }

  private categoryLabel(value: string | null | undefined): string {
    switch (value) {
      case 'account-access':
        return 'Account access';
      case 'saved-places-and-collections':
        return 'Saved places';
      case 'location-and-maps':
        return 'Location and maps';
      case 'ask-agent':
        return 'Ask Agent';
      case 'events-and-stays':
        return 'Events and stays';
      case 'privacy-and-data':
        return 'Privacy and data';
      default:
        return 'Support request';
    }
  }

  private buildInitials(name: string, email: string): string {
    const source = name.trim() || email.trim();
    const parts = source.split(' ').filter(Boolean);

    if (parts.length >= 2) {
      return `${parts[0][0]}${parts[1][0]}`.toUpperCase();
    }

    const compact = source.replace(/[^a-z0-9]/gi, '').slice(0, 2).toUpperCase();
    return compact || '?';
  }

  private avatarColorFor(seed: string): string {
    let hash = 0;
    for (let index = 0; index < seed.length; index++) {
      hash = (hash * 31 + seed.charCodeAt(index)) | 0;
    }
    return AVATAR_COLORS[Math.abs(hash) % AVATAR_COLORS.length];
  }

  private toNumber(value: unknown, fallback = 0): number {
    return typeof value === 'number' && Number.isFinite(value) ? value : fallback;
  }

  private toNullableNumber(value: unknown): number | null {
    return typeof value === 'number' && Number.isFinite(value) ? value : null;
  }
}
