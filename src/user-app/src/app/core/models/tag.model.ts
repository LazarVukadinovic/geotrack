/** User-facing interest topic returned by /api/interests/onboarding. */
export interface Tag {
  id: number;
  name: string;
  slug: string;
  description?: string;
  iconKey?: string;
  group?: string;
  sortOrder?: number;
}
