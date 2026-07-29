export type JsonRecord = Record<string, unknown>

export class LabJsonInputError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'LabJsonInputError'
  }
}

export function prettyJson(value: unknown): string {
  return JSON.stringify(value, null, 2)
}

export function parseJsonObject<T extends JsonRecord>(source: string): T {
  let value: unknown
  try {
    value = JSON.parse(source)
  } catch {
    throw new LabJsonInputError('请求 JSON 语法无效，请检查引号、逗号和括号。')
  }
  if (!isJsonRecord(value)) {
    throw new LabJsonInputError('请求 JSON 顶层必须是对象。')
  }
  return value as T
}

export function isJsonRecord(value: unknown): value is JsonRecord {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

export function hasRequiredString(record: JsonRecord, key: string): boolean {
  const value = record[key]
  return typeof value === 'string' && value.trim().length > 0
}

export function hasPositiveInteger(record: JsonRecord, key: string): boolean {
  const value = record[key]
  return typeof value === 'number' && Number.isInteger(value) && value > 0
}

export function hasNonNegativeInteger(record: JsonRecord, key: string): boolean {
  const value = record[key]
  return typeof value === 'number' && Number.isInteger(value) && value >= 0
}

export function hasArray(record: JsonRecord, key: string, allowEmpty = false): boolean {
  const value = record[key]
  return Array.isArray(value) && (allowEmpty || value.length > 0)
}

export function hasVersionedReference(value: unknown): boolean {
  return isJsonRecord(value) && hasRequiredString(value, 'id') && hasPositiveInteger(value, 'version')
}

export function hasSha256(record: JsonRecord, key: string): boolean {
  const value = record[key]
  return typeof value === 'string' && /^[a-fA-F0-9]{64}$/.test(value)
}
