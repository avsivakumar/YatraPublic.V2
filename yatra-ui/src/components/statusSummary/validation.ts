import { isObject } from '../../componentProtocol';
export interface StatusSummaryProps { status: string; summary: string }
export function validStatusSummaryProps(value: unknown): value is StatusSummaryProps {
  return isObject(value) && Object.keys(value).length === 2 && typeof value.status === 'string' && value.status.trim().length > 0 && value.status.length <= 100
    && typeof value.summary === 'string' && value.summary.trim().length > 0 && value.summary.length <= 1000;
}
export function validStatusSummaryResponse(action: string, data: unknown, props: unknown): boolean {
  return validStatusSummaryProps(props) && action === 'acknowledge' && isObject(data) && Object.keys(data).length === 0;
}
