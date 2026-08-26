import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  reporter: 'list',
  use: {
    baseURL: process.env.BASE_URL || 'http://localhost:5299',
    trace: 'retain-on-failure',
  },
});
