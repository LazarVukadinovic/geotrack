import { Injectable } from '@angular/core';

export interface NationalStat {
  label: string;
  value: string;
  changeFigure: string;
  up: boolean;
  icon: 'globe' | 'users' | 'flag' | 'pin' | 'home' | 'calendar';
}

export interface RegionCard {
  name: string;
  manager: string;
  destinations: number;
  locations: number;
  places: number;
  events: number;
  activityScore: number;
  initials: string[];
}

export interface TopRegion {
  rank: number;
  name: string;
  visitsFigure: string;
  change: string;
}

export interface TopDestination {
  name: string;
  region: string;
  viewsFigure: string;
}

export interface TopLocation {
  name: string;
  region: string;
  viewsFigure: string;
}

export interface TopPlace {
  name: string;
  region: string;
  rating: number;
}

export interface HotEvent {
  name: string;
  date: string;
  attendeesFigure: string;
}

@Injectable({ providedIn: 'root' })
export class NationalDashboardService {

  // --- MOCK: zameniti sa HTTP pozivima ---

  getStats(): NationalStat[] {
    return [
      { label: 'Total Regions',     value: '7',     changeFigure: '0',    up: true,  icon: 'globe'    },
      { label: 'Active Users',       value: '318',   changeFigure: '+9%',  up: true,  icon: 'users'    },
      { label: 'Total Destinations', value: '124',   changeFigure: '+6',   up: true,  icon: 'flag'     },
      { label: 'Total Locations',    value: '2,840', changeFigure: '+87',  up: true,  icon: 'pin'      },
      { label: 'Total Places',       value: '1,563', changeFigure: '+44',  up: true,  icon: 'home'     },
      { label: 'Total Events',       value: '93',    changeFigure: '-5',   up: false, icon: 'calendar' },
    ];
  }

  getRegions(): RegionCard[] {
    return [
      {
        name: 'Šumadija i Zapadna Srbija',
        manager: 'Nikola Petrović',
        destinations: 28, locations: 620, places: 340, events: 22,
        activityScore: 91,
        initials: ['NP', 'MJ', 'AK'],
      },
      {
        name: 'Vojvodina',
        manager: 'Ana Kovačević',
        destinations: 21, locations: 510, places: 280, events: 18,
        activityScore: 84,
        initials: ['AK', 'DS', 'BL'],
      },
      {
        name: 'Beograd',
        manager: 'Stefan Đorđević',
        destinations: 14, locations: 480, places: 310, events: 31,
        activityScore: 95,
        initials: ['SĐ', 'IV', 'MM'],
      },
      {
        name: 'Južna i Istočna Srbija',
        manager: 'Milica Stanković',
        destinations: 22, locations: 390, places: 210, events: 9,
        activityScore: 68,
        initials: ['MS', 'RP', 'TN'],
      },
      {
        name: 'Raška i Stari Ras',
        manager: 'Jovan Milošević',
        destinations: 19, locations: 340, places: 185, events: 8,
        activityScore: 77,
        initials: ['JM', 'KV', 'LP'],
      },
      {
        name: 'Podunavlje i Braničevo',
        manager: 'Dragana Nikolić',
        destinations: 20, locations: 500, places: 238, events: 5,
        activityScore: 82,
        initials: ['DN', 'PJ', 'SA'],
      },
    ];
  }

  getTopRegions(): TopRegion[] {
    return [
      { rank: 1, name: 'Beograd',                   visitsFigure: '420K', change: '+18%' },
      { rank: 2, name: 'Šumadija i Zapadna Srbija', visitsFigure: '310K', change: '+11%' },
      { rank: 3, name: 'Raška i Stari Ras',         visitsFigure: '195K', change: '+14%' },
    ];
  }

  getTopDestinations(): TopDestination[] {
    return [
      { name: 'Đavolja Varoš', region: 'Južna i Istočna Srbija',    viewsFigure: '98K' },
      { name: 'Tara',           region: 'Šumadija i Zapadna Srbija', viewsFigure: '87K' },
      { name: 'Fruška Gora',    region: 'Vojvodina',                  viewsFigure: '74K' },
    ];
  }

  getTopLocations(): TopLocation[] {
    return [
      { name: 'Tvrđava Golubac', region: 'Podunavlje i Braničevo', viewsFigure: '61K' },
      { name: 'Resavska pećina', region: 'Podunavlje i Braničevo', viewsFigure: '54K' },
      { name: 'Uvac kanjon',     region: 'Raška i Stari Ras',       viewsFigure: '48K' },
    ];
  }

  getTopPlaces(): TopPlace[] {
    return [
      { name: 'Manastir Studenica', region: 'Raška i Stari Ras',        rating: 4.9 },
      { name: 'Kalemegdan',          region: 'Beograd',                   rating: 4.8 },
      { name: 'Zlatibor centar',     region: 'Šumadija i Zapadna Srbija', rating: 4.7 },
    ];
  }

  getHotEvents(): HotEvent[] {
    return [
      { name: 'Exit Festival',            date: '2025-07-10', attendeesFigure: '200K' },
      { name: 'Dragačevski sabor — Guča', date: '2025-08-07', attendeesFigure: '800K' },
      { name: 'Nišville Jazz Festival',   date: '2025-08-08', attendeesFigure: '120K' },
    ];
  }
}