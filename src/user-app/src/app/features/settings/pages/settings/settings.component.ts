import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { AuthService } from '../../../../core/services/auth.service';

interface MeResponse {
  id: number;
  username: string;
  email: string;
  firstName: string;
  lastName: string;
  avatarUrl?: string;
}

@Component({
  selector: 'app-settings',
  imports: [CommonModule],
  templateUrl: './settings.component.html',
  styleUrl: './settings.component.css',
})
export class SettingsComponent {
  fullName = '';
  email = '';
  language = 'English (US)';

  constructor(private router: Router, private authService: AuthService, private http: HttpClient) {
    this.http.get<MeResponse>('/api/user/me').subscribe({
      next: (res) => {
        const name = `${res.firstName} ${res.lastName}`.trim();
        this.fullName = name || res.username;
        this.email = res.email;
      },
      error: () => {
        const saved = localStorage.getItem('userProfile');
        if (saved) {
          const p = JSON.parse(saved);
          this.fullName = p.fullName ?? '';
          this.email = p.email ?? '';
        }
      },
    });
  }

  goBack():          void { this.router.navigate(['/profile']); }
  goToProfile():     void { this.router.navigate(['/profile/edit'], { queryParams: { from: 'settings' } }); }
  changePassword():  void { this.router.navigate(['/change-password']); }
  changeLanguage():  void { console.log('Language'); }
  openNotifications(): void { console.log('Notifications'); }
  openPrivacy():     void { console.log('Privacy'); }
  openInterests():   void { this.router.navigate(['/settings/interests'], { queryParams: { from: 'settings' } }); }
  openHelp():        void { console.log('Help'); }
  contactSupport():  void { console.log('Support'); }
  openTerms():       void { console.log('Terms'); }

  logOut(): void {
    this.authService.logout();
    this.router.navigate(['/profile']);
  }
}