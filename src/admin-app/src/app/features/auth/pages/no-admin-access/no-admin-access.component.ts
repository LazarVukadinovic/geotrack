import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { AuthService } from '../../../../core/services/auth.service';

@Component({
  selector: 'app-no-admin-access',
  standalone: true,
  templateUrl: './no-admin-access.component.html',
  styleUrl: './no-admin-access.component.css',
})
export class NoAdminAccessComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  goToUserApp(): void {
    globalThis.location.href = environment.userAppBaseUrl;
  }

  signOut(): void {
    this.auth.logout();
    void this.router.navigate(['/login'], { replaceUrl: true });
  }
}
