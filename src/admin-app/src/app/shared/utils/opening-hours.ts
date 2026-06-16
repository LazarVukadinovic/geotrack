export const OPEN_24_HOURS_LABEL = 'Open 24 hours';

export interface OperatingHourLike {
  dayOfWeek?: string;
  name?: string;
  day?: string;
  isOpen?: boolean;
  open?: boolean;
  openTime?: string | null;
  closeTime?: string | null;
  hours?: string | null;
}

export function isOpen24HoursText(value: string | null | undefined): boolean {
  return /^open\s+24\s*(?:hours?|h)$/i.test((value ?? '').trim());
}

export function isOpen24HoursSchedule(hours: readonly OperatingHourLike[]): boolean {
  if (hours.length < 7) return false;

  return hours.every(hour => {
    const isOpen = hour.isOpen ?? hour.open;
    if (isOpen === true && hour.openTime === '00:00' && hour.closeTime === '00:00') {
      return true;
    }

    const normalizedHours = (hour.hours ?? '').toLowerCase().replace(/\s+/g, ' ').trim();
    return normalizedHours === '00:00 - 00:00' || normalizedHours === 'open 24 hours';
  });
}

export function hasDisplayableOperatingHours(hours: readonly OperatingHourLike[]): boolean {
  return hours.length > 0;
}
