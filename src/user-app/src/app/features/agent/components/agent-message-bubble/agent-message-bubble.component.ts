import { Component, input } from '@angular/core';
import type { ChatMessage } from '../../models/agent.model';

@Component({
  selector: 'app-agent-message-bubble',
  standalone: true,
  imports: [],
  templateUrl: './agent-message-bubble.component.html',
  styleUrl: './agent-message-bubble.component.scss',
})
export class AgentMessageBubbleComponent {
  readonly message = input.required<ChatMessage>();
}
