import { test, expect, type Page } from '@playwright/test';

// KAN-1: in-app release feedback widget, tested against FeedbackService/wwwroot/demo.html,
// which stands in for the real host app (owns "current release" + session token, mounts the
// widget via FeedbackWidget.init).

const AUTH_TOKEN = 'e2e-widget-user';

// Each release must be unique per test: the server enforces a submission cap per
// (authToken, release) pair, and the widget's own "already handled" check is keyed on release.
// Reusing a release across tests/runs would make later runs flaky against earlier ones.
function uniqueRelease(tag: string): string {
  const stamp = Date.now().toString(36).slice(-6) + Math.random().toString(36).slice(2, 5);
  return `${tag}-${stamp}`.slice(0, 20);
}

async function mount(page: Page, release: string, authToken: string = AUTH_TOKEN) {
  await page.goto('/demo.html');
  await page.getByLabel('Current release/build version').fill(release);
  await page.getByLabel(/Session token/).fill(authToken);
  await page.getByRole('button', { name: /^Load app/ }).click();
  return page.getByRole('dialog', { name: 'Release feedback' });
}

function star(dialog: ReturnType<Page['getByRole']>, value: number) {
  const label = `${value} star${value === 1 ? '' : 's'}`;
  return dialog.getByRole('radio', { name: label, exact: true });
}

