import { CustomerComparison } from './components/customerComparison/CustomerComparison';
import { validCustomerProps, validCustomerResponse } from './components/customerComparison/validation';
import type { ComponentRegistry } from './componentRegistry';
import { StatusSummary } from './components/statusSummary/StatusSummary';
import { validStatusSummaryProps, validStatusSummaryResponse } from './components/statusSummary/validation';
export function registerTrustedComponents(registry: ComponentRegistry) {
  registry.register({ componentId: 'customer-comparison', componentVersion: '1.0', supportedActions: ['select', 'cancel'],
    validateProps: props => ({ valid: validCustomerProps(props) }),
    validateResponse: (action, data, props) => ({ valid: validCustomerResponse(action, data, props) }), render: CustomerComparison });
  registry.register({ componentId: 'status-summary', componentVersion: '1.0', supportedActions: ['acknowledge'],
    validateProps: props => ({ valid: validStatusSummaryProps(props) }),
    validateResponse: (action, data, props) => ({ valid: validStatusSummaryResponse(action, data, props) }), render: StatusSummary });
}
