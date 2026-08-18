import assert from 'node:assert/strict'
import test from 'node:test'

import { createSpawnOptions } from './verify-portable-runner.mjs'

test('wraps Windows cmd shims in a shell-compatible spawn call', () => {
  const options = createSpawnOptions('npm.cmd', ['ci'], 'C:/repo/ui', 'win32')

  assert.equal(options.command, 'npm.cmd')
  assert.deepEqual(options.args, ['ci'])
  assert.equal(options.spawnOptions.cwd, 'C:/repo/ui')
  assert.equal(options.spawnOptions.shell, true)
})

test('keeps native executables as direct spawn calls', () => {
  const options = createSpawnOptions('dotnet', ['test'], 'C:/repo', 'win32')

  assert.equal(options.command, 'dotnet')
  assert.deepEqual(options.args, ['test'])
  assert.equal(options.spawnOptions.cwd, 'C:/repo')
  assert.equal(options.spawnOptions.shell, undefined)
})
