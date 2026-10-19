import { isObject } from '../../componentProtocol';
export interface Customer { id: string; name: string; status: string; location?: string; summary?: string }
export interface CustomerComparisonProps { customers: Customer[] }
function text(value: unknown, max: number, required = true): value is string {
  return typeof value === 'string' && value.length <= max && (!required || value.trim().length > 0);
}
function fields(value: Record<string, unknown>, allowed: string[]) { return Object.keys(value).every(key => allowed.includes(key)); }
export function validCustomerProps(value: unknown): value is CustomerComparisonProps {
  if (!isObject(value) || !fields(value, ['customers']) || !Array.isArray(value.customers) || value.customers.length < 2 || value.customers.length > 10) return false;
  const ids = new Set<string>();
  return value.customers.every(customer => {
    if (!isObject(customer) || !fields(customer, ['id', 'name', 'status', 'location', 'summary']) || !text(customer.id, 100) || !text(customer.name, 200) || !text(customer.status, 100) ||
      ('location' in customer && !text(customer.location, 200, false)) || ('summary' in customer && !text(customer.summary, 1000, false)) || ids.has(customer.id)) return false;
    ids.add(customer.id); return true;
  });
}
export function validCustomerResponse(action: string, data: unknown, props: unknown): boolean {
  if (!validCustomerProps(props) || !isObject(data)) return false;
  if (action === 'cancel') return Object.keys(data).length === 0;
  return action === 'select' && Object.keys(data).length === 1 && text(data.customerId, 100) && props.customers.some(customer => customer.id === data.customerId);
}
