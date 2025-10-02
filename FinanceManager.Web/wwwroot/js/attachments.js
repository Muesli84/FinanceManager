export const __apRegistry = new WeakMap();

export function registerDropArea(elementId, uploadUrl, dotnet){
  const el = document.getElementById(elementId);
  if (!el) return;

  // If already registered for this element, just update context and return (idempotent)
  const existing = __apRegistry.get(el);
  if (existing) {
    existing.ctx.uploadUrl = uploadUrl;
    existing.ctx.dotnet = dotnet;
    return;
  }

  const ctx = { uploadUrl, dotnet };
  const onPrevent = e => { e.preventDefault(); e.stopPropagation(); };
  const highlight = () => { el.style.background = '#222'; el.style.borderColor = '#777'; };
  const unhighlight = () => { el.style.background = '#1c1c1c'; el.style.borderColor = '#555'; };

  const onDrop = async (e) => {
    const files = e.dataTransfer?.files;
    if (!files || files.length === 0) return;
    let done = 0; const total = files.length; let error = null;
    try {
      for (let i=0;i<files.length;i++){
        const f = files[i];
        const form = new FormData();
        form.append('file', f, f.name);
        const categoryId = el.getAttribute('data-category');
        if (categoryId) { form.append('categoryId', categoryId); }
        const resp = await fetch(ctx.uploadUrl, { method:'POST', body: form });
        if (!resp.ok){ error = await resp.text(); }
        done = i+1;
        if (ctx.dotnet){
          try { await ctx.dotnet.invokeMethodAsync('OnDropUploadProgress', done, total, error); } catch {}
        }
      }
    } catch(ex){
      error = ex?.message || 'Upload failed';
    }
    if (ctx.dotnet){
      try { await ctx.dotnet.invokeMethodAsync('OnDropUploadCompleted'); } catch {}
    }
  };

  const handlers = [
    { ev:'dragenter', fn:onPrevent },
    { ev:'dragover', fn:onPrevent },
    { ev:'dragleave', fn:onPrevent },
    { ev:'drop', fn:onPrevent },
    { ev:'dragenter', fn:highlight },
    { ev:'dragover', fn:highlight },
    { ev:'dragleave', fn:unhighlight },
    { ev:'drop', fn:unhighlight },
    { ev:'drop', fn:onDrop },
  ];
  handlers.forEach(h => el.addEventListener(h.ev, h.fn, false));

  __apRegistry.set(el, { handlers, ctx });
}

export function unregisterDropArea(elementId){
  const el = document.getElementById(elementId);
  if (!el) return;
  const reg = __apRegistry.get(el);
  if (!reg) return;
  reg.handlers.forEach(h => el.removeEventListener(h.ev, h.fn, false));
  __apRegistry.delete(el);
}

export function AttachmentsPanel_setCategoryAttr(elementId, categoryId){
  const el = document.getElementById(elementId);
  if (!el) return;
  if (!categoryId) el.removeAttribute('data-category'); else el.setAttribute('data-category', categoryId);
}

// Lazy loading via IntersectionObserver within a scrollable container
export function registerInfiniteScroll(containerId, sentinelId, dotnet){
  const container = document.getElementById(containerId);
  const sentinel = document.getElementById(sentinelId);
  if (!container || !sentinel) return;

  // use existing registry so that we can clean up observers on dispose
  const existing = __apRegistry.get(container);
  if (existing && existing.io){
    // update dotnet target
    existing.ioCtx.dotnet = dotnet;
    return;
  }

  const ioCtx = { dotnet };
  const io = new IntersectionObserver(async (entries) => {
    for (const entry of entries){
      if (entry.isIntersecting){
        if (!ioCtx.dotnet) return;
        try { await ioCtx.dotnet.invokeMethodAsync('OnNeedMoreAsync'); } catch {}
      }
    }
  }, { root: container, rootMargin: '0px 0px 150px 0px', threshold: 0 });

  io.observe(sentinel);
  __apRegistry.set(container, { io, ioCtx });
}

export function unregisterInfiniteScroll(containerId){
  const container = document.getElementById(containerId);
  if (!container) return;
  const reg = __apRegistry.get(container);
  if (reg && reg.io){
    try { reg.io.disconnect(); } catch {}
    __apRegistry.delete(container);
  }
}
