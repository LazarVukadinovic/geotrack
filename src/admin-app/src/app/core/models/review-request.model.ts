export type RequestType   = 'Place';
export type RequestStatus = 'Pending' | 'Approved' | 'Rejected';
export interface ReviewRequest {
  id: string;
  title: string;
  type: RequestType;
  region: string;
  reqId: string;
  author: string;
  authorInitials: string;
  authorImg?: string;
  authorBg: string;
  date: string;
  status: RequestStatus;
  imageBg: string;
}
export interface BookingPartner {
  id: string;
  url: string;
}

export interface ReviewRequestDetail {
  id: string;
  reqId: string;
  internalId?: string;

  title: string;
  stayType: string;
  imageUrl?: string;
  imageBg: string;
  address?: string;
  city?: string;
  region?: string;
  country?: string;
  lat?: number | null;
  lng?: number | null;

  tags?: string[];
  amenities?: string[];

  description?: string;
  phone?: string;
  website?: string;
  bookingPartners?: BookingPartner[];

  type: 'Place' | 'Location' | 'Event';
  author: string;
  authorInitials: string;
  authorBg: string;
  authorEmail?: string;
  date: string;
  status: 'Pending' | 'Approved' | 'Rejected';
}
