import { useId, useState } from 'react';
import type { HostedComponentProps } from '../../componentRegistry';
import type { CustomerComparisonProps } from './validation';
export function CustomerComparison({ title, description, props, allowedActions, lifecycle, outcome, emitAction }: HostedComponentProps) {
  const customers = (props as unknown as CustomerComparisonProps).customers;
  const [selected, setSelected] = useState('');
  const group = useId();
  const active = lifecycle === 'ready';
  const selectedId = typeof outcome?.data?.customerId === 'string' ? outcome.data.customerId : selected;
  return <article className="customer-comparison" aria-label={title}>
    <h2>{title}</h2>{description && <p>{description}</p>}
    <fieldset disabled={!active || !allowedActions.includes('select')}>
      <legend>Compare customers</legend>
      <div className="customer-grid">{customers.map(customer => <div key={customer.id} className={`customer-card ${selectedId === customer.id ? 'customer-selected' : ''}`}>
        {allowedActions.includes('select') && <input type="radio" name={group} value={customer.id} checked={selectedId === customer.id} onChange={() => setSelected(customer.id)} aria-label={`${customer.name} (${customer.id})`} />}
        <strong>{customer.name}</strong><span>{customer.id} · {customer.status}</span>
        {customer.location && <span>{customer.location}</span>}
        {customer.summary && <details><summary>Customer details: {customer.name}</summary><p>{customer.summary}</p></details>}
        {selectedId === customer.id && <span>Selected customer</span>}
      </div>)}</div>
    </fieldset>
    <div className="component-actions">
      {allowedActions.includes('select') && <button type="button" disabled={!active || !selectedId} onClick={() => emitAction('select', { customerId: selectedId })}>Confirm selection</button>}
      {allowedActions.includes('cancel') && <button type="button" disabled={!active} onClick={() => emitAction('cancel', {})}>Cancel comparison</button>}
    </div>
  </article>;
}

