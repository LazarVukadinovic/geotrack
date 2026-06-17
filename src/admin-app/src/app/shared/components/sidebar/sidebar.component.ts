import {
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  HostListener,
  Input,
  OnChanges,
  OnInit,
  Output,
  SimpleChanges,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter, take } from 'rxjs/operators';
import { AuthService } from '../../../core/services/auth.service';
import { RegionalAssignedRegion } from '../../../core/models/dashboard.model';
import { TerritoryDashboardService } from '../../../core/services/territory-dashboard.service';
import { RegionalManagerSidebarStateService } from '../../../core/services/regional-manager-sidebar-state.service';

export interface SidebarNavItem {
  label: string;
  path: string;
  icon: string;
  exact?: boolean;
  activePathExclusions?: string[];
  badge?: number;
}

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './sidebar.component.html',
  styleUrl: './sidebar.component.css',
})
export class SidebarComponent implements OnInit, OnChanges {
  @Input() region = '';
  @Input() regions: RegionalAssignedRegion[] = [];
  @Input() activeRegionId: number | null = null;
  @Input() reportedReviewsCount: number | null = null;
  @Input() supportReportsCount: number | null = null;
  @Input() regionLoading = false;
  @Output() regionChange = new EventEmitter<number>();

  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly territoryDashboard = inject(TerritoryDashboardService);
  private readonly regionalSidebarState = inject(RegionalManagerSidebarStateService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly elementRef = inject<ElementRef<HTMLElement>>(ElementRef);

  private readonly currentUrl = signal(this.router.url);
  private loadedTerritories: RegionalAssignedRegion[] = [];
  private loadedActiveTerritoryId: number | null = null;
  private territoryLoading = false;
  readonly regionPickerMenuId = 'sidebar-region-picker-menu';
  regionPickerOpen = false;
  highlightedPickerIndex = -1;

  constructor() {
    this.router.events
      .pipe(filter(e => e instanceof NavigationEnd))
      .subscribe(e => this.currentUrl.set((e as NavigationEnd).urlAfterRedirects));
  }

  ngOnInit(): void {
    this.syncRegionalSidebarState();

    if (!this.auth.canAccessTerritoryAdmin() || this.regions.length > 0) {
      return;
    }

    this.territoryLoading = true;
    this.territoryDashboard.getAssignedTerritories()
      .pipe(take(1))
      .subscribe({
        next: territories => {
          this.loadedTerritories = territories.map(territory => ({
            id: territory.id,
            name: territory.name,
            displayName: territory.displayName,
            statusLabel: territory.statusLabel,
          }));
          const storedId = this.territoryDashboard.getActiveTerritoryId();
          this.loadedActiveTerritoryId = storedId && this.loadedTerritories.some(item => item.id === storedId)
            ? storedId
            : this.loadedTerritories[0]?.id ?? null;
          if (this.loadedActiveTerritoryId) {
            this.territoryDashboard.setActiveTerritoryId(this.loadedActiveTerritoryId);
          }
          this.territoryLoading = false;
          this.cdr.markForCheck();
        },
        error: () => {
          this.territoryLoading = false;
          this.cdr.markForCheck();
        },
      });
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['activeRegionId'] || changes['regions'] || changes['region']) {
      this.syncRegionalSidebarState();
      this.syncPickerHighlight();
      if (!this.canSelectRegion) {
        this.closeRegionPicker();
      }
    }
  }

  isActive(path: string, exact = false): boolean {
    const url = this.currentUrl();
    if (exact) return url === path;
    return url === path || url.startsWith(path + '/') || url.startsWith(path + '?');
  }

  isItemActive(item: SidebarNavItem): boolean {
    if (item.activePathExclusions?.some(path => this.isActive(path))) {
      return false;
    }

    return this.isActive(item.path, item.exact);
  }

  get pickerLabel(): string {
    return this.auth.canAccessTerritoryAdmin() ? 'TERRITORY' : 'REGION';
  }

  get pickerValue(): string {
    return this.resolvedRegionLabel() ?? this.emptyPickerLabel;
  }

