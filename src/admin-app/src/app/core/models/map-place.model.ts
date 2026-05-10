export interface MapPlace {
  id: string;
  name: string;
  type: 'Place' | 'Location' | 'Destination';
  category: string;
  description: string;
  status: string;
  eventsToday: number;
  lat: number;
  lng: number;
  zone?: [number, number][];
  address?: string;
}
