import type { HostedComponentProps } from '../../componentRegistry';
import type { StatusSummaryProps } from './validation';
export function StatusSummary({ title, description, props, lifecycle, allowedActions, emitAction }: HostedComponentProps) {
  const status = props as unknown as StatusSummaryProps;
  return <article className="status-summary" aria-label={title}>
    <h2>{title}</h2>{description && <p>{description}</p>}
    <dl><dt>Status</dt><dd>{status.status}</dd><dt>Summary</dt><dd>{status.summary}</dd></dl>
    {allowedActions.includes('acknowledge') && <button type="button" disabled={lifecycle !== 'ready'} onClick={() => emitAction('acknowledge', {})}>Acknowledge status</button>}
  </article>;
}
