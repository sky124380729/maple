import { spawnSync } from 'node:child_process'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const ui = path.join(root, 'ui')
const npmCommand = process.platform === 'win32' ? 'npm.cmd' : 'npm'
const dotnetCommand = process.env.DOTNET_ROOT
  ? path.join(process.env.DOTNET_ROOT, process.platform === 'win32' ? 'dotnet.exe' : 'dotnet')
  : 'dotnet'

function run(command, args, cwd = root) {
  console.log(`\n> ${command} ${args.join(' ')}`)
  const result = spawnSync(command, args, { cwd, stdio: 'inherit' })
  if (result.error) throw result.error
  if (result.status !== 0) process.exit(result.status ?? 1)
}

function walk(directory, predicate, output = []) {
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    if (['.git', 'node_modules', 'dist', 'playwright-report', 'test-results', 'coverage'].includes(entry.name)) continue
    const absolute = path.join(directory, entry.name)
    if (entry.isDirectory()) walk(absolute, predicate, output)
    else if (predicate(absolute)) output.push(absolute)
  }
  return output
}

run(npmCommand, ['ci'], ui)
run(npmCommand, ['audit', '--audit-level=high'], ui)
run(npmCommand, ['run', 'lint'], ui)
run(npmCommand, ['run', 'typecheck'], ui)
run(npmCommand, ['test'], ui)
run(npmCommand, ['run', 'build'], ui)
run(npmCommand, ['run', 'e2e'], ui)
run(process.execPath, ['tests/portable-contracts.mjs'])
run(process.execPath, ['tests/closed-loop/portable-closed-loop.mjs'])
run(dotnetCommand, ['test', 'Maple.sln', '-p:EnableWindowsTargeting=true'])
run(dotnetCommand, ['build', 'src/Maple.Host/Maple.Host.csproj', '-p:EnableWindowsTargeting=true', '-t:Rebuild'])

if (process.platform !== 'win32') {
  for (const project of walk(root, (file) => file.endsWith('.csproj'))) run('xmllint', ['--noout', project])
}

const textFiles = walk(root, (file) => /\.(?:cs|css|html|js|json|md|mjs|ts|tsx|xml)$/.test(file))
const testIdToken = 'data-' + 'testid'
for (const file of textFiles) {
  const source = fs.readFileSync(file, 'utf8')
  const relative = path.relative(root, file)
  if ((relative.startsWith(`ui${path.sep}src${path.sep}`) || relative.startsWith(`ui${path.sep}tests${path.sep}`)) && source.includes(testIdToken)) {
    throw new Error(`禁止的测试定位属性: ${relative}`)
  }
  if (/(?:sk-[A-Za-z0-9_-]{20,}|api[_-]?key\s*[:=]\s*['"][^'"]+['"])/i.test(source)) throw new Error(`疑似硬编码密钥: ${path.relative(root, file)}`)
}

run('git', ['diff', '--check'])
console.log('\nPORTABLE_VERIFICATION=PASS')
console.log('WINDOWS_NATIVE_AND_HID=NOT_VERIFIED')
