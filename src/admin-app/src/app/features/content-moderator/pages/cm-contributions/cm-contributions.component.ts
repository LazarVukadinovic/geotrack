import { ChangeDetectionStrategy, ChangeDetectorRef, Component, HostListener, OnDestroy, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subject, finalize, forkJoin, takeUntil } from 'rxjs';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { AuthService } from '../../../../core/services/auth.service';
import {
  ContentModeratorContributionsService,
  CmContributionListItem,
  CmRegionOption,
  CmDestinationOption,
  CmLocationOption,
} from '../../../../core/services/content-moderator/content-moderator-contributions.service';

type TabKey = 'all' | 'pending' | 'approved' | 'rejected';
type SortKey = 'newest' | 'oldest' | 'name-asc' | 'name-desc';
type DestinationChoice = 'existing' | 'new';

@Component({
  selector: 'app-cm-contributions',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './cm-contributions.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CmContributionsComponent implements OnInit, OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly service = inject(ContentModeratorContributionsService);
  private readonly router = inject(Router);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly destroy$ = new Subject<void>();

  // ── List state ──────────────────────────────────────────────
  items: CmContributionListItem[] = [];
  isLoading = false;
  errorMessage: string | null = null;
  processingId: number | null = null;
  feedbackMessage: string | null = null;
  private feedbackTimeout: ReturnType<typeof setTimeout> | null = null;
  private readonly failedImages = new Set<number>();

  activeTab: TabKey = 'pending';
  searchQuery = '';
  currentSort: SortKey = 'newest';
  sortOpen = false;

  readonly tabs: { key: TabKey; label: string }[] = [
    { key: 'all',      label: 'All'      },
    { key: 'pending',  label: 'Pending'  },
    { key: 'approved', label: 'Approved' },
    { key: 'rejected', label: 'Rejected' },
  ];

  readonly sortOptions: { value: SortKey; label: string }[] = [
    { value: 'newest',    label: 'Newest first' },
    { value: 'oldest',    label: 'Oldest first' },
    { value: 'name-asc',  label: 'Name A–Z'     },
    { value: 'name-desc', label: 'Name Z–A'     },
  ];

  // ── Reject modal ─────────────────────────────────────────────
  rejectModal: { id: number; name: string } | null = null;
  rejectNote = '';

  // ── Approve modal ─────────────────────────────────────────────
  approveModal: CmContributionListItem | null = null;
  approveModalLoading = false;
  approveNote = '';

  // Region/destination assignment (approve modal)
  regions: CmRegionOption[] = [];
  regionsLoading = false;
  selectedRegionId: number | null = null;
  regionSearch = '';

  destinations: CmDestinationOption[] = [];
  destinationsLoading = false;
  destinationChoice: DestinationChoice = 'new';
  selectedDestinationId: number | null = null;
  newDestinationName = '';

  locations: CmLocationOption[] = [];
  locationsLoading = false;
  newLocationName = '';

  destinationImageFile: File | null = null;
  destinationImagePreview: string | null = null;
  locationImageFile: File | null = null;
  locationImagePreview: string | null = null;

  // ── Computed ─────────────────────────────────────────────────
  get userName(): string { return this.auth.fullName || 'Content Moderator'; }
  get userRole(): string { return this.auth.getUser()?.role || 'Content Moderator'; }

  get currentSortLabel(): string {
    return this.sortOptions.find(o => o.value === this.currentSort)?.label ?? 'Newest first';
  }

  get selectedRegion(): CmRegionOption | null {
    return this.regions.find(r => r.id === this.selectedRegionId) ?? null;
  }

  get filteredRegions(): CmRegionOption[] {
    const term = this.regionSearch.trim().toLowerCase();
    if (!term) return [];
    return this.regions.filter(r => this.regionDisplayName(r).toLowerCase().includes(term)).slice(0, 8);
  }

  get filteredLocations(): CmLocationOption[] {
    const term = this.newLocationName.trim().toLowerCase();
    const list = term
      ? this.locations.filter(l => l.name.toLowerCase().includes(term) && l.name.toLowerCase() !== term)
      : this.locations;
    return list.slice(0, 8);
  }

  get canConfirmApprove(): boolean {
    if (!this.approveModal) return false;
    if (!this.selectedRegionId) return true; // approve without region assignment
    if (!this.newLocationName.trim()) return false;
    if (this.destinationChoice === 'existing') return !!this.selectedDestinationId;
    return !!this.newDestinationName.trim();
  }

  tabCount(key: TabKey): number {
    if (key === 'all') return this.items.length;
    const status = this.tabStatus(key);
    return this.items.filter(i => i.status === status).length;
  }

  private tabStatus(key: TabKey): string {
    switch (key) {
      case 'pending':  return 'PendingReview';
      case 'approved': return 'Approved';
      case 'rejected': return 'Rejected';
      default:         return '';
    }
  }

  get filteredItems(): CmContributionListItem[] {
    let result = this.items;

    if (this.activeTab !== 'all') {
      const status = this.tabStatus(this.activeTab);
      result = result.filter(i => i.status === status);
    }

    const q = this.searchQuery.trim().toLowerCase();
    if (q) result = result.filter(i => i.name.toLowerCase().includes(q));

    switch (this.currentSort) {
      case 'oldest':    return [...result].sort((a, b) => a.submittedAt.localeCompare(b.submittedAt));
      case 'name-asc':  return [...result].sort((a, b) => a.name.localeCompare(b.name));
      case 'name-desc': return [...result].sort((a, b) => b.name.localeCompare(a.name));
      default:          return [...result].sort((a, b) => b.submittedAt.localeCompare(a.submittedAt));
    }
  }

  // ── Lifecycle ─────────────────────────────────────────────────
  ngOnInit(): void {
    const state = history.state as { feedbackMessage?: unknown } | null;
    const msg = typeof state?.feedbackMessage === 'string' ? state.feedbackMessage.trim() : '';
    if (msg) this.feedbackMessage = msg;
    this.loadContributions();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
    if (this.feedbackTimeout) clearTimeout(this.feedbackTimeout);
  }

  @HostListener('document:click')
  closeSortDropdown(): void {
    if (this.sortOpen) { this.sortOpen = false; this.cdr.markForCheck(); }
  }

  // ── List actions ──────────────────────────────────────────────
  loadContributions(): void {
    this.isLoading = true;
    this.errorMessage = null;
    this.cdr.markForCheck();

    this.service.getAll()
      .pipe(takeUntil(this.destroy$), finalize(() => { this.isLoading = false; this.cdr.markForCheck(); }))
      .subscribe({
        next: items => { this.items = items; this.cdr.markForCheck(); },
        error: () => { this.errorMessage = 'Unable to load contributions for review.'; this.cdr.markForCheck(); },
      });
  }

  selectTab(key: TabKey): void { this.activeTab = key; this.cdr.markForCheck(); }
  setSort(value: SortKey): void { this.currentSort = value; this.sortOpen = false; this.cdr.markForCheck(); }
  onSearchChange(): void { this.cdr.markForCheck(); }

  viewDetail(item: CmContributionListItem): void {
    this.router.navigate(['/content-moderator/contributions', item.id]);
  }

  // ── Approve modal ─────────────────────────────────────────────
  openApprove(item: CmContributionListItem, event: Event): void {
    event.stopPropagation();
    this.approveModal = item;
    this.approveNote = '';
    this.approveModalLoading = true;
    this.resetAssignmentForm();
    this.cdr.markForCheck();

    // Fetch full detail to get suggestion fields (suggestedRegionId, suggestedDestinationName, etc.)
    this.service.getById(item.id)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: detail => {
          this.newDestinationName = detail.suggestedDestinationName || detail.city || '';
          this.newLocationName = detail.suggestedLocationName || '';
          this.approveModalLoading = false;
          this.cdr.markForCheck();
          this.loadRegions(detail.suggestedRegionId ?? null);
        },
        error: () => {
          // fallback: open without prefill
          this.approveModalLoading = false;
          this.cdr.markForCheck();
          this.loadRegions(null);
        },
      });
  }

  private resetAssignmentForm(): void {
    this.regions = [];
    this.selectedRegionId = null;
    this.regionSearch = '';
    this.destinations = [];
    this.destinationChoice = 'new';
    this.selectedDestinationId = null;
    this.newDestinationName = '';
    this.locations = [];
    this.newLocationName = '';
    this.destinationImageFile = null;
    this.destinationImagePreview = null;
    this.locationImageFile = null;
    this.locationImagePreview = null;
  }

  onDestinationImageSelected(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    (event.target as HTMLInputElement).value = '';
    if (!file) return;
    this.destinationImageFile = file;
    const reader = new FileReader();
    reader.onload = () => { this.destinationImagePreview = reader.result as string; this.cdr.markForCheck(); };
    reader.readAsDataURL(file);
  }

  clearDestinationImage(): void {
    this.destinationImageFile = null;
    this.destinationImagePreview = null;
    this.cdr.markForCheck();
  }

  onLocationImageSelected(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    (event.target as HTMLInputElement).value = '';
    if (!file) return;
    this.locationImageFile = file;
    const reader = new FileReader();
    reader.onload = () => { this.locationImagePreview = reader.result as string; this.cdr.markForCheck(); };
    reader.readAsDataURL(file);
  }

  clearLocationImage(): void {
    this.locationImageFile = null;
    this.locationImagePreview = null;
    this.cdr.markForCheck();
  }

  private loadRegions(suggestedRegionId: number | null): void {
    this.regionsLoading = true;
    this.cdr.markForCheck();

    this.service.getRegionOptions()
      .pipe(takeUntil(this.destroy$), finalize(() => { this.regionsLoading = false; this.cdr.markForCheck(); }))
      .subscribe({
        next: regions => {
          this.regions = regions;
          // Auto-select suggested region exactly like the detail page does
          if (suggestedRegionId && !this.selectedRegionId) {
            const suggested = regions.find(r => r.id === suggestedRegionId);
            if (suggested) this.selectRegion(suggested);
          }
          this.cdr.markForCheck();
        },
        error: () => { this.cdr.markForCheck(); },
      });
  }

  selectRegion(region: CmRegionOption): void {
    this.selectedRegionId = region.id;
    this.regionSearch = this.regionDisplayName(region);
    this.destinations = [];
    this.destinationChoice = 'new';
    this.selectedDestinationId = null;
    this.locations = [];
    this.cdr.markForCheck();

    this.destinationsLoading = true;
    this.service.getDestinationsForRegion(region.id)
      .pipe(takeUntil(this.destroy$), finalize(() => { this.destinationsLoading = false; this.cdr.markForCheck(); }))
      .subscribe({
        next: destinations => { this.destinations = destinations; this.cdr.markForCheck(); },
        error: () => { this.cdr.markForCheck(); },
      });
  }

  clearRegionSelection(): void {
    this.selectedRegionId = null;
    this.regionSearch = '';
    this.destinations = [];
    this.locations = [];
    this.selectedDestinationId = null;
    this.destinationChoice = 'new';
    this.cdr.markForCheck();
  }

  onDestinationChoiceChanged(choice: DestinationChoice): void {
    this.destinationChoice = choice;
    this.selectedDestinationId = null;
    this.locations = [];
    this.cdr.markForCheck();
  }

  onDestinationSelected(): void {
    this.locations = [];
    if (!this.selectedDestinationId) { this.cdr.markForCheck(); return; }
    this.locationsLoading = true;
    this.cdr.markForCheck();

    this.service.getLocationsForDestination(this.selectedDestinationId)
      .pipe(takeUntil(this.destroy$), finalize(() => { this.locationsLoading = false; this.cdr.markForCheck(); }))
      .subscribe({
        next: locations => { this.locations = locations; this.cdr.markForCheck(); },
        error: () => { this.cdr.markForCheck(); },
      });
  }

  selectLocation(location: CmLocationOption): void {
    this.newLocationName = location.name;
    this.cdr.markForCheck();
  }

  cancelApprove(): void {
    this.approveModal = null;
    this.resetAssignmentForm();
    this.cdr.markForCheck();
  }

  confirmApprove(): void {
    if (!this.approveModal || this.processingId !== null || !this.canConfirmApprove) return;
    const item = this.approveModal;
    this.approveModal = null;
    this.processingId = item.id;
    this.cdr.markForCheck();

    const destImage = this.destinationImageFile;
    const locImage = this.locationImageFile;

    const obs = this.selectedRegionId
      ? this.service.approveWithRegion(
          item.id,
          this.selectedRegionId,
          this.destinationChoice === 'existing' ? this.selectedDestinationId : null,
          this.destinationChoice === 'new' ? this.newDestinationName.trim() : null,
          this.newLocationName.trim(),
          this.approveNote,
        )
      : this.service.approve(item.id, this.approveNote);

    obs.pipe(finalize(() => { this.processingId = null; this.cdr.markForCheck(); }))
      .subscribe({
        next: (updated) => {
          const idx = this.items.findIndex(i => i.id === item.id);
          if (idx !== -1) {
            this.items = [
              ...this.items.slice(0, idx),
              { ...this.items[idx], status: updated.status },
              ...this.items.slice(idx + 1),
            ];
            this.cdr.markForCheck();
          }

          // Upload card images if provided
          const uploads: ReturnType<typeof this.service.uploadDestinationCardImage>[] = [];
          if (updated.createdDestinationId && destImage)
            uploads.push(this.service.uploadDestinationCardImage(updated.createdDestinationId, destImage));
          if (updated.createdLocationId && locImage)
            uploads.push(this.service.uploadLocationCardImage(updated.createdLocationId, locImage));

          if (uploads.length > 0) {
            forkJoin(uploads).subscribe(); // fire-and-forget, not critical
          }

          this.showFeedback('Contribution approved.');
        },
        error: (err) => {
          this.errorMessage = err?.error?.message || 'Unable to approve this contribution.';
          this.cdr.markForCheck();
        },
      });
  }

  // ── Reject modal ──────────────────────────────────────────────
  openReject(item: CmContributionListItem, event: Event): void {
    event.stopPropagation();
    this.rejectNote = '';
    this.rejectModal = { id: item.id, name: item.name };
    this.cdr.markForCheck();
  }

  confirmReject(): void {
    if (!this.rejectModal || this.processingId !== null) return;
    const id = this.rejectModal.id;
    const note = this.rejectNote;
    this.rejectModal = null;
    this.processingId = id;
    this.cdr.markForCheck();

    this.service.reject(id, note)
      .pipe(finalize(() => { this.processingId = null; this.cdr.markForCheck(); }))
      .subscribe({
        next: (updated) => {
          const idx = this.items.findIndex(i => i.id === id);
          if (idx !== -1) {
            this.items = [
              ...this.items.slice(0, idx),
              { ...this.items[idx], status: updated.status },
              ...this.items.slice(idx + 1),
            ];
            this.cdr.markForCheck();
          }
          this.showFeedback('Contribution rejected.');
        },
        error: (err) => {
          this.errorMessage = err?.error?.message || 'Unable to reject this contribution.';
          this.cdr.markForCheck();
        },
      });
  }

  cancelReject(): void {
    this.rejectModal = null;
    this.rejectNote = '';
    this.cdr.markForCheck();
  }

  // ── Helpers ───────────────────────────────────────────────────
  hasThumbnail(item: CmContributionListItem): boolean {
    return !!item.cardImageUrl && !this.failedImages.has(item.id);
  }

  onImageError(item: CmContributionListItem): void {
    this.failedImages.add(item.id);
    this.cdr.markForCheck();
  }

  typeLabel(type: string): string {
    switch ((type ?? '').toLowerCase()) {
      case 'updateplace':    return 'Update Place';
      case 'claimownership': return 'Claim Ownership';
      default:               return 'Place';
    }
  }

  typeBadgeClass(type: string): string {
    switch ((type ?? '').toLowerCase()) {
      case 'updateplace':    return 'bg-purple-50 text-purple-700 border border-purple-100';
      case 'claimownership': return 'bg-orange-50 text-orange-700 border border-orange-100';
      default:               return 'bg-blue-50 text-blue-700 border border-blue-100';
    }
  }

  statusClass(status: string): string {
    switch (status) {
      case 'Approved': return 'text-green-600';
      case 'Rejected': return 'text-red-500';
      default:         return 'text-amber-600';
    }
  }

  locationSummary(item: CmContributionListItem): string {
    return item.location || 'Location outside mapped regions';
  }

  regionDisplayName(region: CmRegionOption): string {
    return region.territoryName ? `${region.name} (${region.territoryName})` : region.name;
  }

  private showFeedback(msg: string): void {
    this.feedbackMessage = msg;
    if (this.feedbackTimeout) clearTimeout(this.feedbackTimeout);
    this.feedbackTimeout = setTimeout(() => {
      this.feedbackMessage = null;
      this.cdr.markForCheck();
    }, 4000);
    this.cdr.markForCheck();
  }
}
