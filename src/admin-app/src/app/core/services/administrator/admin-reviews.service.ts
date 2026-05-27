import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable, map, tap } from 'rxjs';
import {
  AdminReviewActionResult,
  AdminReviewAuthor,
  AdminReviewDetail,
  AdminReviewListCounts,
  AdminReviewListItem,
  AdminReviewListResponse,
  AdminReviewPermissions,
  AdminReviewReport,
  AdminReviewsPage,
  AdminReviewsPageRequest,
} from '../../models/administrator/admin-review.model';

const AVATAR_COLORS = ['#2563eb', '#059669', '#7c3aed', '#d97706', '#dc2626', '#0891b2', '#be185d', '#0f766e'];

@Injectable({ providedIn: 'root' })
export class AdminReviewsService {
  private readonly endpoint = '/api/admin/reviews';
  readonly lastPage$ = new BehaviorSubject<AdminReviewsPage | null>(null);

  constructor(private readonly http: HttpClient) {}

  getPage$(req: AdminReviewsPageRequest): Observable<AdminReviewsPage> {
    let params = new HttpParams()
      .set('status', req.status)
      .set('type', this.typeParam(req.type))
      .set('sort', req.sort)
      .set('skip', String(Math.max(0, req.skip)))
      .set('take', String(Math.max(1, req.take)));

    if (req.search.trim()) {
      params = params.set('search', req.search.trim());
    }

    return this.http.get<AdminReviewListResponse>(this.endpoint, { params }).pipe(
      map(response => this.normalizePage(response)),
      tap(page => this.lastPage$.next(page)),
    );
  }

  getDetail$(reviewKey: string): Observable<AdminReviewDetail> {
    return this.http
      .get<AdminReviewDetail>(`${this.endpoint}/${encodeURIComponent(reviewKey)}`)
      .pipe(map(detail => this.normalizeDetail(detail)));
  }

  deleteReview$(reviewKey: string): Observable<AdminReviewActionResult> {
    return this.http
      .post<AdminReviewActionResult>(`${this.endpoint}/${encodeURIComponent(reviewKey)}/delete`, {})
      .pipe(map(result => this.normalizeActionResult(result)));
  }

  removeAndSuspendAuthor$(reviewKey: string): Observable<AdminReviewActionResult> {
    return this.http
      .post<AdminReviewActionResult>(`${this.endpoint}/${encodeURIComponent(reviewKey)}/remove-and-suspend-author`, {})
      .pipe(map(result => this.normalizeActionResult(result)));
  }

  removeAndDeleteAuthor$(reviewKey: string): Observable<AdminReviewActionResult> {
    return this.http
      .post<AdminReviewActionResult>(`${this.endpoint}/${encodeURIComponent(reviewKey)}/remove-and-delete-author`, {})
      .pipe(map(result => this.normalizeActionResult(result)));
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

  private normalizePage(response: AdminReviewListResponse | null | undefined): AdminReviewsPage {
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
      items: (safe.items ?? []).map(review => this.normalizeListItem(review)),
      totalCount: this.toNumber(safe.totalCount),
      total: this.toNumber(safe.totalCount),
      skip: this.toNumber(safe.skip),
      take: this.toNumber(safe.take, 25),
      nextSkip: this.toNullableNumber(safe.nextSkip),
      hasMore: !!safe.hasMore,
      counts: this.normalizeCounts(safe.counts),
    };
  }

  private normalizeDetail(value: AdminReviewDetail): AdminReviewDetail {
    return {
      ...value,
      reviewKey: value.reviewKey || `${value.reviewedEntity?.entityType ?? 'Review'}:${value.reviewId}`,
      reviewId: this.toNumber(value.reviewId),
      author: this.normalizeAuthor(value.author),
      rating: this.toNumber(value.rating),
      maxRating: this.toNumber(value.maxRating, 5),
      content: value.content ?? '',
      status: this.normalizeReviewStatus(value.status),
      reviewedEntity: {
        entityId: this.toNumber(value.reviewedEntity?.entityId),
        entityType: value.reviewedEntity?.entityType || 'Review',
        name: value.reviewedEntity?.name || 'Unknown item',
        region: value.reviewedEntity?.region ?? null,
        location: value.reviewedEntity?.location ?? null,
        destination: value.reviewedEntity?.destination ?? null,
      },
      allReports: (value.allReports ?? []).map(report => this.normalizeReport(report)),
      reportSummary: {
        totalReports: this.toNumber(value.reportSummary?.totalReports),
        pendingReports: this.toNumber(value.reportSummary?.pendingReports),
        acceptedReports: this.toNumber(value.reportSummary?.acceptedReports),
        rejectedReports: this.toNumber(value.reportSummary?.rejectedReports),
      },
      moderationHistory: value.moderationHistory ?? [],
      permissions: this.normalizePermissions(value.permissions),
    };
  }

