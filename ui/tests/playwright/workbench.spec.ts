import { expect, test } from '@playwright/test'

test('工作台关键控制与响应式布局可用', async ({ page }) => {
  const consoleErrors: string[] = []
  page.on('console', (message) => {
    if (message.type() === 'error') consoleErrors.push(message.text())
  })

  await page.goto('/')
  await expect(page.getByRole('banner')).toBeVisible()
  await expect(page.getByText('Maple 自动化工作台')).toBeVisible()
  await expect(page.getByText('自己角色', { exact: true })).toBeVisible()

  const emergencyStop = page.getByRole('button', { name: '紧急停止' })
  await expect(emergencyStop).toBeVisible()
  await page.getByRole('button', { name: '开始运行' }).click()
  await expect(page.locator('.control-hero__copy', { hasText: '输入服务已就绪' })).toBeVisible()
  await page.getByRole('button', { name: '暂停并释放按键' }).click()
  await expect(page.locator('.topbar .status-pill', { hasText: '已暂停' })).toBeVisible()
  await emergencyStop.click()
  await expect(page.locator('.topbar .status-pill', { hasText: '紧急停止' })).toBeVisible()

  const horizontalOverflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)
  expect(horizontalOverflow).toBe(false)
  expect(consoleErrors).toEqual([])
})
