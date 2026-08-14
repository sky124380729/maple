import assert from 'node:assert/strict'
import { test } from 'node:test'

import { resolvePortableCommand, runCapture } from '../../tools/portable-process.mjs'

test('wraps Windows command scripts with ComSpec', () => {
  const resolved = resolvePortableCommand(
    'npm.cmd',
    ['run', 'build'],
    'win32',
    'C:\\Windows\\System32\\cmd.exe',
  )

  assert.equal(resolved.command, 'C:\\Windows\\System32\\cmd.exe')
  assert.deepEqual(resolved.args.slice(0, 3), ['/d', '/s', '/c'])
  assert.match(resolved.args[3], /npm\.cmd/)
  assert.match(resolved.args[3], /build/)
})

test('keeps native executables on the direct spawn path', () => {
  const resolved = resolvePortableCommand('node.exe', ['--version'], 'win32', 'cmd.exe')

  assert.equal(resolved.command, 'node.exe')
  assert.deepEqual(resolved.args, ['--version'])
})

test('executes npm command scripts on Windows', { skip: process.platform !== 'win32' }, () => {
  const result = runCapture('npm.cmd', ['--version'], process.cwd())

  assert.equal(result.status, 0, result.stderr || result.error?.message)
  assert.match(result.stdout, /^\d+\.\d+\.\d+/)
})
