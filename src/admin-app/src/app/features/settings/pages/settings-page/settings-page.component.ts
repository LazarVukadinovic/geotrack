import { ChangeDetectorRef, Component, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { AuthService } from '../../../../core/services/auth.service';
import { AdminProfileService } from '../../../../core/services/admin-profile.service';
import { AdminReviewsService } from '../../../../core/services/administrator/admin-reviews.service';
import { AdminProfile, AdminProfileRegion } from '../../../../core/models/admin-profile.model';
import { RegionalAssignedRegion } from '../../../../core/models/dashboard.model';

interface SelectOption {
  value: string;
  label: string;
}

const MAX_AVATAR_FILE_BYTES = 2 * 1024 * 1024;
const ALLOWED_AVATAR_TYPES = new Set(['image/png', 'image/jpeg', 'image/jpg', 'image/webp']);

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './settings-page.component.html',
  styleUrl: './settings-page.component.css',
})
export class SettingsComponent implements OnInit {
  private static readonly activeRegionStorageKey = 'geotrack_admin_active_region_id';

  readonly languageOptions: SelectOption[] = [
    { value: 'en-US', label: 'English (US)' },
    { value: 'sr', label: 'Serbian (Srpski)' },
    { value: 'de', label: 'German (Deutsch)' },
    { value: 'fr', label: 'French (Francais)' },
  ];

  readonly timezoneOptions: SelectOption[] = [
    { value: 'Europe/Belgrade', label: 'Europe/Belgrade' },
    { value: 'UTC', label: 'UTC' },
    { value: 'Europe/Berlin', label: 'Europe/Berlin' },
    { value: 'Europe/Paris', label: 'Europe/Paris' },
    { value: 'America/New_York', label: 'America/New_York' },
    { value: 'America/Los_Angeles', label: 'America/Los_Angeles' },
  ];

  assignedRegions: RegionalAssignedRegion[] = [];
  activeRegionId: number | null = null;
  regionLoading = false;

  readonly firstName = signal('');
  readonly lastName = signal('');
  readonly email = signal('');
  readonly bio = signal('');
  readonly avatarUrl = signal('');
  readonly bioMaxLength = 300;
  readonly bioLength = computed(() => this.bio().length);
  readonly initials = computed(() => this.buildInitials());

  readonly language = signal('en-US');
  readonly timezone = signal('UTC');
  readonly notifyRequests = signal(true);
  readonly notifyNewReview = signal(true);

  readonly isLoading = signal(true);
  readonly isSaving = signal(false);
  readonly isUploadingAvatar = signal(false);
  readonly saveSuccess = signal(false);
  readonly errorMessage = signal<string | null>(null);
  readonly saveError = signal<string | null>(null);
  readonly avatarError = signal<string | null>(null);

  private lastProfile: AdminProfile | null = null;

  constructor(
    private readonly router: Router,
    private readonly auth: AuthService,
    private readonly adminProfileService: AdminProfileService,
    private readonly adminReviewsService: AdminReviewsService,
    private readonly cdr: ChangeDetectorRef,
  ) {}

  get isAdmin(): boolean {
    return this.auth.isAdministrator();
  }

  get reportedReviewsCount(): number {
    return this.adminReviewsService.getReportedCount();
  }

  ngOnInit(): void {
    this.loadProfile();
  }

  get userName(): string {
    return this.auth.fullName;
  }

  get userRole(): string {
    return this.auth.getUser()?.role ?? '';
  }

  get regionDisplayName(): string {
    return this.activeRegion?.displayName ?? '';
  }

  get activeRegion(): RegionalAssignedRegion | null {
    return this.assignedRegions.find(region => region.id === this.activeRegionId) ?? null;
  }

  get currentRole(): string {
    const roles = this.lastProfile?.roles ?? [];
    if (roles.some(role => this.normalizeRole(role) === 'regionalmanager')) {
      return 'Regional Manager';
    }

    const formattedRoles = roles.map(role => this.formatRole(role)).filter(Boolean);
    return formattedRoles.join(', ') || this.userRole || '-';
  }

  get assignedRegion(): string {
    const regions = this.lastProfile?.regionalManager.assignedRegions ?? [];
    if (regions.length === 0) {
      return 'No assigned regions';
    }

    return regions.map(region => region.displayName).join(', ');
  }

  get memberSince(): string {
    return this.formatDate(this.lastProfile?.regionalManager.memberSince);
  }

