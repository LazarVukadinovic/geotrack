import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { SidebarComponent } from '../../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../../shared/components/topbar/topbar.component';
import { RegionsService, RegionDetail, RegionTeamMember, buildDetail, RegionListItem } from '../../../../../core/services/national/regions.service';
import { UsersService } from '../../../../../core/services/national/users.service';
import { PlatformUser } from '../../../../../core/models/platform-user.model';

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
    private usersSvc: UsersService,
    private cdr: ChangeDetectorRef,
  ) {}

  private destroy$ = new Subject<void>();
  private emailDropdownTimer: ReturnType<typeof setTimeout> | null = null;
  private nameDropdownTimer:  ReturnType<typeof setTimeout> | null = null;

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

  // Assign user modal
  showAssignModal   = false;
  newUserName       = '';
  newUserEmail      = '';
  newUserRole       = 'Viewer';
  allUsers:          PlatformUser[] = [];
  filteredByEmail:   PlatformUser[] = [];
  filteredByName:    PlatformUser[] = [];
  showEmailDropdown = false;
  showNameDropdown  = false;

  // Remove member
  removeTarget: RegionTeamMember | null = null;
  isRemoving = false;

  // Edit / view member modal
  editMember:       PlatformUser | null = null;
  isMemberReadOnly  = false;
  isTogglingRole    = false;

  ngOnInit(): void {
    const state = history.state as { region?: RegionListItem; editMode?: boolean } | undefined;
    this.isViewMode = !(state?.editMode === true);

    if (state?.region) {
      this.region = buildDetail(state.region);
      this.isLoading = false;
      this.syncEditFields();
      this.cdr.markForCheck();
    }

    const id = this.route.snapshot.paramMap.get('id') ?? '';

    this.svc.getDetail$(id)
      .pipe(takeUntil(this.destroy$))
      .subscribe(detail => {
        if (detail) {
          this.region = detail;
          this.syncEditFields();
          this.cdr.markForCheck();
        }
        this.isLoading = false;
        this.cdr.markForCheck();
      });

    this.usersSvc.getPage$({ pageSize: 1000, search: '' })
      .pipe(takeUntil(this.destroy$))
      .subscribe(page => {
        this.allUsers        = page.items;
        this.filteredByEmail = [...this.allUsers];
        this.filteredByName  = [...this.allUsers];
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

    const patch: Partial<RegionDetail> = {
      name:            this.editName,
      internalRefId:   this.editRefId,
      shortSummary:    this.editSummary,
      fullDescription: this.editDescription,
      lastUpdated: new Date().toLocaleDateString('en-GB', {
        day: '2-digit', month: 'long', year: 'numeric',
      }),
    };

    Object.assign(this.region, patch);
    this.isViewMode = true;
    this.cdr.markForCheck();

    this.svc.updateDetail$(this.region.id, patch).pipe(takeUntil(this.destroy$)).subscribe();
  }

  cancelEdit(): void {
    this.isViewMode = true;
    this.cdr.markForCheck();
  }

  goBack(): void { this.router.navigate(['/regions']); }

  // ── Assign User Modal ────────────────────────────────────────────

  openAssignModal(): void {
    this.newUserName       = '';
    this.newUserEmail      = '';
    this.newUserRole       = 'Viewer';
    this.filteredByEmail   = [...this.allUsers];
    this.filteredByName    = [...this.allUsers];
    this.showEmailDropdown = false;
    this.showNameDropdown  = false;
    this.showAssignModal   = true;
    this.cdr.markForCheck();
  }

  closeAssignModal(): void {
    this.showAssignModal   = false;
    this.newUserName       = '';
    this.newUserEmail      = '';
    this.newUserRole       = 'Viewer';
    this.showEmailDropdown = false;
    this.showNameDropdown  = false;
    this.cdr.markForCheck();
  }

  onEmailFocus(): void {
    if (this.emailDropdownTimer) clearTimeout(this.emailDropdownTimer);
    this.filteredByEmail  = this.newUserEmail.trim()
      ? this.allUsers.filter(u => u.email.toLowerCase().includes(this.newUserEmail.toLowerCase())
          || `${u.firstName} ${u.lastName}`.toLowerCase().includes(this.newUserEmail.toLowerCase()))
      : [...this.allUsers];
    this.showEmailDropdown = true;
    this.showNameDropdown  = false;
    this.cdr.markForCheck();
  }

  onEmailInput(): void {
    this.filteredByEmail = this.newUserEmail.trim()
      ? this.allUsers.filter(u => u.email.toLowerCase().includes(this.newUserEmail.toLowerCase())
          || `${u.firstName} ${u.lastName}`.toLowerCase().includes(this.newUserEmail.toLowerCase()))
      : [...this.allUsers];
    this.showEmailDropdown = true;
    this.cdr.markForCheck();
  }

  scheduleCloseEmailDropdown(): void {
    this.emailDropdownTimer = setTimeout(() => {
      this.showEmailDropdown = false;
      this.cdr.markForCheck();
    }, 200);
  }

  onNameFocus(): void {
    if (this.nameDropdownTimer) clearTimeout(this.nameDropdownTimer);
    this.filteredByName = this.newUserName.trim()
      ? this.allUsers.filter(u => `${u.firstName} ${u.lastName}`.toLowerCase().includes(this.newUserName.toLowerCase())
          || u.email.toLowerCase().includes(this.newUserName.toLowerCase()))
      : [...this.allUsers];
    this.showNameDropdown  = true;
    this.showEmailDropdown = false;
    this.cdr.markForCheck();
  }

  onNameInput(): void {
    this.filteredByName = this.newUserName.trim()
      ? this.allUsers.filter(u => `${u.firstName} ${u.lastName}`.toLowerCase().includes(this.newUserName.toLowerCase())
          || u.email.toLowerCase().includes(this.newUserName.toLowerCase()))
      : [...this.allUsers];
    this.showNameDropdown = true;
    this.cdr.markForCheck();
  }

  scheduleCloseNameDropdown(): void {
    this.nameDropdownTimer = setTimeout(() => {
      this.showNameDropdown = false;
      this.cdr.markForCheck();
    }, 200);
  }

  selectUserForAssign(user: PlatformUser): void {
    if (this.emailDropdownTimer) clearTimeout(this.emailDropdownTimer);
    if (this.nameDropdownTimer)  clearTimeout(this.nameDropdownTimer);
    this.newUserEmail      = user.email;
    this.newUserName       = `${user.firstName} ${user.lastName}`;
    this.showEmailDropdown = false;
    this.showNameDropdown  = false;
    this.cdr.markForCheck();
  }

  assignUser(): void {
    if (!this.region || !this.newUserName || !this.newUserEmail) return;

    const matched     = this.allUsers.find(u => u.email === this.newUserEmail);
    const initials    = matched ? matched.avatarInitials
      : this.newUserName.split(' ').map(w => w[0]).join('').toUpperCase().slice(0, 2);
    const colors      = ['#3b82f6', '#8b5cf6', '#10b981', '#f59e0b', '#ef4444'];
    const avatarColor = matched ? matched.avatarColor : colors[this.region.team.length % colors.length];

    const newMember: RegionTeamMember = {
      id:           matched?.id ?? 'u' + Date.now(),
      name:         this.newUserName,
      email:        this.newUserEmail,
      initials,
      avatarColor,
      role:         this.newUserRole,
      status:       'Active',
      assignedDate: new Date().toLocaleDateString('en-GB', {
        day: '2-digit', month: 'long', year: 'numeric',
      }),
    };

    const updatedTeam = [...this.region.team, newMember];
    const regionName  = this.region.name;

    this.region.team = updatedTeam;
    this.closeAssignModal();
    this.cdr.markForCheck();

    this.svc.updateTeam$(this.region.id, updatedTeam).pipe(takeUntil(this.destroy$)).subscribe();

    if (matched) {
      this.usersSvc.addRegion$(matched.id, regionName).pipe(takeUntil(this.destroy$)).subscribe();
    }
  }

  // ── Remove Member ────────────────────────────────────────────────

  promptRemove(member: RegionTeamMember): void {
    this.removeTarget = member;
    this.cdr.markForCheck();
  }

  cancelRemove(): void { this.removeTarget = null; this.cdr.markForCheck(); }

  confirmRemove(): void {
    if (!this.region || !this.removeTarget) return;

    const updatedTeam = this.region.team.filter(m => m.id !== this.removeTarget!.id);
    const regionId    = this.region.id;
    const regionName  = this.region.name;
    const removedId   = this.removeTarget.id;

    this.region.team  = updatedTeam;
    this.removeTarget = null;
    this.cdr.markForCheck();

    this.svc.updateTeam$(regionId, updatedTeam).pipe(takeUntil(this.destroy$)).subscribe();
    this.usersSvc.removeRegion$(removedId, regionName).pipe(takeUntil(this.destroy$)).subscribe();
  }

  // ── Edit Member Modal ────────────────────────────────────────────

  openViewMember(member: RegionTeamMember): void {
    this.isMemberReadOnly = true;
    this._loadMember(member);
  }

  openEditMember(member: RegionTeamMember): void {
    this.isMemberReadOnly = false;
    this._loadMember(member);
  }

  private _loadMember(member: RegionTeamMember): void {
    const found = this.allUsers.find(u => u.email === member.email || u.id === member.id);
    const currentRegion = this.region?.name ?? '';

    if (found) {
      const assignedRegions = currentRegion && !found.assignedRegions.includes(currentRegion)
        ? [...found.assignedRegions, currentRegion]
        : found.assignedRegions;

      this.editMember = { ...found, assignedRegions };
    } else {
      const parts = member.name.split(' ');
      const effectiveRole: 'Regional Manager' | 'Viewer' =
        member.role === 'Regional Manager' ? 'Regional Manager' : 'Viewer';
      this.editMember = {
        id:              member.id,
        firstName:       parts[0] ?? member.name,
        lastName:        parts.slice(1).join(' '),
        email:           member.email,
        avatarInitials:  member.initials,
        avatarColor:     member.avatarColor,
        role:            effectiveRole,
        status:          effectiveRole === 'Regional Manager' ? 'Active' : 'Inactive',
        assignedRegions: currentRegion ? [currentRegion] : [],
        lastLogin:       '—',
        createdAt:       member.assignedDate,
      };
    }
    this.cdr.markForCheck();
  }

  closeEditMember(): void {
    this.editMember       = null;
    this.isMemberReadOnly = false;
    this.isTogglingRole   = false;
    this.cdr.markForCheck();
  }

  toggleMemberRole(): void {
    if (!this.editMember) return;
    this.isTogglingRole = true;

    const obs = this.editMember.role === 'Regional Manager'
      ? this.usersSvc.removeManagerRole$(this.editMember.id)
      : this.usersSvc.promoteToManager$(this.editMember.id);

    obs.pipe(takeUntil(this.destroy$)).subscribe(updated => {
      if (updated && this.editMember) {
        this.editMember = { ...this.editMember, role: updated.role, status: updated.status };

        if (this.region) {
          const updatedTeam = this.region.team.map(m =>
            m.id === updated.id ? { ...m, role: updated.role, status: updated.status } : m
          );
          this.region.team = updatedTeam;
          this.svc.updateTeam$(this.region.id, updatedTeam).pipe(takeUntil(this.destroy$)).subscribe();
        }
      }
      this.isTogglingRole = false;
      this.cdr.markForCheck();
    });
  }

  removeRegionFromMember(region: string): void {
    if (!this.editMember) return;
    this.usersSvc.removeRegion$(this.editMember.id, region)
      .pipe(takeUntil(this.destroy$))
      .subscribe(updated => {
        if (updated && this.editMember) {
          this.editMember = { ...this.editMember, assignedRegions: updated.assignedRegions };
        }
        this.cdr.markForCheck();
      });
  }

  formatLastLogin(iso: string): string {
    return this.usersSvc.formatLastLogin(iso);
  }

  statusClass(s: string): string {
    return s === 'Active'
      ? 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200'
      : 'bg-gray-100 text-gray-500 ring-1 ring-gray-200';
  }

  roleClass(role: string): string {
    return role === 'Regional Manager'
      ? 'bg-blue-50 text-blue-700 ring-1 ring-blue-200'
      : 'bg-gray-50 text-gray-500 ring-1 ring-gray-200';
  }

  get scoreColor(): string {
    const s = this.region?.activityScore ?? 0;
    if (s >= 85) return '#10b981';
    if (s >= 60) return '#f59e0b';
    return '#ef4444';
  }
}
