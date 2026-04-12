export interface EventTag {
  label: string;
}

export interface EventDetailInfo {
  date: string;
  timeRange: string;
  venue: string;
  ticket: string;
  duration: string;
  hostedBy: string;
}

export interface EventVenueInfo {
  description: string;
  arrivalTips: string;
  accessibility: string;
}

export interface EventSimilar {
  id: string;
  title: string;
  subtitle: string;
  image: string;
  isSaved: boolean;
}

export interface EventDetail {
  id: string;
  title: string;
  location: string;
  heroImage: string;
  tags: EventTag[];
  distance: string;
  about: string;
  info: EventDetailInfo;
  venueInfo: EventVenueInfo;
  similar: EventSimilar[];
  isSaved: boolean;
}
