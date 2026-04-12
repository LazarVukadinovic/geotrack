import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { AuthService } from '../../../../core/services/auth.service';

@Component({
  selector: 'app-interests-page',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './interests-page.component.html',
  styleUrl: './interests-page.component.css',
})
export class InterestsPageComponent {
  selectedInterests: string[] = [];

  interests = [
    'Food & Drink',
    'History',
    'Beaches',
    'Nature',
    'Museums',
    'Local Events',
    'Nightlife',
    'Festivals',
    'Adventure',
    'Scenic Walks',
    'Culture',
    'Shopping',
  ];

  constructor(private router: Router, private authService: AuthService) {}

  toggleInterest(interest: string): void {
    if (this.isSelected(interest)) {
      this.selectedInterests = this.selectedInterests.filter((i) => i !== interest);
    } else {
      this.selectedInterests = [...this.selectedInterests, interest];
    }
  }

  isSelected(interest: string): boolean {
    return this.selectedInterests.includes(interest);
  }

  onFinish(): void {
    if (this.selectedInterests.length === 0) return;
    this.router.navigate(['/']);
  }

  onSkip(): void {
    this.router.navigate(['/']);
  }

  goBack(): void {
    this.router.navigate(['/profile/register']);
  }
}
