import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { SidebarComponent } from '../../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../../shared/components/topbar/topbar.component';
import { RegionsService, RegionDetail, RegionTeamMember } from '../../../../../core/services/national/regions.service';

@Component({
  selector: 'app-region-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './region-detail.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegionDetailComponent implements OnInit, OnDestroy {

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private svc: RegionsService,
    private cdr: ChangeDetectorRef,
  ) {}

  private destroy$ = new Subject<void>();

  userName = 'Sarah Jenkins';
  userRole = 'National Manager';
  isViewMode = true;

  region: RegionDetail | null = null;
  isLoading = true;
  isSaving  = false;

  editName        = '';
  editRefId       = '';
  editSummary     = '';
  editDescription = '';

  showAssignModal = false;
  newUserName  = '';
  newUserEmail = '';
  newUserRole  = 'Regional Admin';

  removeTarget: RegionTeamMember | null = null;
  isRemoving = false;

  ngOnInit(): void {
    const state = history.state as { editMode?: boolean } | undefined;
    this.isViewMode = !(state?.editMode === true);

    const id = this.route.snapshot.paramMap.get('id') ?? '';

    this.svc.getDetail$(id)
      .pipe(takeUntil(this.destroy$))
      .subscribe(detail => {
        this.region    = detail;
        this.isLoading = false;
        this.syncEditFields();
        this.cdr.markForCheck();
      });
  }

  ngOnDestroy(): void { this.destroy$.next(); this.destroy$.complete(); }

  private syncEditFields(): void {
    if (!this.region) return;
    this.editName        = this.region.name;
    this.editRefId       = this.region.internalRefId;
    this.editSummary     = this.region.shortSummary;
    this.editDescription = this.region.fullDescription;
  }
  formatDate(dateStr: string): string {
    if (!dateStr) return '—';
    const d = new Date(dateStr);
    if (isNaN(d.getTime())) return dateStr;
    return d.toLocaleDateString('en-GB', { day: '2-digit', month: 'long', year: 'numeric' });
  }

  switchToEdit(): void {
    this.isViewMode = false;
    this.syncEditFields();
    this.cdr.markForCheck();
  }

  discardChanges(): void {
    this.syncEditFields();
    this.isViewMode = true;
    this.cdr.markForCheck();
  }

  saveChanges(): void {
    if (!this.region) return;
    this.isSaving = true;

    const patch: Partial<RegionDetail> = {
      name:            this.editName,
      internalRefId:   this.editRefId,
      shortSummary:    this.editSummary,
      fullDescription: this.editDescription,
      lastUpdated: new Date().toLocaleDateString('en-GB', {
        day: '2-digit', month: 'long', year: 'numeric',
      }),
    };

    this.svc.updateDetail$(this.region.id, patch)
      .pipe(takeUntil(this.destroy$))
      .subscribe(updated => {
        if (updated) this.region = updated;
        this.isSaving   = false;
        this.isViewMode = true;
        this.cdr.markForCheck();
      });
  }

  goBack(): void { this.router.navigate(['/regions']); }

  openAssignModal(): void { this.showAssignModal = true; this.cdr.markForCheck(); }

  closeAssignModal(): void {
    this.showAssignModal = false;
    this.newUserName  = '';
    this.newUserEmail = '';
    this.newUserRole  = 'Regional Admin';
    this.cdr.markForCheck();
  }

  assignUser(): void {
    if (!this.region || !this.newUserName || !this.newUserEmail) return;

    const initials  = this.newUserName.split(' ').map(w => w[0]).join('').toUpperCase().slice(0, 2);
    const colors    = ['#3b82f6', '#8b5cf6', '#10b981', '#f59e0b', '#ef4444'];
    const newMember: RegionTeamMember = {
      id:           'u' + Date.now(),
      name:         this.newUserName,
      email:        this.newUserEmail,
      initials,
      avatarColor:  colors[this.region.team.length % colors.length],
      role:         this.newUserRole,
      status:       'Active',
      assignedDate: new Date().toLocaleDateString('en-GB', {
        day: '2-digit', month: 'long', year: 'numeric',
      }),
    };

    const updatedTeam = [...this.region.team, newMember];

    this.svc.updateTeam$(this.region.id, updatedTeam)
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        this.region!.team = updatedTeam;
        this.closeAssignModal();
        this.cdr.markForCheck();
      });
  }

  promptRemove(member: RegionTeamMember): void {
    this.removeTarget = member;
    this.cdr.markForCheck();
  }

  cancelRemove(): void { this.removeTarget = null; this.cdr.markForCheck(); }

  confirmRemove(): void {
    if (!this.region || !this.removeTarget) return;
    this.isRemoving = true;

    const updatedTeam = this.region.team.filter(m => m.id !== this.removeTarget!.id);

    this.svc.updateTeam$(this.region.id, updatedTeam)
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        this.region!.team = updatedTeam;
        this.removeTarget = null;
        this.isRemoving   = false;
        this.cdr.markForCheck();
      });
  }

  statusClass(s: string): string {
    return s === 'Active'
      ? 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200'
      : 'bg-gray-100 text-gray-500 ring-1 ring-gray-200';
  }

  get scoreColor(): string {
    const s = this.region?.activityScore ?? 0;
    if (s >= 85) return '#10b981';
    if (s >= 60) return '#f59e0b';
    return '#ef4444';
  }
}