import { CommonModule } from '@angular/common';
import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { SettingsSubpageShellComponent } from '../../components/settings-subpage-shell/settings-subpage-shell.component';

interface LegalSection {
  title: string;
  body: string[];
}

const LEGAL_SECTIONS: LegalSection[] = [
  {
    title: 'Terms of Service',
    body: [
      'GeoTrack is designed to help you discover destinations, save places, plan local outings, and explore recommendations that match your interests.',
      'You are responsible for the choices you make while using maps, saved collections, travel suggestions, and Ask Agent responses. Always confirm important travel details such as opening hours, ticket availability, and safety guidance before acting on them.',
      'Please keep your account credentials secure, use respectful language when sending support requests or submitting reviews, and avoid uploading or sharing content you do not have the right to use.',
    ],
  },
  {
    title: 'Account and Saved Content',
    body: [
      'Your GeoTrack account stores core profile details, interests, favorites, and collections so your planning progress carries across sessions.',
      'If you choose to update your password or adjust notification preferences, those changes are saved to your account and applied the next time related features are available.',
      'Saved places and travel collections are private to your account unless GeoTrack clearly introduces a sharing feature in a future release.',
    ],
  },
  {
    title: 'Privacy Policy',
    body: [
      'GeoTrack uses account information, saved places, and interest tags to personalize recommendations and make destination browsing more relevant.',
      'When precise location access is granted, GeoTrack uses those coordinates for the current browser session only. Precise GPS coordinates are not stored in long-term browser storage.',
      'If precise location is unavailable, GeoTrack asks before estimating your area from your network connection. Approximate coordinates are also session-only, while your accept or not-now choice can be remembered on this device.',
      'Support requests you submit in-app are stored with your account so the GeoTrack team can review and respond during testing and product iteration.',
    ],
  },
  {
    title: 'Travel Data and Recommendations',
    body: [
      'Listings, maps, and AI-assisted suggestions are provided to help with planning, but they can change as venues update schedules, local conditions shift, or destination content evolves.',
      'GeoTrack works best as a discovery and planning companion, not as the sole source of truth for transport, safety, or booking decisions.',
    ],
  },
];

@Component({
  selector: 'app-terms-privacy-page',
  standalone: true,
  imports: [CommonModule, SettingsSubpageShellComponent],
  templateUrl: './terms-privacy-page.component.html',
  styleUrl: './terms-privacy-page.component.css',
})
export class TermsPrivacyPageComponent {
  private readonly router = inject(Router);

  protected readonly sections = LEGAL_SECTIONS;

  protected openSupport(): void {
    void this.router.navigate(['/settings/contact-support']);
  }
}
