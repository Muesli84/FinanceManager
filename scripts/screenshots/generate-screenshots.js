const { spawn } = require('child_process');
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const os = require('os');
const http = require('http');

const repoRoot = path.resolve(__dirname, '..', '..');
const outputDir = path.join(repoRoot, 'Docs', 'screenshots');
const appDll = path.join(repoRoot, 'FinanceManager.Web', 'bin', 'Release', 'net10.0', 'publish', 'FinanceManager.Web.dll');
const tempDbDir = fs.mkdtempSync(path.join(os.tmpdir(), 'financemanager-screenshots-'));
const tempDb = path.join(tempDbDir, 'financemanager.db');
const appLogPath = path.join(tempDbDir, 'app.log');

fs.mkdirSync(outputDir, { recursive: true });

function getFreePort() {
  return new Promise((resolve, reject) => {
    const server = http.createServer();
    server.listen(0, '127.0.0.1', () => {
      const port = server.address().port;
      server.close(() => resolve(port));
    });
    server.on('error', reject);
  });
}

function waitFor(predicate, timeoutMs = 120000, intervalMs = 500) {
  return new Promise((resolve, reject) => {
    const deadline = Date.now() + timeoutMs;
    const check = async () => {
      try {
        const ok = await predicate();
        if (ok) return resolve();
      } catch { /* ignore */ }
      if (Date.now() >= deadline) return reject(new Error(`Timeout while waiting for predicate (app log: ${appLogPath})`));
      setTimeout(check, intervalMs);
    };
    check();
  });
}

