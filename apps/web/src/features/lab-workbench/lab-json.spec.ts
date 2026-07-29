import { describe, expect, it } from 'vitest'
import {
  hasArray,
  hasNonNegativeInteger,
  hasPositiveInteger,
  hasRequiredString,
  hasSha256,
  hasVersionedReference,
  LabJsonInputError,
  parseJsonObject,
  prettyJson
} from './lab-json'

describe('laboratory structured JSON helpers', () => {
  it('parses an object and preserves exact versioned fields', () => {
    const value = parseJsonObject('{"expectedCurrentVersion":2,"items":[1]}')
    expect(hasPositiveInteger(value, 'expectedCurrentVersion')).toBe(true)
    expect(hasArray(value, 'items')).toBe(true)
    expect(prettyJson(value)).toContain('"expectedCurrentVersion": 2')
  })

  it('rejects malformed JSON and non-object top levels', () => {
    expect(() => parseJsonObject('{')).toThrow(LabJsonInputError)
    expect(() => parseJsonObject('[1]')).toThrow('顶层必须是对象')
  })

  it('validates strings, integer boundaries, references, and hashes', () => {
    const record = {
      name: 'value', zero: 0, positive: 1, hash: 'a'.repeat(64), empty: []
    }
    expect(hasRequiredString(record, 'name')).toBe(true)
    expect(hasNonNegativeInteger(record, 'zero')).toBe(true)
    expect(hasPositiveInteger(record, 'zero')).toBe(false)
    expect(hasPositiveInteger(record, 'positive')).toBe(true)
    expect(hasVersionedReference({ id: 'ref', version: 1 })).toBe(true)
    expect(hasVersionedReference({ id: 'ref', version: 0 })).toBe(false)
    expect(hasSha256(record, 'hash')).toBe(true)
    expect(hasArray(record, 'empty')).toBe(false)
    expect(hasArray(record, 'empty', true)).toBe(true)
  })
})
