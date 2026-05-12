export type KpiIcon = 'eye' | 'users' | 'link' | 'star' | 'bookmark' | 'chat';

export type RegionStatus =
  | 'Active / Fast'
  | 'Active / Normal'
  | 'Processing Delays'
  | 'Inactive';

// ── KPI summary card ─────────────────────────────────────────────────────────
export interface NationalKpi {
  label:  string;
  value:  string;   // formatted string, e.g. "2.4M", "4.5/5"
  change: string;   // formatted string, e.g. "+12.5%", "+0.2"
  up:     boolean;
  icon:   KpiIcon;
}

// ── Top/fastest region highlight ─────────────────────────────────────────────
export interface TopRegionHighlight {
  name:  string;
  score: number;   // 0–100
  note:  string;
}

export interface FastestGrowingRegion {
  name:   string;
  change: string;  // e.g. "+24% visitor growth"
}

// ── Top entity item (sidebar list) ───────────────────────────────────────────
export type EntityType = 'destination' | 'event' | 'place' | 'location';

export interface TopEntityItem {
  name: string;
  sub:  string;       // e.g. "Destination • Pacific NW"
  rank: number;
  icon: EntityType;
}

// ── Page views time-series (line chart) ──────────────────────────────────────
export interface PageViewSeries {
  label:  string;    // region name
  color:  string;    // hex
  points: number[];  // 12 monthly values, relative scale 0–100
}

// ── Top destination row (ranked bar list) ────────────────────────────────────
export interface TopDestination {
  name:   string;
  region: string;
  views:  number;    // thousands
}

// ── Top event row (ranked bar list) ──────────────────────────────────────────
export interface TopEvent {
  name:   string;
  region: string;
  saves:  number;    // thousands
}

// ── Content quality per region (bar + line chart) ────────────────────────────
export interface ContentQualityPoint {
  region:    string;
  reviews:   number;   // absolute count
  avgRating: number;   // 0.0–5.0
}

// ── Operational alert ────────────────────────────────────────────────────────
export type AlertType = 'no-manager' | 'processing-delay' | 'high-pending';

export interface OperationalAlert {
  type:       AlertType;
  regionName: string;
  message:    string;
}

// ── Regional performance table row ───────────────────────────────────────────
export interface RegionAnalyticRow {
  name:             string;
  refId:            string;
  pageViews:        string;   // formatted, e.g. "1.2M"
  visitors:         string;   // formatted, e.g. "450K"
  saves:            string;   // formatted, e.g. "85K"
  extClicks:        string;   // formatted, e.g. "42K"
  totalReviews:     string;   // formatted, e.g. "18K"
  avgRating:        number;
  entities:         string;   // formatted "D / L / P / E", e.g. "45 / 182 / 315 / 18"
  status:           RegionStatus;
  qualityScore:     number;   // 0–100
  pendingRequests:  number;
  lastManagerLogin: string;   // e.g. "Today", "2 days ago", "N/A"
  approvedLast30d:  number;
  rejectedLast30d:  number;
  hasManager:       boolean;
}

// ── Top place row (ranked bar list) ─────────────────────────────────────────
export interface TopPlace {
  name:   string;
  region: string;
  views:  number;
}

// ── Top location row (ranked bar list) ───────────────────────────────────────
export interface TopLocation {
  name:   string;
  region: string;
  views:  number;
}

// ── Full response shape (mirrors what a real API would return) ────────────────
export interface NationalAnalyticsData {
  kpis:             NationalKpi[];
  topRegion:        TopRegionHighlight;
  fastestGrowing:   FastestGrowingRegion;
  topEntities:      TopEntityItem[];
  pageViewsData:    PageViewSeries[];
  topDestinations:  TopDestination[];
  topEvents:        TopEvent[];
  topPlaces:        TopPlace[];
  topLocations:     TopLocation[];
  contentQuality:   ContentQualityPoint[];
  regionRows:       RegionAnalyticRow[];
  alerts:           OperationalAlert[];
}