import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { AuthService } from '../../../../core/services/auth.service';
import { UserService } from '../../../../core/services/user.service';

@Component({
  selector: 'app-settings',
  imports: [CommonModule],
  templateUrl: './settings.component.html',
  styleUrl: './settings.component.css',
})
export class SettingsComponent {
  // UserService is pre-populated by provideAppInitializer before any route
  // renders, so fullName() and email() are already set on first paint.
  protected readonly fullName = inject(UserService).fullName;
  protected readonly email    = inject(UserService).email;
  protected readonly avatarUrl = inject(UserService).avatarUrl;

  readonly language = 'English (US)';

  constructor(private router: Router, private authService: AuthService) {}

  goBack():            void { this.router.navigate(['/profile']); }
  goToProfile():       void { this.router.navigate(['/profile/edit'], { queryParams: { from: 'settings' } }); }
  changePassword():    void { this.router.navigate(['/settings/change-password']); }
  openPrivacy():       void { this.router.navigate(['/settings/privacy-settings']); }
  openAnalytics():     void { this.router.navigate(['/settings/analytics']); }
  openInterests():     void { this.router.navigate(['/settings/interests'], { queryParams: { from: 'settings' } }); }
  openHelp():          void { this.router.navigate(['/settings/help-center']); }
  contactSupport():    void { this.router.navigate(['/settings/contact-support']); }
  openTerms():         void { this.router.navigate(['/settings/terms-and-privacy']); }

  logOut(): void {
    this.authService.logout();
    this.router.navigate(['/profile']);
  }
}
