import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';

@Component({
  selector: 'app-topbar',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './topbar.component.html',
})
export class TopbarComponent {
  @Input() title = '';
  @Input() userName = 'Sarah Jenkins';
  @Input() userRole = 'Regional Manager';
  @Input() showBack = false;
  @Input() backRoute: string | null = null;

  constructor(private router: Router) {}

  goBack(): void {
    if (this.backRoute) {
      this.router.navigate([this.backRoute]);
    } else {
      history.back();
    }
  }
}