(async () => {
  if (!fs.existsSync(appDll)) {
    throw new Error(
      `Published application not found at ${appDll}. Run "dotnet publish FinanceManager.Web -c Release" first.`
    );
  }

  const port = await getFreePort();
  const baseUrl = `http://127.0.0.1:${port}`;
  const appLogFd = fs.openSync(appLogPath, 'w');

  const appProcess = spawn('dotnet', [appDll], {
    cwd: path.dirname(appDll),
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
      DOTNET_ENVIRONMENT: 'Development',
      ASPNETCORE_URLS: baseUrl,
      Api__BaseAddress: `${baseUrl}/`,
      E2E__DisableHttpsRedirection: 'true',
      ConnectionStrings__Default: `Data Source=${tempDb}`,
      Jwt__Key: 'screenshots-demo-signing-key-0123456789abcdef0123456789abcdef',
      BackgroundTasks__Enabled: 'true',
      Workers__SecurityPriceWorker__Enabled: 'false',
      FileLogging__Enabled: 'false',
      Updates__Enabled: 'false',
      Updates__HostedServicesEnabled: 'false',
      DetailedErrors: 'true'
    },
    stdio: ['ignore', appLogFd, appLogFd]
  });

  let keepTempDir = false;
  try {
    // Wait until the application responds to HTTP requests.
    console.log(`Waiting for app on ${baseUrl} (log: ${appLogPath})`);
    await waitFor(async () => {
      try {
        const res = await fetch(`${baseUrl}/register`);
        return res.status < 500;
      } catch {
        return false;
      }
    }, 120000, 250);
    console.log('App is up');

    const browser = await chromium.launch({ headless: true });
    const context = await browser.newContext({
      viewport: { width: 1280, height: 900 },
      locale: 'de-DE',
      timezoneId: 'Europe/Berlin'
    });
    const page = await context.newPage();

    const screenshot = (name) => page.screenshot({ path: path.join(outputDir, `${name}.png`), fullPage: false });

    // 1. Registration page with the demo-data checkbox (only shown for the first user).
    await page.goto(`${baseUrl}/register`, { waitUntil: 'load' });
    await page.waitForSelector('#create-demo-data', { timeout: 30000 });
    await page.waitForTimeout(500);
    await screenshot('register');
    console.log('Screenshot: register');

    const username = `demo${Date.now().toString(36)}`;
    const password = 'DemoTest!123';

    // Register through the real UI so the demo-data flag goes through the same
    // path a user takes. Blazor may need a moment to attach event handlers, so
    // the submit is retried like in the E2E tests.
    let registered = false;
    for (let attempt = 0; attempt < 3 && !registered; attempt++) {
      await page.fill('#username', username);
      await page.fill('#password', password);
      await page.evaluate(({ user, pass }) => {
        const userInput = document.querySelector('#username');
        const passInput = document.querySelector('#password');
        const demoCheckbox = document.querySelector('#create-demo-data');
        for (const el of [userInput, passInput]) {
          if (!el) continue;
          el.dispatchEvent(new Event('input', { bubbles: true }));
          el.dispatchEvent(new Event('change', { bubbles: true }));
        }
        if (demoCheckbox) {
          demoCheckbox.checked = true;
          demoCheckbox.dispatchEvent(new Event('input', { bubbles: true }));
          demoCheckbox.dispatchEvent(new Event('change', { bubbles: true }));
        }
      }, { user: username, pass: password });
      await page.click('button[type="submit"]');
      try {
        await page.waitForFunction(() => location.pathname !== '/register', null, { timeout: 5000 });
        registered = true;
      } catch { /* Blazor event handlers may still be attaching */ }
    }
    if (!registered) {
      throw new Error('Registration did not navigate away from /register');
    }
    console.log(`Registered ${username} with demo data enabled`);

    // 2. Registration itself does not authenticate the browser, so log in
    // through the UI afterwards.
    await page.goto(`${baseUrl}/login`, { waitUntil: 'load' });
    {
      await page.waitForSelector('#login-user', { timeout: 30000 });
      await page.fill('#login-user', username);
      await page.fill('#login-pass', password);
      await page.waitForFunction(() => typeof window.fmAuthLogin === 'function', null, { timeout: 15000 });
      await page.evaluate(() => {
        window.__fmAuthLoginCalls = 0;
        if (!window.__fmAuthLoginOriginal) {
          window.__fmAuthLoginOriginal = window.fmAuthLogin;
        }
        window.fmAuthLogin = async (...args) => {
          window.__fmAuthLoginCalls += 1;
          return await window.__fmAuthLoginOriginal(...args);
        };
      });
      let loggedIn = false;
      for (let attempt = 0; attempt < 3 && !loggedIn; attempt++) {
        await page.click('button[type="submit"]');
        try {
          await page.waitForFunction(() => window.__fmAuthLoginCalls > 0, null, { timeout: 3000 });
          await page.waitForFunction(() => !location.pathname.includes('/login'), null, { timeout: 15000 });
          loggedIn = true;
        } catch { /* Blazor event handlers may still be attaching */ }
      }
      if (!loggedIn) {
        throw new Error('Login after registration failed');
      }
      console.log('Logged in');
    }

    // 3. Wait for the demo-data background task to finish. The auth cookie
    // contains the raw JWT, so it can be reused as a bearer token.
    const cookies = await context.cookies(baseUrl);
    const authCookie = cookies.find(c => c.name === 'FinanceManager.Auth');
    if (!authCookie) {
      throw new Error('FinanceManager.Auth cookie missing after login');
    }
    const token = decodeURIComponent(authCookie.value);

    const getActiveTasks = async () => {
      try {
        const res = await fetch(`${baseUrl}/api/background-tasks/active`, {
          headers: { Authorization: `Bearer ${token}` }
        });
        if (!res.ok) return null;
        const tasks = await res.json();
        return Array.isArray(tasks) ? tasks : null;
      } catch {
        return null;
      }
    };

    // The task is enqueued inside the register call, so it must be visible
    // shortly after login. If it never appears the demo-data flag did not go
    // through (e.g. the checkbox binding did not take).
    console.log('Waiting for demo-data background task to start');
    await waitFor(async () => {
      const tasks = await getActiveTasks();
      return tasks !== null && tasks.length > 0;
    }, 90000, 1000);

    console.log('Waiting for demo-data background task to finish');
    await waitFor(async () => {
      const tasks = await getActiveTasks();
      return tasks !== null && tasks.length === 0;
    }, 360000, 2000);
    console.log('Demo data seeding finished');

    // Generic helper for the table-based list pages.
    const shootListPage = async (route, name, minRows) => {
      await page.goto(`${baseUrl}${route}`, { waitUntil: 'load' });
      await waitFor(async () => {
        const rows = await page.locator('.fm-table tbody tr').count();
        return rows >= minRows;
      }, 60000, 500);
      await page.waitForTimeout(400);
      await screenshot(name);
      console.log(`Screenshot: ${name}`);
    };

    // 4. Home with the seeded KPI tiles.
    await page.goto(`${baseUrl}/`, { waitUntil: 'load' });
    await waitFor(async () => {
      const count = await page.locator('.kpi-grid .kpi-tile').count();
      return count >= 3;
    }, 60000, 500);
    await page.waitForTimeout(1000);
    await screenshot('home');
    console.log('Screenshot: home');

    // 5. Master data and statement drafts.
    await shootListPage('/list/accounts', 'accounts', 3);
    await shootListPage('/list/statement-drafts', 'statement-drafts', 1);
    await shootListPage('/list/contacts', 'contacts', 10);
    await shootListPage('/list/savings-plans', 'savings-plans', 5);
    await shootListPage('/list/securities', 'securities', 2);
    await shootListPage('/list/budget/purposes', 'budget-purposes', 5);

    // 6. Reports overview with the seeded report favorites.
    await page.goto(`${baseUrl}/reports`, { waitUntil: 'load' });
    await waitFor(async () => {
      const count = await page.locator('.fav-grid .fav-card').count();
      return count >= 1;
    }, 60000, 500);
    await page.waitForTimeout(400);
    await screenshot('reports');
    console.log('Screenshot: reports');

    // 7. Budget report for the previous month (contains booked postings).
    await page.goto(`${baseUrl}/reports/budget`, { waitUntil: 'load' });
    await waitFor(async () => {
      return await page.locator('.budget-report-table').count() > 0;
    }, 90000, 500);
    const periodBefore = await page.locator('.budget-report-period').first().innerText().catch(() => '');
    const prevMonth = page.locator('#PrevMonth');
    if (await prevMonth.count() > 0) {
      await prevMonth.click();
      await waitFor(async () => {
        const loading = await page.locator('.budget-report-loading').count();
        const periodNow = await page.locator('.budget-report-period').first().innerText().catch(() => '');
        return loading === 0 && periodNow !== periodBefore;
      }, 60000, 500);
      await page.waitForTimeout(400);
    }
    await screenshot('budget-report');
    console.log('Screenshot: budget-report');

    await context.close();
    await browser.close();
    console.log(`Screenshots saved to ${outputDir}`);
  } catch (err) {
    keepTempDir = true;
    console.error(`Failed; keeping app log and database at ${tempDbDir}`);
    throw err;
  } finally {
    appProcess.kill('SIGTERM');
    if (!keepTempDir) {
      try {
        fs.rmSync(tempDbDir, { recursive: true, force: true });
      } catch { /* ignore */ }
    }
  }
})();
