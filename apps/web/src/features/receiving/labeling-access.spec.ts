import { describe, expect, it } from 'vitest'
import {
  hasLabelPrintCapability,
  hasLabelReprintCapability,
  hasLabelScanCapability
} from './labeling-access'

describe('labeling access', () => {
  it('requires exact business capabilities and never expands system admin', () => {
    expect(hasLabelPrintCapability({ capability: ['receiving.label.print'] })).toBe(true)
    expect(hasLabelScanCapability({ capability: ['receiving.label.scan'] })).toBe(true)
    expect(hasLabelReprintCapability({ capability: ['receiving.label.reprint'] })).toBe(true)
    expect(hasLabelPrintCapability({ capability: ['system_admin'] })).toBe(false)
    expect(hasLabelReprintCapability(undefined)).toBe(false)
  })
})
