import type { AgentRecommendationCard, ChatMessage, SuggestionPill } from '../models/agent.model';

const u = (id: string, w = 640) =>
  `https://images.unsplash.com/${id}?ixlib=rb-4.0.3&auto=format&fit=crop&w=${w}&q=75`;

// ─── Suggestion pills ────────────────────────────────────────────────────────

export const AGENT_SUGGESTION_PILLS: SuggestionPill[] = [
  { label: 'Top Attractions', prompt: 'Show me the top attractions in Dubrovnik' },
  { label: 'Best Restaurants', prompt: 'What are the best restaurants in Dubrovnik?' },
  { label: 'Scenic Views', prompt: 'Where can I find the best scenic views?' },
  { label: 'Weekend Plans', prompt: "I'm looking for fun things to do this weekend, maybe some nice views and good food." },
  { label: 'Beaches', prompt: 'What are the best beaches near Dubrovnik?' },
];

// ─── Welcome message ─────────────────────────────────────────────────────────

export const AGENT_WELCOME_MESSAGE: ChatMessage = {
  id: 'welcome',
  role: 'assistant',
  type: 'text',
  text: 'Hi there! I am your AI travel assistant. How can I help you plan your trip today?',
  suggestionPills: AGENT_SUGGESTION_PILLS,
  timestamp: new Date(),
};

// ─── Mock recommendation cards ───────────────────────────────────────────────

export const MOCK_WEEKEND_CARDS: AgentRecommendationCard[] = [
  {
    id: 'plan-1',
    title: 'Scenic Walk + Dinner',
    description: 'Walk the historic city walls at golden hour, followed by a reservation at an elegant seafood restaurant nearby.',
    image: u('photo-1555990793-da11153b6c89'),
    rating: 4.9,
    durationHours: 3.5,
    distanceKm: 1.2,
    isFavorite: false,
  },
  {
    id: 'plan-2',
    title: 'Sunset Plan',
    description: 'Enjoy traditional Dalmatian recipes with a wonderful terrace ambiance as the sun sets over the Adriatic.',
    image: u('photo-1559592413-7cec4d0cae2b'),
    rating: 4.8,
    durationHours: 2.0,
    distanceKm: 0.8,
    isFavorite: false,
  },
];

export const MOCK_ATTRACTION_CARDS: AgentRecommendationCard[] = [
  {
    id: 'attr-1',
    title: 'Old Town Walls',
    description: 'Walk the iconic medieval walls surrounding the old city with breathtaking Adriatic panoramas.',
    image: u('photo-1555990793-da11153b6c89'),
    rating: 4.9,
    durationHours: 2.0,
    distanceKm: 0.5,
    isFavorite: false,
  },
  {
    id: 'attr-2',
    title: 'Fort Lovrijenac',
    description: 'A dramatic cliff-top fortress offering sweeping views of the sea and the old town.',
    image: u('photo-1506905925346-21bda4d32df4'),
    rating: 4.7,
    durationHours: 1.5,
    distanceKm: 0.8,
    isFavorite: false,
  },
];

// ─── Mock response map ───────────────────────────────────────────────────────
//
// Key: keyword pattern. Value: factory returning a ChatMessage.
// Replace this map with a real API call when connecting the MCP backend.

type MockResponseEntry = {
  keywords: string[];
  build: () => Omit<ChatMessage, 'id' | 'timestamp'>;
};

export const MOCK_RESPONSE_ENTRIES: MockResponseEntry[] = [
  {
    keywords: ['weekend', 'plan', 'things to do', 'views and good food'],
    build: () => ({
      role: 'assistant',
      type: 'suggestion_cards',
      text: "Great choices! Dubrovnik is famous for its stunning views and excellent Mediterranean cuisine. Here are a couple of curated mini-itineraries for you:",
      cardsTitle: 'Suggested Weekend Plans',
      cards: MOCK_WEEKEND_CARDS.map(c => ({ ...c })),
    }),
  },
  {
    keywords: ['attraction', 'sight', 'top places', 'must see'],
    build: () => ({
      role: 'assistant',
      type: 'suggestion_cards',
      text: "Dubrovnik has some truly spectacular attractions. Here are the must-see spots:",
      cardsTitle: 'Top Attractions',
      cards: MOCK_ATTRACTION_CARDS.map(c => ({ ...c })),
    }),
  },
  {
    keywords: ['restaurant', 'food', 'eat', 'dine', 'cuisine'],
    build: () => ({
      role: 'assistant',
      type: 'text',
      text: "Dubrovnik offers an incredible culinary scene! Try fresh seafood at Nautika, or explore local konobas for traditional Dalmatian dishes like peka and black risotto. The Stradun promenade also has many wonderful café terraces.",
    }),
  },
  {
    keywords: ['beach', 'swim', 'coast'],
    build: () => ({
      role: 'assistant',
      type: 'text',
      text: "For beaches, Banje Beach is the most popular near the old town. Šulić Beach is quieter and more local. For a wilder experience, take a short ferry to Lokrum Island with its rocky coves and a small saltwater lake.",
    }),
  },
  {
    keywords: ['view', 'scenic', 'panorama', 'sunset'],
    build: () => ({
      role: 'assistant',
      type: 'text',
      text: "The best views are from the cable car at Mount Srđ — you can see the entire old town and coastline. The city walls themselves offer phenomenal panoramas, especially at sunset. Fort Lovrijenac is another hidden gem for views.",
    }),
  },
];

export const MOCK_FALLBACK_RESPONSE: Omit<ChatMessage, 'id' | 'timestamp'> = {
  role: 'assistant',
  type: 'text',
  text: "That sounds wonderful! I'd love to help you explore Dubrovnik more. Ask me about top attractions, restaurants, beaches, scenic viewpoints, or weekend plans.",
};