test.describe('KAN-1 — release feedback widget', () => {
  // AC-1
  test('widget appears once per release, on a device that has not answered or dismissed it', async ({ page }) => {
    const release = uniqueRelease('ac1');
    const dialog = await mount(page, release);
    await expect(dialog).toBeVisible();
  });

  // AC-2
  test('widget shows again once the release differs from the one recorded on the device', async ({ page }) => {
    const releaseA = uniqueRelease('ac2a');
    const releaseB = uniqueRelease('ac2b');
    const dialog = await mount(page, releaseA);
    await dialog.getByRole('button', { name: 'Dismiss feedback widget' }).click();
    await expect(dialog).toBeHidden();

    // Widget decides again, without a full navigation, against the new release.
    await page.getByLabel('Current release/build version').fill(releaseB);
    await page.getByRole('button', { name: /^Load app/ }).click();
    await expect(dialog).toBeVisible();
  });

  // AC-3
  test('widget does not reappear for a release already answered or dismissed on this device', async ({ page }) => {
    const release = uniqueRelease('ac3');
    const dialog = await mount(page, release);
    await dialog.getByRole('button', { name: 'Dismiss feedback widget' }).click();
    await expect(dialog).toBeHidden();

    // Reopen the app (full reload) and load the same release again.
    await page.reload();
    await page.getByLabel('Current release/build version').fill(release);
    await page.getByRole('button', { name: /^Load app/ }).click();
    await expect(page.getByRole('dialog', { name: 'Release feedback' })).toHaveCount(0);
  });

  // AC-4
  test('rating control is 5 stars', async ({ page }) => {
    const dialog = await mount(page, uniqueRelease('ac4'));
    const stars = dialog.getByRole('radiogroup', { name: 'Star rating' }).getByRole('radio');
    await expect(stars).toHaveCount(5);
    for (let value = 1; value <= 5; value++) {
      await expect(star(dialog, value)).toBeVisible();
    }
  });

  // AC-5
  test('comment is not required to submit once a rating is provided', async ({ page }) => {
    const release = uniqueRelease('ac5');
    const dialog = await mount(page, release);
    const comment = dialog.getByLabel('Comment (optional)');
    await expect(comment).not.toHaveAttribute('required', '');

    await star(dialog, 3).click();
    await dialog.getByRole('button', { name: /^Submit/ }).click();

    // No "you must fill this in" block for the empty comment - only a possible rating block,
    // which does not apply here since a rating was picked.
    await expect(dialog.getByRole('alert')).toHaveCount(0);
    await expect(dialog.getByText(`Thanks, logged for ${release}.`)).toBeVisible();
  });

  // AC-6
  test('submit succeeds with a rating and no comment', async ({ page }) => {
    const release = uniqueRelease('ac6');
    const dialog = await mount(page, release);
    await star(dialog, 5).click();
    await dialog.getByRole('button', { name: /^Submit/ }).click();
    await expect(dialog.getByText(`Thanks, logged for ${release}.`)).toBeVisible();
  });

  // AC-7
  test('submit is blocked with a visible message when no rating is selected', async ({ page }) => {
    const release = uniqueRelease('ac7');
    const dialog = await mount(page, release);
    const submitButton = dialog.getByRole('button', { name: /^Submit/ });

    // The control itself must stay clickable with no rating picked - the block happens on press.
    await expect(submitButton).toBeEnabled();
    await submitButton.click();

    await expect(dialog.getByRole('alert')).toHaveText('Please pick a star rating before submitting.');
    await expect(dialog).toBeVisible();
  });

  // AC-8
  test('close control dismisses the widget and records the release as handled on this device', async ({ page }) => {
    const release = uniqueRelease('ac8');
    const dialog = await mount(page, release);
    await dialog.getByRole('button', { name: 'Dismiss feedback widget' }).click();
    await expect(dialog).toBeHidden();

    // The demo host's #state readout only refreshes on its own Load/Reset button handlers, so
    // read the actual source of truth (localStorage) rather than that stale debug display.
    const stored = await page.evaluate(() => localStorage.getItem('feedbackWidget.lastHandledRelease'));
    expect(stored).toBe(release);
  });

  // AC-9
  test('comment is capped at 500 characters for both typed and pasted input', async ({ page }) => {
    const dialog = await mount(page, uniqueRelease('ac9'));
    const comment = dialog.getByLabel('Comment (optional)');
    const counter = dialog.locator('.feedback-widget__counter');

    // Typed input beyond the cap.
    await comment.fill('a'.repeat(510));
    await expect(comment).toHaveValue('a'.repeat(500));
    await expect(counter).toHaveText('0 characters left');

    // Pasted input beyond the cap, in a single action.
    await comment.fill('');
    await page.context().grantPermissions(['clipboard-read', 'clipboard-write']);
    await page.evaluate((text) => navigator.clipboard.writeText(text), 'b'.repeat(600));
    await comment.click();
    await comment.press('Control+V');
    await expect(comment).toHaveValue('b'.repeat(500));
    await expect(counter).toHaveText('0 characters left');
  });

  // AC-10
  test('a failed submit shows a visible error, keeps the widget open with the entry intact, and does not record the release as answered', async ({ page }) => {
    const release = uniqueRelease('ac10');
    const dialog = await mount(page, release);

    await page.route('**/api/feedback', (route) =>
      route.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({}) }),
    );

    await star(dialog, 2).click();
    await dialog.getByLabel('Comment (optional)').fill('kept on failure');
    await dialog.getByRole('button', { name: /^Submit/ }).click();

    await expect(dialog.getByRole('alert')).toBeVisible();
    await expect(star(dialog, 2)).toHaveClass(/feedback-widget__star--filled/);
    await expect(dialog.getByLabel('Comment (optional)')).toHaveValue('kept on failure');
    await expect(dialog).toBeVisible();

    // Not recorded as answered: reopening for the same release shows the widget again.
    await page.unroute('**/api/feedback');
    await page.reload();
    await page.getByLabel('Current release/build version').fill(release);
    await page.getByRole('button', { name: /^Load app/ }).click();
    await expect(page.getByRole('dialog', { name: 'Release feedback' })).toBeVisible();
  });

  // AC-11
  test('confirmation after a successful submit names the release it was logged for', async ({ page }) => {
    const release = uniqueRelease('ac11');
    const dialog = await mount(page, release);
    await star(dialog, 4).click();
    await dialog.getByRole('button', { name: /^Submit/ }).click();
    await expect(dialog.getByText(`Thanks, logged for ${release}.`)).toBeVisible();
  });

  // AC-12
  test('there is no option to attach a screenshot or file', async ({ page }) => {
    const dialog = await mount(page, uniqueRelease('ac12'));
    await expect(dialog.locator('input[type="file"]')).toHaveCount(0);
    await expect(dialog.getByRole('button', { name: /attach|screenshot/i })).toHaveCount(0);
  });

  // AC-13
  test('submit and the rating/comment inputs lock while the request is in flight, and unlock on failure', async ({ page }) => {
    const release = uniqueRelease('ac13');
    const dialog = await mount(page, release);

    let requestCount = 0;
    await page.route('**/api/feedback', async (route) => {
      requestCount++;
      await new Promise((resolve) => setTimeout(resolve, 700));
      await route.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({}) });
    });

    await star(dialog, 5).click();
    const submitButton = dialog.getByRole('button', { name: /^Submit/ });
    await submitButton.click();

    await expect(submitButton).toBeDisabled();
    await expect(submitButton).toHaveText('Submitting…');
    await expect(star(dialog, 5)).toBeDisabled();
    await expect(dialog.getByLabel('Comment (optional)')).toBeDisabled();

    // A second press while pending must not fire a duplicate request.
    await submitButton.click({ force: true });

    await expect(dialog.getByRole('alert')).toBeVisible();
    await expect(submitButton).toBeEnabled();
    await expect(submitButton).toHaveText('Submit');
    expect(requestCount).toBe(1);
  });
});
