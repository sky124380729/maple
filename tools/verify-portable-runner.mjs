export function createSpawnOptions(command, args, cwd, platform = process.platform) {
  const spawnOptions = { cwd, stdio: 'inherit' }
  if (platform === 'win32' && /\.cmd$/i.test(command)) spawnOptions.shell = true
  return { command, args, spawnOptions }
}
