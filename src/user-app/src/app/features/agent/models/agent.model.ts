export type ChatRole = 'user' | 'assistant';
export type ChatMessageType = 'text' | 'suggestion_cards';
export type AgentKeyMode = 'saved' | 'session' | 'project-demo';
export type AgentResultEntityType = 'place' | 'stay' | 'event';

export interface AgentResultCard {
  entityType: AgentResultEntityType;
  id: number;
  title: string;
  subtitle?: string | null;
  description?: string | null;
  imageUrl?: string | null;
  locationName?: string | null;
  rating?: number | null;
  reviewCount?: number | null;
  distanceKm?: number | null;
  tags?: string[];
  dateText?: string | null;
  stayType?: string | null;
  placeType?: string | null;
  detailRoute: string;
}

export type AgentRecommendationCard = AgentResultCard;

export interface SuggestionPill {
  label: string;
  prompt: string;
}

export interface ChatMessage {
  id: string;
  role: ChatRole;
  type: ChatMessageType;
  text: string;
  /** Section heading shown above cards in the chat */
  cardsTitle?: string;
  /** Rich recommendation cards attached to this message */
  cards?: AgentResultCard[];
  /** Quick-reply pills shown below this message (welcome message only) */
  suggestionPills?: SuggestionPill[];
  timestamp: Date;
}

export interface AgentProviderModel {
  id: string;
  displayName: string;
  isDefault: boolean;
}

export interface AgentProvider {
  id: string;
  displayName: string;
  enabled: boolean;
  models: AgentProviderModel[];
  defaultModel: string | null;
  supportsSavedKey: boolean;
  supportsSessionKey: boolean;
  projectDemoKeyEnabled: boolean;
  hasSavedKey: boolean;
  keyLast4: string | null;
  keyUpdatedAt: string | null;
}

export interface AgentApiKeyMetadata {
  provider: string;
  keyLast4: string;
  displayName: string | null;
  createdAt: string;
  updatedAt: string;
  lastUsedAt: string | null;
}

export interface AgentMcpMetadata {
  enabled: boolean;
  url: string | null;
  transport: string;
  privateToolsEnabled: boolean;
}

export interface AgentProvidersResponse {
  providers: AgentProvider[];
  mcp: AgentMcpMetadata;
}

export interface AgentContextOptions {
  destinationId?: number;
  currentCity?: string;
  currentDestinationName?: string;
  currentLocationName?: string;
  language?: string;
}
