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

  readonly language = 'English (US)';

  constructor(private router: Router, private authService: AuthService) {}

  goBack():            void { this.router.navigate(['/profile']); }
  goToProfile():       void { this.router.navigate(['/profile/edit'], { queryParams: { from: 'settings' } }); }
  changePassword():    void { this.router.navigate(['/change-password']); }
  changeLanguage():    void { console.log('Language'); }
  openNotifications(): void { console.log('Notifications'); }
  openPrivacy():       void { console.log('Privacy'); }
  openInterests():     void { this.router.navigate(['/settings/interests'], { queryParams: { from: 'settings' } }); }
  openHelp():          void { console.log('Help'); }
  contactSupport():    void { console.log('Support'); }
  openTerms():         void { console.log('Terms'); }

  logOut(): void {
    this.authService.logout();
    this.router.navigate(['/profile']);
  }
}