  get pickerDisplayValue(): string {
    const label = this.resolvedRegionLabel();
    return this.isPickerLoading && !label ? 'Loading...' : (label ?? this.emptyPickerLabel);
  }

  get canSelectRegion(): boolean {
    return this.pickerItems.length > 1;
  }

  get highlightedOptionId(): string | null {
    const item = this.pickerItems[this.highlightedPickerIndex];
    return item ? this.regionOptionId(item.id) : null;
  }

  get pickerAriaLabel(): string {
    const label = this.isTerritory ? 'Active territory' : 'Active region';
    return `${label}: ${this.pickerDisplayValue}`;
  }

  get isAdministrator(): boolean {
    return this.auth.isAdministrator();
  }

  get isContentModerator(): boolean {
    return this.auth.isContentModerator();
  }

  get isTerritory(): boolean {
    return this.auth.canAccessTerritoryAdmin();
  }

  get administratorMainNav(): SidebarNavItem[] {
    return [
      { label: 'Admin Dashboard', path: '/admin-dashboard', icon: 'dashboard', exact: true },
      { label: 'Territory Management', path: '/admin-territories', icon: 'globe' },
      { label: 'User Management', path: '/admin-users', icon: 'users' },
      {
        label: 'Support Reports',
        path: '/admin-support-tickets',
        icon: 'support',
        badge: this.supportReportsCount || undefined,
      },
    ];
  }

  get contentModeratorMainNav(): SidebarNavItem[] {
    return [
      { label: 'Dashboard', path: '/content-moderator/dashboard', icon: 'dashboard', exact: true },
      { label: 'Destinations', path: '/content-moderator/destinations', icon: 'flag' },
      { label: 'Locations', path: '/content-moderator/locations', icon: 'pin' },
      { label: 'Places', path: '/content-moderator/places', icon: 'home' },
      { label: 'Events', path: '/content-moderator/events', icon: 'calendar' },
      {
        label: 'Review Management',
        path: '/content-moderator/review-management',
        icon: 'review',
        badge: this.reportedReviewsCount || undefined,
      },
    ];
  }

  get territoryMainNav(): SidebarNavItem[] {
    return [
      { label: 'Territory Overview', path: '/territory/overview', icon: 'dashboard', exact: true },
      {
        label: 'Regional Management',
        path: '/territory/regions',
        icon: 'map',
        activePathExclusions: ['/territory/regions/map'],
      },
      { label: 'Region Map', path: '/territory/regions/map', icon: 'map' },
      { label: 'User Administration', path: '/territory/users', icon: 'users' },
    ];
  }

  get territoryAnalyticsNav(): SidebarNavItem[] {
    return [
      { label: 'Analytics & Insights', path: '/territory/analytics', icon: 'chart' },
    ];
  }

  get mainNav(): SidebarNavItem[] {
    return [
      { label: 'Dashboard', path: '/dashboard', icon: 'dashboard', exact: true },
      {
        label: 'Requests Queue',
        path: '/requests',
        icon: 'inbox',
        badge: this.requestsQueueBadge,
      },
      { label: 'Map', path: '/map', icon: 'map' },
    ];
  }

  contentNav: SidebarNavItem[] = [
    { label: 'Destinations', path: '/destinations', icon: 'flag' },
    { label: 'Locations', path: '/locations', icon: 'pin' },
    { label: 'Places', path: '/places', icon: 'home' },
    { label: 'Events', path: '/events', icon: 'calendar' },
  ];

  analyticsNav: SidebarNavItem[] = [
    { label: 'Analytics & Insights', path: '/analytics', icon: 'chart' },
  ];

  signOut(): void {
    this.auth.logout();
    this.router.navigate(['/login']);
  }

  toggleRegionPicker(event?: Event): void {
    event?.stopPropagation();
    if (!this.canSelectRegion || this.isPickerLoading) {
      return;
    }

    if (this.regionPickerOpen) {
      this.closeRegionPicker();
      return;
    }

    this.openRegionPicker();
  }