  private normalizeListItem(value: AdminReviewListItem): AdminReviewListItem {
    const reviewKey = value.reviewKey || `${value.reviewedEntity?.entityType ?? 'Review'}:${value.reviewId}`;

    return {
      ...value,
      reviewKey,
      reviewId: this.toNumber(value.reviewId),
      author: this.normalizeAuthor(value.author),
      rating: this.toNumber(value.rating),
      maxRating: this.toNumber(value.maxRating, 5),
      content: value.content ?? '',
      status: this.normalizeReviewStatus(value.status),
      reviewedEntity: {
        entityId: this.toNumber(value.reviewedEntity?.entityId),
        entityType: value.reviewedEntity?.entityType || 'Review',
        name: value.reviewedEntity?.name || 'Unknown item',
        region: value.reviewedEntity?.region ?? null,
        location: value.reviewedEntity?.location ?? null,
        destination: value.reviewedEntity?.destination ?? null,
      },
      reports: {
        reportCount: this.toNumber(value.reports?.reportCount),
        pendingReportCount: this.toNumber(value.reports?.pendingReportCount),
        latestReportReason: value.reports?.latestReportReason ?? null,
        latestReportCreatedAt: value.reports?.latestReportCreatedAt ?? null,
        latestReportStatus: value.reports?.latestReportStatus ?? null,
      },
      permissions: this.normalizePermissions(value.permissions),
    };
  }

  private normalizeAuthor(value: AdminReviewAuthor | null | undefined): AdminReviewAuthor {
    const safe = value ?? {
      userId: 0,
      displayName: 'Unknown user',
      email: '',
      initials: '?',
      status: 'Unknown' as const,
    };
    const userId = this.toNumber(safe.userId);
    const displayName = safe.displayName?.trim() || safe.email?.trim() || 'Unknown user';
    const initials = safe.initials?.trim() || this.buildInitials(displayName, safe.email ?? '');

    return {
      ...safe,
      userId,
      displayName,
      email: safe.email ?? '',
      avatarUrl: safe.avatarUrl ?? null,
      initials,
      status: safe.status === 'Suspended' || safe.status === 'Deleted' || safe.status === 'Unknown'
        ? safe.status
        : 'Active',
      avatarColor: safe.avatarColor || this.avatarColorFor(String(userId || safe.email || displayName)),
    };
  }

  private normalizeReport(value: AdminReviewReport): AdminReviewReport {
    return {
      ...value,
      reportId: this.toNumber(value.reportId),
      reporter: {
        userId: this.toNumber(value.reporter?.userId),
        displayName: value.reporter?.displayName || 'Unknown reporter',
      },
      reason: value.reason || 'Unspecified',
      details: value.details ?? null,
      status: value.status || 'Pending',
      decidedAt: value.decidedAt ?? null,
      decidedBy: value.decidedBy ?? null,
      decision: value.decision ?? null,
      moderatorNote: value.moderatorNote ?? null,
    };
  }

  private normalizePermissions(value: AdminReviewPermissions | null | undefined): AdminReviewPermissions {
    return {
      canDeleteReview: !!value?.canDeleteReview,
      canSuspendAuthor: !!value?.canSuspendAuthor,
      canUnsuspendAuthor: !!value?.canUnsuspendAuthor,
      canDeleteAuthor: !!value?.canDeleteAuthor,
      canRemoveReviewAndSuspendAuthor: !!value?.canRemoveReviewAndSuspendAuthor,
      canRemoveReviewAndDeleteAuthor: !!value?.canRemoveReviewAndDeleteAuthor,
    };
  }

  private normalizeActionResult(value: AdminReviewActionResult): AdminReviewActionResult {
    return {
      reviewKey: value.reviewKey ?? '',
      success: !!value.success,
      message: value.message || 'Action completed.',
      counts: this.normalizeCounts(value.counts),
    };
  }

  private normalizeCounts(value: AdminReviewListCounts | null | undefined): AdminReviewListCounts {
    return {
      reportedCount: this.toNumber(value?.reportedCount),
      activeCount: this.toNumber(value?.activeCount),
      allCount: this.toNumber(value?.allCount),
    };
  }

  private normalizeReviewStatus(status: unknown): AdminReviewListItem['status'] {
    return status === 'Reported' || status === 'Removed' || status === 'Hidden' || status === 'Deleted'
      ? status
      : 'Active';
  }

  private emptyCounts(): AdminReviewListCounts {
    return {
      reportedCount: 0,
      activeCount: 0,
      allCount: 0,
    };
  }

  private typeParam(types: AdminReviewsPageRequest['type']): string {
    const selected = (types ?? []).filter(type => type !== 'all');
    return selected.length === 0 ? 'all' : selected.join(',');
  }

  private toNumber(value: unknown, fallback = 0): number {
    return typeof value === 'number' && Number.isFinite(value) ? value : fallback;
  }

  private toNullableNumber(value: unknown): number | null {
    return typeof value === 'number' && Number.isFinite(value) ? value : null;
  }

  private avatarColorFor(seed: string): string {
    const hash = Array.from(seed).reduce((total, char) => total + char.charCodeAt(0), 0);
    return AVATAR_COLORS[hash % AVATAR_COLORS.length];
  }

  private buildInitials(name: string, email: string): string {
    const source = name.trim() || email.trim();
    const parts = source.split(/\s+/).filter(Boolean);
    if (parts.length >= 2) {
      return `${parts[0][0] ?? ''}${parts[1][0] ?? ''}`.toUpperCase();
    }

    return (source.slice(0, 2) || '?').toUpperCase();
  }
}
