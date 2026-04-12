export type ChatRole = 'user' | 'assistant';
export type ChatMessageType = 'text' | 'suggestion_cards';

export interface AgentRecommendationCard {
  id: string;
  title: string;
  description: string;
  image: string;
  rating: number;
  durationHours: number;
  distanceKm: number;
  isFavorite: boolean;
}

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
  cards?: AgentRecommendationCard[];
  /** Quick-reply pills shown below this message (welcome message only) */
  suggestionPills?: SuggestionPill[];
  timestamp: Date;
}