  selectPickerItem(regionId: number, event?: Event): void {
    event?.stopPropagation();
    const normalizedRegionId = this.toPositiveRegionId(regionId);
    if (!normalizedRegionId) {
      this.closeRegionPicker();
      return;
    }

    if (normalizedRegionId === this.resolvedActiveId) {
      this.closeRegionPicker();
      return;
    }

    if (this.isTerritory) {
      this.loadedActiveTerritoryId = normalizedRegionId;
      this.territoryDashboard.setActiveTerritoryId(normalizedRegionId);
    } else if (this.shouldUseRegionalSidebarState) {
      this.regionalSidebarState.selectRegion(normalizedRegionId, this.pickerItems);
    }

    this.regionChange.emit(normalizedRegionId);
    this.closeRegionPicker();
  }

  onRegionTriggerKeydown(event: KeyboardEvent): void {
    if (!this.canSelectRegion || this.isPickerLoading) {
      return;
    }

    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        if (!this.regionPickerOpen) {
          this.openRegionPicker(true);
        } else {
          this.movePickerHighlight(1, true);
        }
        break;
      case 'ArrowUp':
        event.preventDefault();
        if (!this.regionPickerOpen) {
          this.openRegionPicker(true);
        } else {
          this.movePickerHighlight(-1, true);
        }
        break;
      case 'Home':
        event.preventDefault();
        this.openRegionPicker();
        this.setPickerHighlight(0, true);
        break;
      case 'End':
        event.preventDefault();
        this.openRegionPicker();
        this.setPickerHighlight(this.pickerItems.length - 1, true);
        break;
      case 'Enter':
      case ' ':
        event.preventDefault();
        if (!this.regionPickerOpen) {
          this.openRegionPicker(true);
        } else if (this.highlightedPickerIndex >= 0) {
          const item = this.pickerItems[this.highlightedPickerIndex];
          if (item) {
            this.selectPickerItem(item.id);
          }
        }
        break;
      case 'Escape':
        if (this.regionPickerOpen) {
          event.preventDefault();
          this.closeRegionPicker();
        }
        break;
    }
  }

  onRegionOptionKeydown(event: KeyboardEvent, index: number): void {
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        this.setPickerHighlight(index, false);
        this.movePickerHighlight(1, true);
        break;
      case 'ArrowUp':
        event.preventDefault();
        this.setPickerHighlight(index, false);
        this.movePickerHighlight(-1, true);
        break;
      case 'Home':
        event.preventDefault();
        this.setPickerHighlight(0, true);
        break;
      case 'End':
        event.preventDefault();
        this.setPickerHighlight(this.pickerItems.length - 1, true);
        break;
      case 'Enter':
      case ' ':
        event.preventDefault();
        this.selectPickerItem(this.pickerItems[index]?.id ?? 0);
        break;
      case 'Escape':
        event.preventDefault();
        this.closeRegionPicker();
        this.focusRegionTrigger();
        break;
    }
  }

  regionOptionId(regionId: number): string {
    return `${this.regionPickerMenuId}-option-${regionId}`;
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    const target = event.target instanceof Node ? event.target : null;
    if (this.regionPickerOpen && target && !this.elementRef.nativeElement.contains(target)) {
      this.closeRegionPicker();
    }
  }

  @HostListener('document:keydown.escape', ['$event'])
  onDocumentEscape(event: Event): void {
    if (!this.regionPickerOpen) {
      return;
    }

    event.preventDefault();
    this.closeRegionPicker();
  }

  private resolvedRegionLabel(): string | null {
    const activeRegion = this.pickerItems.find((region) => region.id === this.resolvedActiveId);
    const sharedActiveRegion = this.shouldUseRegionalSidebarState ? this.regionalSidebarState.activeRegion() : null;
    const value = this.shouldUseRegionalSidebarState
      ? activeRegion?.displayName || sharedActiveRegion?.displayName || this.region || this.auth.getUser()?.region || ''
      : this.region || activeRegion?.displayName || sharedActiveRegion?.displayName || this.auth.getUser()?.region || '';
    return value.trim() || null;
  }

  get pickerItems(): RegionalAssignedRegion[] {
    if (this.regions.length > 0) {
      return this.regions;
    }

    if (this.shouldUseRegionalSidebarState) {
      return this.regionalSidebarState.assignedRegions();
    }

    return this.loadedTerritories;
  }

  get resolvedActiveId(): number | null {
    return this.activeRegionId ??
      (this.shouldUseRegionalSidebarState ? this.regionalSidebarState.activeRegionId() : null) ??
      this.loadedActiveTerritoryId;
  }

  get isPickerLoading(): boolean {
    return this.regionLoading ||
      this.territoryLoading ||
      (this.shouldUseRegionalSidebarState && this.regionalSidebarState.loading() && this.pickerItems.length === 0);
  }

  private get emptyPickerLabel(): string {
    return this.isTerritory ? 'No assigned territories' : 'No assigned regions';
  }

  private get shouldUseRegionalSidebarState(): boolean {
    return this.auth.getUser()?.activeRoleCode === 'RegionalManager' && !this.isAdministrator && !this.isContentModerator && !this.isTerritory;
  }

  private get requestsQueueBadge(): number | undefined {
    if (!this.shouldUseRegionalSidebarState) {
      return undefined;
    }

    const count = this.regionalSidebarState.pendingRequestsCount();
    return typeof count === 'number' && count > 0 ? count : undefined;
  }

  private openRegionPicker(focusHighlighted = false): void {
    if (!this.canSelectRegion || this.isPickerLoading) {
      return;
    }

    this.regionPickerOpen = true;
    this.syncPickerHighlight();
    this.cdr.markForCheck();

    if (focusHighlighted) {
      this.focusHighlightedPickerOption();
    }
  }

  private closeRegionPicker(): void {
    if (!this.regionPickerOpen && this.highlightedPickerIndex === -1) {
      return;
    }

    this.regionPickerOpen = false;
    this.highlightedPickerIndex = -1;
    this.cdr.markForCheck();
  }

  private syncPickerHighlight(): void {
    const activeId = this.resolvedActiveId;
    const activeIndex = this.pickerItems.findIndex(item => item.id === activeId);
    this.highlightedPickerIndex = activeIndex >= 0 ? activeIndex : (this.pickerItems.length > 0 ? 0 : -1);
  }

  private movePickerHighlight(direction: 1 | -1, focusOption: boolean): void {
    const itemCount = this.pickerItems.length;
    if (itemCount === 0) {
      this.highlightedPickerIndex = -1;
      return;
    }

    const currentIndex = this.highlightedPickerIndex >= 0 ? this.highlightedPickerIndex : 0;
    const nextIndex = (currentIndex + direction + itemCount) % itemCount;
    this.setPickerHighlight(nextIndex, focusOption);
  }

  private setPickerHighlight(index: number, focusOption: boolean): void {
    const itemCount = this.pickerItems.length;
    if (itemCount === 0) {
      this.highlightedPickerIndex = -1;
      return;
    }

    this.highlightedPickerIndex = Math.min(Math.max(index, 0), itemCount - 1);
    this.cdr.markForCheck();

    if (focusOption) {
      this.focusHighlightedPickerOption();
    }
  }

  private focusHighlightedPickerOption(): void {
    const id = this.highlightedOptionId;
    if (!id) {
      return;
    }

    setTimeout(() => document.getElementById(id)?.focus(), 0);
  }

  private focusRegionTrigger(): void {
    setTimeout(() => {
      this.elementRef.nativeElement.querySelector<HTMLButtonElement>('.region-picker--selectable')?.focus();
    }, 0);
  }

  private syncRegionalSidebarState(): void {
    if (!this.shouldUseRegionalSidebarState) {
      return;
    }

    const explicitRegions = this.regions.length > 0 ? this.regions : null;
    if (explicitRegions) {
      this.regionalSidebarState.setAssignedRegions(explicitRegions);
    }

    const activeRegionId = this.toPositiveRegionId(this.activeRegionId);
    if (activeRegionId) {
      this.regionalSidebarState.selectRegion(
        activeRegionId,
        explicitRegions ?? this.regionalSidebarState.assignedRegions(),
      );
      return;
    }

    this.regionalSidebarState.ensureLoaded();
  }

  private toPositiveRegionId(value: unknown): number | null {
    const parsed = Number(value);
    return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
  }
}
