window.fmScroll = {
    getScrollInfo: el => {
        if (!el) { return { scrollTop: 0, scrollHeight: 0, clientHeight: 0 }; }
        return {
            scrollTop: el.scrollTop,
            scrollHeight: el.scrollHeight,
            clientHeight: el.clientHeight
        };
    }
};

window.fmInfinite = (function () {
    let observer;
    let currentSentinel;
    let currentCallback;
    function observe(sentinel, dotNetRef, rootSelector) {
        if (!sentinel || !dotNetRef) {
            return;
        }
        currentSentinel = sentinel;

        const root = rootSelector ? document.querySelector(rootSelector) : null;

        if (observer) {
            observer.disconnect();
        }

        currentCallback = entries => {
            for (const e of entries) {
                if (e.isIntersecting) {
                    dotNetRef.invokeMethodAsync('LoadMoreFromJs').catch(() => { });
                }
            }
        };

        observer = new IntersectionObserver(currentCallback, {
            root,
            rootMargin: '0px 0px 400px 0px',
            threshold: 0
        });

        observer.observe(sentinel);
    }

    function refresh() {
        // Erzwingt eine erneute Prüfung (Workaround für seltene fehlende Re-Trigger)
        if (observer && currentSentinel && currentCallback) {
            observer.unobserve(currentSentinel);
            observer.observe(currentSentinel);
        }
    }

    return { observe, refresh };
})();