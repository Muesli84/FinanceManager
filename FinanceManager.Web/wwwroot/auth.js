window.fmAuthLogin = async (username, password) => {
  try {
    const preferredLanguage = (navigator.languages && navigator.languages.length > 0)
      ? navigator.languages[0]
      : (navigator.language || null);
    let timeZoneId = null;
    try { timeZoneId = Intl.DateTimeFormat().resolvedOptions().timeZone || null; } catch {}

    const resp = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ username, password, preferredLanguage, timeZoneId })
    });
    if (!resp.ok) {
      let errText = '';
      try { const data = await resp.json(); errText = data.error || ''; } catch { }
      return { ok: false, error: errText };
    }
    return { ok: true };
  } catch (e) {
    return { ok: false, error: e?.message || 'Network error' };
  }
};

window.fmAuthLogout = async () => {
  try {
    const resp = await fetch('/api/auth/logout', {
      method: 'POST',
      credentials: 'same-origin'
    });
    return resp.ok;
  } catch {
    return false;
  }
};
