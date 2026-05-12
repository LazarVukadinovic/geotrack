import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';

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

  constructor(private router: Router, private readonly auth: AuthService) {}

  get displayUserName(): string {
    return this.auth.fullName || this.userName;
  }

  get displayUserRole(): string {
    return this.auth.getUser()?.role || this.userRole;
  }

  goBack(): void {
    if (this.backRoute) {
      this.router.navigate([this.backRoute]);
    } else {
      history.back();
    }
  }
}
