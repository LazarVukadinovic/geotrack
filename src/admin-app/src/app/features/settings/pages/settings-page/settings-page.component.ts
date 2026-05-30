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
import { AdminDashboardService } from '../../../../core/services/administrator/admin-dashboard.service';
import { ContentModeratorDashboardService } from '../../../../core/services/content-moderator/content-moderator-dashboard.service';
import { buildAvatarDisplayUrl } from '../../../../core/utils/avatar-url';
import {
  AdminProfile,
  AdminProfileAssignment,
  AdminProfileRegion,
  AdminProfileRole,
} from '../../../../core/models/admin-profile.model';
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
  readonly avatarVersion = signal<number | null>(null);
  readonly avatarImageUrl = computed(() => buildAvatarDisplayUrl(this.avatarUrl(), this.avatarVersion()));
  readonly bioMaxLength = 300;
  readonly bioLength = computed(() => this.bio().length);
  readonly initials = computed(() => this.buildInitials());

  readonly language = signal('en-US');
  readonly timezone = signal('UTC');
  readonly notifyRequests = signal(false);
  readonly notifyNewReview = signal(false);

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
    private readonly adminDashboardService: AdminDashboardService,
    private readonly contentModeratorDashboardService: ContentModeratorDashboardService,
    private readonly cdr: ChangeDetectorRef,
  ) {}

  get isAdmin(): boolean {
    return this.auth.isAdministrator() || this.isAdminProfile;
  }

  get isContentModerator(): boolean {
    return this.auth.isContentModerator() || this.isContentModeratorProfile;
  }

  get hasAssignmentAccessLevel(): boolean {
    return this.assignmentAccessLevel.trim().length > 0;
  }

  supportReportsCount = 0;
  reportedReviewsCount = 0;

  ngOnInit(): void {
    this.loadPendingReportsBadge();
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
    const activeRole = this.userRole.trim();
    if (activeRole) {
      return activeRole;
    }

    const roleDisplayName = this.lastProfile?.role?.roleDisplayName?.trim();
    if (roleDisplayName) {
      return this.formatRole(roleDisplayName);
    }

    const roles = this.lastProfile?.roles ?? [];
    if (roles.some(role => this.normalizeRole(role) === 'admin')) {
      return 'Administrator';
    }

    if (roles.some(role => this.normalizeRole(role) === 'contentmoderator')) {
      return 'Content Moderator';
    }

    if (roles.some(role => this.normalizeRole(role) === 'territorymanager')) {
      return 'Territory Manager';
    }

    if (roles.some(role => this.normalizeRole(role) === 'territoryassociate')) {
      return 'Territory Associate';
    }

    if (roles.some(role => this.normalizeRole(role) === 'regionalmanager')) {
      return 'Regional Manager';
    }

    const formattedRoles = roles.map(role => this.formatRole(role)).filter(Boolean);
    return formattedRoles.join(', ') || this.userRole || '-';
  }

  get assignmentSectionTitle(): string {
    return this.lastProfile?.assignment?.sectionTitle?.trim() || 'Assignment';
  }

  get assignmentLabel(): string {
    return this.lastProfile?.assignment?.label?.trim() || 'Assigned Area';
  }

  get assignmentDisplayValue(): string {
    return this.lastProfile?.assignment?.displayValue?.trim() || '-';
  }

  get assignmentAccessLevel(): string {
    return this.lastProfile?.assignment?.accessLevel?.trim() || (this.isAdmin ? 'Full access' : (this.isContentModerator ? 'Review moderation' : ''));
  }

  get notificationPrimaryLabel(): string {
    if (this.isContentModerator) {
      return 'Review reports';
    }

    return this.isAdmin ? 'Reports' : 'Requests';
  }

  get notificationPrimaryDescription(): string {
    if (!this.emailNotificationsAvailable) {
      if (this.isContentModerator) {
        return 'Email delivery for review reports is not available yet.';
      }

      return this.isAdmin
        ? 'Email delivery for administrator reports is not available yet.'
        : 'Email delivery for requests is not available yet.';
    }

    if (this.isContentModerator) {
      return 'Receive an email when a review report needs moderation.';
    }

    return this.isAdmin
      ? 'Receive an email when a new report needs administrator review.'
      : 'Receive an email when someone submits a request.';
  }

  get notificationSecondaryLabel(): string {
    if (this.isContentModerator) {
      return 'Moderation updates';
    }

    return this.isAdmin ? 'Reviews' : 'New Review';
  }

  get notificationSecondaryDescription(): string {
    if (!this.emailNotificationsAvailable) {
      if (this.isContentModerator) {
        return 'Email delivery for moderation updates is not available yet.';
      }

      return this.isAdmin
        ? 'Email delivery for review moderation is not available yet.'
        : 'Email delivery for new reviews is not available yet.';
    }

    if (this.isContentModerator) {
      return 'Get notified about changes to review moderation work.';
    }

    return this.isAdmin
      ? 'Get notified about reported reviews requiring moderation.'
      : 'Get notified when someone leaves a review.';
  }

  get emailNotificationsAvailable(): boolean {
    return this.lastProfile?.preferences.emailNotificationsAvailable === true;
  }

  get canEditEmailNotifications(): boolean {
    return this.emailNotificationsAvailable && !this.isSaving();
  }

  get emailNotificationUnavailableMessage(): string {
    return 'Email notifications are not available yet. In-app notifications are currently supported.';
  }

  get assignedRegion(): string {
    return this.assignmentDisplayValue;
  }

  get memberSince(): string {
    return this.formatDate(
      this.lastProfile?.assignment?.memberSince ?? this.lastProfile?.regionalManager.memberSince,
    );
  }

  get hasChanges(): boolean {
    const profile = this.lastProfile;
    if (!profile || this.isLoading()) {
      return false;
    }

    const profileFieldsChanged = this.firstName().trim() !== (profile.user.firstName ?? '').trim()
      || this.lastName().trim() !== (profile.user.lastName ?? '').trim()
      || this.normalizeOptionalString(this.bio()) !== this.normalizeOptionalString(profile.user.shortBio);

    const interfacePreferencesChanged = this.language() !== profile.preferences.interfaceLanguage
      || this.timezone() !== profile.preferences.timezone;

    const emailPreferencesChanged = this.emailNotificationsAvailable &&
      (this.notifyRequests() !== profile.preferences.emailRequestsEnabled
        || this.notifyNewReview() !== profile.preferences.emailNewReviewEnabled);

    return profileFieldsChanged || interfacePreferencesChanged || emailPreferencesChanged;
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

  private loadPendingReportsBadge(): void {
    if (this.isContentModerator) {
      this.supportReportsCount = 0;
      this.contentModeratorDashboardService.loadDashboard().subscribe({
        next: dashboard => {
          this.reportedReviewsCount = dashboard.badges.pendingReviewReportsCount;
          this.notifyStateChanged();
        },
        error: () => {
          this.reportedReviewsCount = 0;
          this.notifyStateChanged();
        },
      });
      return;
    }

    if (!this.isAdmin) {
      this.supportReportsCount = 0;
      this.reportedReviewsCount = 0;
      return;
    }

    this.adminDashboardService.loadDashboard().subscribe({
      next: dashboard => {
        this.supportReportsCount = dashboard.badges.supportReportsCount;
        this.notifyStateChanged();
      },
      error: () => {
        this.supportReportsCount = 0;
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
          const avatarVersion = response.avatarVersion ?? null;
          this.avatarUrl.set(avatarUrl);
          this.avatarVersion.set(avatarVersion);
          this.patchLastProfileAvatar(avatarUrl, avatarVersion, response.updatedAt ?? null);
          this.patchAuthUser({ avatarUrl, avatarVersion });
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
          this.avatarVersion.set(null);
          this.patchLastProfileAvatar('', null, response.updatedAt ?? null);
          this.patchAuthUser({ avatarUrl: undefined, avatarVersion: null });
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
    if (this.isSaving() || this.isLoading() || !this.hasChanges) {
      return;
    }

    const emailRequestsEnabled = this.emailNotificationsAvailable ? this.notifyRequests() : false;
    const emailNewReviewEnabled = this.emailNotificationsAvailable ? this.notifyNewReview() : false;

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
        emailRequestsEnabled,
        emailNewReviewEnabled,
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
      this.router.navigateByUrl(this.auth.getHomeRoute());
    }

    this.saveSuccess.set(false);
    this.saveError.set(null);
    this.avatarError.set(null);
    this.notifyStateChanged();
  }

  toggleEmailRequests(): void {
    if (!this.canEditEmailNotifications) {
      return;
    }

    this.notifyRequests.update(value => !value);
  }

  toggleEmailNewReview(): void {
    if (!this.canEditEmailNotifications) {
      return;
    }

    this.notifyNewReview.update(value => !value);
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
    this.avatarVersion.set(normalizedProfile.user.avatarVersion ?? null);
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
      region: this.assignmentDisplayValue,
      memberSince: this.memberSince,
      bio: normalizedProfile.user.shortBio ?? '',
      language: normalizedProfile.preferences.interfaceLanguage,
      timezone: normalizedProfile.preferences.timezone,
      notifyRequests: normalizedProfile.preferences.emailRequestsEnabled,
      notifyNewReview: normalizedProfile.preferences.emailNewReviewEnabled,
      avatarUrl: normalizedProfile.user.avatarUrl ?? undefined,
      avatarVersion: normalizedProfile.user.avatarVersion ?? null,
    });

    this.notifyStateChanged();
  }

  private normalizeProfile(profile: AdminProfile): AdminProfile {
    const providedRoles = profile.roles ?? [];
    const role = profile.role ?? this.buildFallbackRole(providedRoles);
    const roles = providedRoles.length > 0
      ? providedRoles
      : (role.roleCode ? [role.roleCode] : []);
    const regionalManager = {
      assignedRegions: profile.regionalManager?.assignedRegions ?? [],
      memberSince: profile.regionalManager?.memberSince ?? null,
    };
    const assignment = this.normalizeAssignment(
      profile.assignment,
      roles,
      regionalManager,
    );
    const preferences = profile.preferences ?? {
      interfaceLanguage: 'en-US',
      timezone: 'UTC',
      emailNotificationsAvailable: false,
      emailRequestsEnabled: false,
      emailNewReviewEnabled: false,
    };
    const language = this.isAllowedOption(this.languageOptions, preferences.interfaceLanguage)
      ? preferences.interfaceLanguage
      : 'en-US';
    const timezone = this.isAllowedOption(this.timezoneOptions, preferences.timezone)
      ? preferences.timezone
      : 'UTC';
    const emailNotificationsAvailable = preferences.emailNotificationsAvailable === true;

    return {
      ...profile,
      roles,
      role,
      assignment,
      regionalManager,
      preferences: {
        interfaceLanguage: language,
        timezone,
        emailNotificationsAvailable,
        emailRequestsEnabled: emailNotificationsAvailable && (preferences.emailRequestsEnabled ?? false),
        emailNewReviewEnabled: emailNotificationsAvailable && (preferences.emailNewReviewEnabled ?? false),
      },
    };
  }

  private normalizeAssignment(
    assignment: AdminProfileAssignment | null | undefined,
    roles: string[],
    regionalManager: AdminProfile['regionalManager'],
  ): AdminProfileAssignment {
    if (assignment) {
      const assignedItems = assignment.assignedItems ?? [];
      const displayValue = assignment.displayValue?.trim()
        || this.joinAssignmentItems(assignedItems)
        || this.emptyAssignmentValue(assignment.assignmentType, roles);

      return {
        assignmentType: assignment.assignmentType?.trim() || this.fallbackAssignmentType(roles),
        sectionTitle: assignment.sectionTitle?.trim() || this.fallbackAssignmentTitle(roles),
        label: assignment.label?.trim() || this.fallbackAssignmentLabel(roles),
        displayValue,
        accessLevel: assignment.accessLevel?.trim() || (this.isAdminRoleSet(roles) ? 'Full access' : null),
        assignedItems,
        memberSince: assignment.memberSince ?? null,
      };
    }

    return this.buildFallbackAssignment(roles, regionalManager);
  }

  private buildFallbackAssignment(
    roles: string[],
    regionalManager: AdminProfile['regionalManager'],
  ): AdminProfileAssignment {
    if (this.isAdminRoleSet(roles)) {
      return {
        assignmentType: 'System',
        sectionTitle: 'System Access',
        label: 'Scope',
        displayValue: 'Platform-wide',
        accessLevel: 'Full access',
        assignedItems: [],
        memberSince: null,
      };
    }

    if (this.isContentModeratorRoleSet(roles)) {
      return {
        assignmentType: 'ContentModeration',
        sectionTitle: 'Content Moderation Access',
        label: 'Scope',
        displayValue: 'Content moderation',
        accessLevel: 'Review moderation',
        assignedItems: [],
        memberSince: null,
      };
    }

    const isTerritoryManager = roles.some(role => this.normalizeRole(role) === 'territorymanager');
    if (isTerritoryManager) {
      return {
        assignmentType: 'Territory',
        sectionTitle: 'Territory Manager Assignment',
        label: 'Assigned Territory',
        displayValue: 'No assigned territories',
        assignedItems: [],
        memberSince: null,
      };
    }

    const assignedItems = regionalManager.assignedRegions.map(region => ({
      id: region.id,
      name: region.name,
      displayName: region.displayName,
      assignedAt: region.assignedAt,
    }));

    return {
      assignmentType: 'Region',
      sectionTitle: 'Regional Manager Assignment',
      label: 'Assigned Region',
      displayValue: this.joinAssignmentItems(assignedItems) || 'No assigned regions',
      assignedItems,
      memberSince: regionalManager.memberSince ?? null,
    };
  }

  private buildFallbackRole(roles: string[]): AdminProfileRole {
    const roleCode = roles.find(role => this.normalizeRole(role) === 'admin')
      ?? roles.find(role => this.normalizeRole(role) === 'contentmoderator')
      ?? roles.find(role => this.normalizeRole(role) === 'territorymanager')
      ?? roles.find(role => this.normalizeRole(role) === 'regionalmanager')
      ?? roles[0]
      ?? '';

    return {
      roleCode,
      roleDisplayName: this.formatRole(roleCode),
    };
  }

  private joinAssignmentItems(items: Array<{ displayName?: string | null }>): string {
    return items
      .map(item => item.displayName?.trim())
      .filter((value): value is string => !!value)
      .join(', ');
  }

  private fallbackAssignmentType(roles: string[]): string {
    if (this.isAdminRoleSet(roles)) {
      return 'System';
    }

    if (this.isContentModeratorRoleSet(roles)) {
      return 'ContentModeration';
    }

    return roles.some(role => this.normalizeRole(role) === 'territorymanager')
      ? 'Territory'
      : 'Region';
  }

  private fallbackAssignmentTitle(roles: string[]): string {
    if (this.isAdminRoleSet(roles)) {
      return 'System Access';
    }

    if (this.isContentModeratorRoleSet(roles)) {
      return 'Content Moderation Access';
    }

    return roles.some(role => this.normalizeRole(role) === 'territorymanager')
      ? 'Territory Manager Assignment'
      : 'Regional Manager Assignment';
  }

  private fallbackAssignmentLabel(roles: string[]): string {
    if (this.isAdminRoleSet(roles)) {
      return 'Scope';
    }

    if (this.isContentModeratorRoleSet(roles)) {
      return 'Scope';
    }

    return roles.some(role => this.normalizeRole(role) === 'territorymanager')
      ? 'Assigned Territory'
      : 'Assigned Region';
  }

  private emptyAssignmentValue(assignmentType: string | null | undefined, roles: string[]): string {
    const normalizedType = (assignmentType ?? this.fallbackAssignmentType(roles)).trim().toLowerCase();
    if (normalizedType === 'system' || this.isAdminRoleSet(roles)) {
      return 'Platform-wide';
    }

    if (normalizedType === 'contentmoderation' || this.isContentModeratorRoleSet(roles)) {
      return 'Content moderation';
    }

    if (normalizedType === 'country') {
      return 'No assigned countries';
    }

    return normalizedType === 'territory' ? 'No assigned territories' : 'No assigned regions';
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

  private patchLastProfileAvatar(avatarUrl: string, avatarVersion: number | null, updatedAt: string | null): void {
    if (!this.lastProfile) {
      return;
    }

    this.lastProfile = {
      ...this.lastProfile,
      user: {
        ...this.lastProfile.user,
        avatarUrl: avatarUrl || null,
        avatarVersion,
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
      case 'contentmoderator':
        return 'Content Moderator';
      case 'territorymanager':
      case 'nationalmanager':
        return 'Territory Manager';
      case 'territoryassociate':
        return 'Territory Associate';
      case 'admin':
        return 'Administrator';
      default:
        return role;
    }
  }

  private normalizeRole(role: string): string {
    return role.replace(/[\s_-]/g, '').toLowerCase();
  }

  private isAdminRoleSet(roles: readonly string[]): boolean {
    return roles.some(role => this.normalizeRole(role) === 'admin');
  }

  private isContentModeratorRoleSet(roles: readonly string[]): boolean {
    return roles.some(role => this.normalizeRole(role) === 'contentmoderator');
  }

  private get isAdminProfile(): boolean {
    const roles = [
      this.lastProfile?.role?.roleCode,
      ...(this.lastProfile?.roles ?? []),
    ].filter((role): role is string => typeof role === 'string');

    return this.isAdminRoleSet(roles);
  }

  private get isContentModeratorProfile(): boolean {
    const roles = [
      this.lastProfile?.role?.roleCode,
      ...(this.lastProfile?.roles ?? []),
    ].filter((role): role is string => typeof role === 'string');

    return this.isContentModeratorRoleSet(roles);
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
      return 'Sign in with an authenticated admin app account to view settings.';
    }

    return this.toErrorMessage(error, 'Unable to load profile settings.');
  }

  private toErrorMessage(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse && typeof error.error?.message === 'string') {
      return error.error.message;
    }

    return fallback;
  }

  private normalizeOptionalString(value: string | null | undefined): string {
    return value?.trim() ?? '';
  }

  private notifyStateChanged(): void {
    this.cdr.markForCheck();
  }
}
