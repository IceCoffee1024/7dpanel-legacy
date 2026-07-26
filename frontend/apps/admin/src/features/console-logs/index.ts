export type {
  ConsoleCommandCatalog,
  ConsoleCommandCatalogEntry,
} from './api/consoleCommands'
export type {
  ConsoleCommandFeedback,
  ConsoleCommandFeedbackCode,
  ConsoleCommandsController,
} from './model/useConsoleCommands'
export type { ConsoleLogEntry } from './model/consoleLog'
export type { ConsoleLogsController } from './model/useConsoleLogs'

export { useConsoleCommands } from './model/useConsoleCommands'
export { useConsoleLogs } from './model/useConsoleLogs'
export { default as ConsoleWorkspace } from './ui/ConsoleWorkspace.vue'
