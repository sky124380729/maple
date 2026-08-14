import { spawnSync } from 'node:child_process'

function quoteCmdToken(value) {
  const text = String(value)
  return `"${text.replaceAll('"', '""')}"`
}

function quoteCmdCommand(value) {
  const text = String(value)
  return /[\s&|<>()^]/.test(text) ? quoteCmdToken(text) : text
}

export function resolvePortableCommand(
  command,
  args,
  platform = process.platform,
  comSpec = process.env.ComSpec || process.env.COMSPEC || 'cmd.exe',
) {
  if (platform === 'win32' && /\.(?:cmd|bat)$/i.test(command)) {
    const commandLine = [quoteCmdCommand(command), ...args.map(quoteCmdToken)].join(' ')
    return {
      command: comSpec,
      args: ['/d', '/s', '/c', commandLine],
      windowsVerbatimArguments: true,
    }
  }

  return { command, args: [...args] }
}

export function runCapture(command, args, cwd, options = {}) {
  const resolved = resolvePortableCommand(command, args)
  return spawnSync(resolved.command, resolved.args, {
    cwd,
    encoding: 'utf8',
    windowsVerbatimArguments: resolved.windowsVerbatimArguments,
    ...options,
  })
}

export function run(command, args, cwd) {
  console.log(`\n> ${command} ${args.join(' ')}`)
  const result = runCapture(command, args, cwd, { encoding: undefined, stdio: 'inherit' })
  if (result.error) throw result.error
  if (result.status !== 0) process.exit(result.status ?? 1)
}
