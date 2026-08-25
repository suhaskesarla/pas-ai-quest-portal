import { type Page } from '@playwright/test'
import fs from 'node:fs/promises'
import path from 'node:path'

export async function captureEvidence(page: Page, journey: string, fileName: string) {
  const root = process.env.QA_SCREENSHOT_DIR
  if (!root) throw new Error('QA_SCREENSHOT_DIR is required for Playwright evidence.')
  const directory = path.join(root, journey)
  await fs.mkdir(directory, { recursive: true })
  await page.screenshot({ path: path.join(directory, fileName), fullPage: true })
}
