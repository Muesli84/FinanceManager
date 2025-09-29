export function getLocale(){
  try {
    if (navigator.languages && navigator.languages.length>0) return navigator.languages[0];
    return navigator.language || '';
  } catch { return ''; }
}
export function getTimeZone(){
  try { return Intl.DateTimeFormat().resolvedOptions().timeZone || ''; } catch { return ''; }
}
