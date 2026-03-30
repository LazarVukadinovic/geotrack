import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';

@Component({
  selector: 'app-interests',
  imports: [CommonModule],
  templateUrl: './interests.html',
  styleUrl: './interests.css',
})
export class Interests {
  maxSelections = 5;
  selectedInterests: string[] = [];

  interests = [
    'Food & Drink',
    'History',
    'Beaches',
    'Nature',
    'Museums',
    'Local Events',
    'Nightlife',
    'Scenic Walks',
    'Culture',
    'Adventure',
  ];

  constructor(private router: Router) {}

  toggleInterest(interest: string): void {
    if (this.isSelected(interest)) {
      this.selectedInterests = this.selectedInterests.filter((i) => i !== interest);
    } else if (this.selectedInterests.length < this.maxSelections) {
      this.selectedInterests = [...this.selectedInterests, interest];
    }
  }

  isSelected(interest: string): boolean {
    return this.selectedInterests.includes(interest);
  }

  onFinish(): void {
    if (this.selectedInterests.length === 0) return;
    // Save interests and navigate to main app
    this.router.navigate(['/']);
  }

  onSkip(): void {
    this.router.navigate(['/']);
  }

  goBack(): void {
    this.router.navigate(['/register']);
  }
}