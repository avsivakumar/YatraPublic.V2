import { ComponentHost } from './ComponentHost';
import { componentLimits } from '../componentLimits';
import type { ReactNode } from 'react';
import type { FormRequestPayload, TextPayload, YatraErrorPayload, YatraMessage } from '../types';

interface MessageListProps {
  messages: YatraMessage[];
  renderForm: (message: YatraMessage<FormRequestPayload>) => ReactNode;
  componentsReplaying?: boolean;
}

export function MessageList({ messages, renderForm, componentsReplaying = false }: MessageListProps) {
  if (messages.length === 0) {
    return <div className="empty-state">No messages yet</div>;
  }

  const visibleComponents = new Set(messages.filter(message => message.type === 'component.request')
    .slice(-componentLimits.maxHostedComponents).map(message => message.messageId));

  return (
    <ol className="message-list">
      {messages.filter(message => message.type !== 'form.response').map((message) => (
        <li key={message.messageId} className={`message-row message-row-${message.type.replace('.', '-')}`}>
          {message.type === 'component.request' && !visibleComponents.has(message.messageId)
            ? <div className="unsupported-message" role="status">Component rendering limit reached. This older interaction is not displayed.</div>
            : renderMessage(message, renderForm, messages, componentsReplaying)}
        </li>
      ))}
    </ol>
  );
}

function renderMessage(
  message: YatraMessage,
  renderForm: (message: YatraMessage<FormRequestPayload>) => ReactNode,
  messages: YatraMessage[],
  componentsReplaying: boolean,
) {
  switch (message.type) {
    case 'user.text':
      return <TextBubble align="right" label="You" text={(message.payload as TextPayload).text} />;
    case 'agent.text':
      return <TextBubble align="left" label="Yatra" text={(message.payload as TextPayload).text} />;
    case 'form.request':
      return renderForm(message as YatraMessage<FormRequestPayload>);
    case 'component.request':
      return <ComponentHost payload={message.payload} message={message} messages={messages} replaying={componentsReplaying} />;
    case 'component.response':
      return <div className="status-message">Component response recorded</div>;
    case 'status':
      return <div className="status-message">{(message.payload as { text?: string }).text ?? 'Status update'}</div>;
    case 'error': {
      const error = message.payload as YatraErrorPayload;
      return (
        <div className="error-message" role="alert">
          <strong>{error.code}</strong>
          <span>{error.message}</span>
        </div>
      );
    }
    default:
      console.warn('Unsupported Yatra message type', message.type);
      return <div className="unsupported-message">Unsupported message</div>;
  }
}

function TextBubble({ align, label, text }: { align: 'left' | 'right'; label: string; text: string }) {
  return (
    <article className={`message-bubble message-bubble-${align}`}>
      <span>{label}</span>
      <p>{text}</p>
    </article>
  );
}


