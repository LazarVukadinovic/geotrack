import { Component, inject } from '@angular/core';
import { AuthService } from '../../../../core/services/auth.service';
import { LoginPageComponent } from '../../../auth/pages/login-page/login-page.component';
import { ProfileComponent } from '../profile/profile.component';

@Component({
  selector: 'app-profile-tab',
  standalone: true,
  imports: [LoginPageComponent, ProfileComponent],
  template: `
    @if (isLoggedIn()) {
      <app-profile />
    } @else {
      <app-login-page />
    }
  `,
})
export class ProfileTabComponent {
  protected readonly isLoggedIn = inject(AuthService).isLoggedIn;
}
