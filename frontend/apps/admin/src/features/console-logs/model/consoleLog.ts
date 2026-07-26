import * as v from 'valibot'

export interface ConsoleLogEntry {
  sequence: number
  formattedMessage: string | null
  message: string | null
  trace: string | null
  logType: string
  timestamp: string
  uptimeMilliseconds: number
}

const safePositiveInteger = v.pipe(
  v.number(),
  v.integer(),
  v.minValue(1),
  v.maxValue(Number.MAX_SAFE_INTEGER),
)

const safeNonNegativeInteger = v.pipe(
  v.number(),
  v.integer(),
  v.minValue(0),
  v.maxValue(Number.MAX_SAFE_INTEGER),
)

const consoleLogEntrySchema = v.strictObject({
  sequence: safePositiveInteger,
  formattedMessage: v.nullable(v.string()),
  message: v.nullable(v.string()),
  trace: v.nullable(v.string()),
  logType: v.string(),
  timestamp: v.pipe(v.string(), v.isoTimestamp()),
  uptimeMilliseconds: safeNonNegativeInteger,
})

export function parseConsoleLogEntry(value: unknown): ConsoleLogEntry {
  try {
    return Object.freeze(v.parse(consoleLogEntrySchema, value))
  }
  catch {
    throw new Error('Invalid console log entry')
  }
}
