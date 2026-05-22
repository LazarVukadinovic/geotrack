import { Component, ElementRef, HostListener, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';
import { AdminView } from '../../../core/models/admin-view.model';

@Component({
  selector: 'app-topbar',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './topbar.component.html',
})
export class TopbarComponent {
  @Input() title = '';
  @Input() userName = '';
  @Input() userRole = '';
  @Input() showBack = false;
  @Input() backRoute: string | null = null;
  failedAvatarUrl = '';
  profileMenuOpen = false;

  constructor(
    private router: Router,
    private readonly auth: AuthService,
    private readonly elementRef: ElementRef<HTMLElement>,
  ) {}

  get displayUserName(): string {
    return this.auth.fullName || this.userName;
  }

  get displayUserRole(): string {
    return this.auth.getUser()?.role || this.userRole;
  }

  get displayUserEmail(): string {
    return this.auth.getUser()?.email ?? '';
  }

  get displayAvatarUrl(): string {
    const avatarUrl = this.auth.getUser()?.avatarUrl ?? '';
    return avatarUrl && avatarUrl !== this.failedAvatarUrl ? avatarUrl : '';
  }

  get userInitials(): string {
    const user = this.auth.getUser();
    const parts = [
      user?.firstName ?? this.displayUserName.split(/\s+/)[0] ?? '',
      user?.lastName ?? this.displayUserName.split(/\s+/).slice(1).join(' ') ?? '',
    ]
      .map(part => part.trim())
      .filter(Boolean);

    if (parts.length > 0) {
      return parts
        .slice(0, 2)
        .map(part => part.charAt(0).toUpperCase())
        .join('');
    }

    return (this.displayUserEmail.trim().charAt(0) || '?').toUpperCase();
  }

  get availableViews(): AdminView[] {
    return this.auth.getAvailableAdminViews();
  }

  get showViewSwitcher(): boolean {
    return this.availableViews.length > 1;
  }

  get activeView(): AdminView | null {
    return this.auth.getActiveAdminView();
  }

  get avatarAlt(): string {
    const name = this.displayUserName || this.displayUserEmail || 'User';
    return `${name} profile image`;
  }

  onAvatarError(): void {
    this.failedAvatarUrl = this.displayAvatarUrl;
  }

  toggleProfileMenu(event: MouseEvent): void {
    event.stopPropagation();
    this.profileMenuOpen = !this.profileMenuOpen;
  }

  closeProfileMenu(): void {
    this.profileMenuOpen = false;
  }

  isActiveView(view: AdminView): boolean {
    return this.activeView?.roleCode === view.roleCode;
  }

  selectView(view: AdminView): void {
    const changed = this.auth.switchAdminView(view.roleCode);
    this.closeProfileMenu();

    if (changed || this.router.url !== view.dashboardRoute) {
      this.router.navigateByUrl(view.dashboardRoute);
    }
  }

  openSettings(): void {
    const settingsRoute = this.activeView?.settingsRoute ?? '/settings';
    this.closeProfileMenu();
    this.router.navigateByUrl(settingsRoute);
  }

  signOut(): void {
    this.closeProfileMenu();
    this.auth.logout();
    this.router.navigateByUrl('/login');
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    const target = event.target instanceof Node ? event.target : null;
    if (this.profileMenuOpen && target && !this.elementRef.nativeElement.contains(target)) {
      this.closeProfileMenu();
    }
  }

  @HostListener('document:keydown.escape', ['$event'])
  onEscape(event: Event): void {
    if (!this.profileMenuOpen) {
      return;
    }

    event.preventDefault();
    this.closeProfileMenu();
  }

  goBack(): void {
    if (this.backRoute) {
      this.router.navigate([this.backRoute]);
    } else {
      history.back();
    }
  }
}
