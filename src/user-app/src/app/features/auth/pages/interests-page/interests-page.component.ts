import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';

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

  constructor(private router: Router) {}

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
    const labelToId: Record<string, string> = {
      'Food & Drink': 'food',
      'History': 'history',
      'Beaches': 'beaches',
      'Nature': 'nature',
      'Museums': 'museums',
      'Local Events': 'events',
      'Nightlife': 'nightlife',
      'Scenic Walks': 'walks',
      'Festivals': 'festivals',
      'Adventure': 'adventure',
      'Shopping': 'shopping',
      'Culture': 'culture',
    };

    const ids = this.selectedInterests
      .map(label => labelToId[label])
      .filter(Boolean);

    localStorage.setItem('userInterests', JSON.stringify(ids));
    this.router.navigate(['/home']);
  }

  onSkip(): void {
    this.router.navigate(['/']);
  }

  goBack(): void {
    this.router.navigate(['/profile/register']);
  }
}
