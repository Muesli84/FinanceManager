window.fmAuthLogin = async (username, password) => {
  try {
    const resp = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ username, password })
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