  loadProfile(): void {
    this.isLoading.set(true);
    this.regionLoading = true;
    this.errorMessage.set(null);
    this.saveError.set(null);
    this.avatarError.set(null);
    this.notifyStateChanged();

    this.adminProfileService
      .getProfile()
      .pipe(
        finalize(() => {
          this.isLoading.set(false);
          this.regionLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: profile => this.applyProfile(profile),
        error: error => {
          this.lastProfile = null;
          this.assignedRegions = [];
          this.activeRegionId = null;
          this.errorMessage.set(this.toProfileLoadError(error));
          this.clearPersistedRegion();
          this.notifyStateChanged();
        },
      });
  }

  onBioChange(value: string): void {
    this.bio.set(value.slice(0, this.bioMaxLength));
  }

  onAvatarChange(event: Event): void {
    const input = event.target instanceof HTMLInputElement ? event.target : null;
    const file = input?.files?.[0] ?? null;
    if (input) {
      input.value = '';
    }

    if (!file || this.isUploadingAvatar()) {
      return;
    }

    this.avatarError.set(null);

    if (file.size > MAX_AVATAR_FILE_BYTES) {
      this.avatarError.set('Profile image must be 2 MB or smaller.');
      this.notifyStateChanged();
      return;
    }

    if (file.type && !ALLOWED_AVATAR_TYPES.has(file.type)) {
      this.avatarError.set('Only PNG, JPG, JPEG, or WEBP images are supported.');
      this.notifyStateChanged();
      return;
    }

    this.isUploadingAvatar.set(true);
    this.notifyStateChanged();

    this.adminProfileService
      .uploadAvatar(file)
      .pipe(
        finalize(() => {
          this.isUploadingAvatar.set(false);
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: response => {
          const avatarUrl = response.avatarUrl ?? '';
          this.avatarUrl.set(avatarUrl);
          this.patchLastProfileAvatar(avatarUrl, response.updatedAt ?? null);
          this.patchAuthUser({ avatarUrl });
          this.avatarError.set(null);
          this.notifyStateChanged();
        },
        error: error => {
          this.avatarError.set(this.toErrorMessage(error, 'Failed to upload profile image.'));
          this.notifyStateChanged();
        },
      });
  }

  removeAvatar(): void {
    if (this.isUploadingAvatar()) {
      return;
    }

    this.avatarError.set(null);
    this.isUploadingAvatar.set(true);
    this.notifyStateChanged();

    this.adminProfileService
      .removeAvatar()
      .pipe(
        finalize(() => {
          this.isUploadingAvatar.set(false);
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: response => {
          this.avatarUrl.set('');
          this.patchLastProfileAvatar('', response.updatedAt ?? null);
          this.patchAuthUser({ avatarUrl: undefined });
          this.avatarError.set(null);
          this.notifyStateChanged();
        },
        error: error => {
          this.avatarError.set(this.toErrorMessage(error, 'Failed to remove profile image.'));
          this.notifyStateChanged();
        },
      });
  }

  onSave(): void {
    if (this.isSaving() || this.isLoading()) {
      return;
    }

    this.isSaving.set(true);
    this.saveSuccess.set(false);
    this.saveError.set(null);
    this.notifyStateChanged();

    this.adminProfileService
      .updateProfile({
        firstName: this.firstName().trim(),
        lastName: this.lastName().trim(),
        shortBio: this.bio().trim() || null,
        interfaceLanguage: this.language(),
        timezone: this.timezone(),
        emailRequestsEnabled: this.notifyRequests(),
        emailNewReviewEnabled: this.notifyNewReview(),
      })
      .pipe(
        finalize(() => {
          this.isSaving.set(false);
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: profile => {
          this.applyProfile(profile);
          this.saveSuccess.set(true);
          this.notifyStateChanged();
        },
        error: error => {
          this.saveError.set(this.toErrorMessage(error, 'Failed to save settings.'));
          this.notifyStateChanged();
        },
      });
  }

  onCancel(): void {
    if (this.lastProfile) {
      this.applyProfile(this.lastProfile);
    } else {
      this.router.navigate(['/dashboard']);
    }

    this.saveSuccess.set(false);
    this.saveError.set(null);
    this.avatarError.set(null);
    this.notifyStateChanged();
  }

  onRegionSelected(regionId: number): void {
    if (!this.assignedRegions.some(region => region.id === regionId)) {
      return;
    }

    this.activeRegionId = regionId;
    this.persistRegion(regionId);
    this.notifyStateChanged();
  }

  private applyProfile(profile: AdminProfile): void {
    const normalizedProfile = this.normalizeProfile(profile);
    this.lastProfile = normalizedProfile;

    this.firstName.set(normalizedProfile.user.firstName ?? '');
    this.lastName.set(normalizedProfile.user.lastName ?? '');
    this.email.set(normalizedProfile.user.email ?? '');
    this.bio.set((normalizedProfile.user.shortBio ?? '').slice(0, this.bioMaxLength));
    this.avatarUrl.set(normalizedProfile.user.avatarUrl ?? '');
    this.language.set(normalizedProfile.preferences.interfaceLanguage);
    this.timezone.set(normalizedProfile.preferences.timezone);
    this.notifyRequests.set(normalizedProfile.preferences.emailRequestsEnabled);
    this.notifyNewReview.set(normalizedProfile.preferences.emailNewReviewEnabled);

    this.assignedRegions = normalizedProfile.regionalManager.assignedRegions.map(region =>
      this.toSidebarRegion(region),
    );
    this.selectInitialActiveRegion();

    this.patchAuthUser({
      firstName: normalizedProfile.user.firstName,
      lastName: normalizedProfile.user.lastName,
      email: normalizedProfile.user.email,
      region: this.assignedRegion,
      memberSince: this.memberSince,
      bio: normalizedProfile.user.shortBio ?? '',
      language: normalizedProfile.preferences.interfaceLanguage,
      timezone: normalizedProfile.preferences.timezone,
      notifyRequests: normalizedProfile.preferences.emailRequestsEnabled,
      notifyNewReview: normalizedProfile.preferences.emailNewReviewEnabled,
      avatarUrl: normalizedProfile.user.avatarUrl ?? undefined,
    });

    this.notifyStateChanged();
  }

  private normalizeProfile(profile: AdminProfile): AdminProfile {
    const language = this.isAllowedOption(this.languageOptions, profile.preferences.interfaceLanguage)
      ? profile.preferences.interfaceLanguage
      : 'en-US';
    const timezone = this.isAllowedOption(this.timezoneOptions, profile.preferences.timezone)
      ? profile.preferences.timezone
      : 'UTC';

    return {
      ...profile,
      roles: profile.roles ?? [],
      regionalManager: {
        assignedRegions: profile.regionalManager?.assignedRegions ?? [],
        memberSince: profile.regionalManager?.memberSince ?? null,
      },
      preferences: {
        interfaceLanguage: language,
        timezone,
        emailRequestsEnabled: profile.preferences?.emailRequestsEnabled ?? true,
        emailNewReviewEnabled: profile.preferences?.emailNewReviewEnabled ?? true,
      },
    };
  }

  private toSidebarRegion(region: AdminProfileRegion): RegionalAssignedRegion {
    return {
      id: region.id,
      name: region.name,
      displayName: region.displayName,
      territoryName: region.territoryName,
    };
  }

  private selectInitialActiveRegion(): void {
    if (this.assignedRegions.length === 0) {
      this.activeRegionId = null;
      this.clearPersistedRegion();
      return;
    }

    const persistedRegionId = this.readPersistedRegionId();
    const selectedRegion =
      this.assignedRegions.find(region => region.id === persistedRegionId) ?? this.assignedRegions[0];

    this.activeRegionId = selectedRegion.id;
    this.persistRegion(selectedRegion.id);
  }

  private patchLastProfileAvatar(avatarUrl: string, updatedAt: string | null): void {
    if (!this.lastProfile) {
      return;
    }

    this.lastProfile = {
      ...this.lastProfile,
      user: {
        ...this.lastProfile.user,
        avatarUrl: avatarUrl || null,
        updatedAt,
      },
    };
  }

  private patchAuthUser(update: Partial<NonNullable<ReturnType<AuthService['getUser']>>>): void {
    this.auth.updateUser(update).subscribe({
      error: () => undefined,
    });
  }

  private isAllowedOption(options: SelectOption[], value: string | null | undefined): value is string {
    return options.some(option => option.value === value);
  }

  private formatRole(role: string): string {
    switch (this.normalizeRole(role)) {
      case 'regionalmanager':
        return 'Regional Manager';
      case 'nationalmanager':
        return 'National Manager';
      case 'admin':
        return 'Admin';
      default:
        return role;
    }
  }

  private normalizeRole(role: string): string {
    return role.replace(/[\s_-]/g, '').toLowerCase();
  }

  private formatDate(value: string | null | undefined): string {
    if (!value) {
      return '-';
    }

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return '-';
    }

    return date.toLocaleDateString(undefined, {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    });
  }

  private buildInitials(): string {
    const names = [this.firstName(), this.lastName()]
      .map(part => part.trim())
      .filter(Boolean);

    if (names.length > 0) {
      return names
        .slice(0, 2)
        .map(part => part.charAt(0).toUpperCase())
        .join('');
    }

    return (this.email().trim().charAt(0) || '?').toUpperCase();
  }

  private readPersistedRegionId(): number | null {
    try {
      const value = sessionStorage.getItem(SettingsComponent.activeRegionStorageKey);
      const parsed = value ? Number(value) : NaN;
      return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
    } catch {
      return null;
    }
  }

  private persistRegion(regionId: number): void {
    try {
      sessionStorage.setItem(SettingsComponent.activeRegionStorageKey, String(regionId));
    } catch {
      // Active region persistence is only a session convenience.
    }
  }

  private clearPersistedRegion(): void {
    try {
      sessionStorage.removeItem(SettingsComponent.activeRegionStorageKey);
    } catch {
      // Active region persistence is only a session convenience.
    }
  }

  private toProfileLoadError(error: unknown): string {
    if (error instanceof HttpErrorResponse && error.status === 403) {
      return 'This account is not authorized to view the admin profile.';
    }

    if (error instanceof HttpErrorResponse && error.status === 401) {
      return 'Sign in with an authenticated Regional Manager account to view settings.';
    }

    return this.toErrorMessage(error, 'Unable to load profile settings.');
  }

  private toErrorMessage(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse && typeof error.error?.message === 'string') {
      return error.error.message;
    }

    return fallback;
  }

  private notifyStateChanged(): void {
    this.cdr.markForCheck();
  }
}
