window.simulator = {
    downloadText: (fileName, content) => {
        const blob = new Blob([content], { type: "application/json" });
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement("a");
        anchor.href = url;
        anchor.download = fileName;
        anchor.click();
        URL.revokeObjectURL(url);
    }
};

window.costCompass = {
    getTheme: () => document.documentElement.getAttribute("data-theme") || "light",
    revealSetting: (settingId) => {
        const target = document.getElementById(settingId);
        if (!target) throw new Error("The setting is no longer available. Run a fresh simulation.");
        for (let ancestor = target.parentElement; ancestor; ancestor = ancestor.parentElement) {
            if (ancestor.tagName === "DETAILS") ancestor.open = true;
        }
        const highlighted = target.closest(".cp-field-blocking") || target;
        highlighted.scrollIntoView({ block: "center", behavior: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? "instant" : "smooth" });
        target.focus({ preventScroll: true });
    },
    toggleTheme: async () => {
        const theme = document.documentElement.getAttribute("data-theme") === "dark" ? "light" : "dark";
        document.documentElement.setAttribute("data-theme", theme);
        const url = new URL(window.location.href);
        url.searchParams.set("scoutTheme", theme);
        window.history.replaceState(window.history.state, "", url);
        await window.decisionFlow?.rerenderAll();
        return theme;
    }
};

window.decisionFlow = {
    mermaidPromise: null,
    observers: new Map(),
    registrations: new Map(),

    loadMermaid: () => {
        if (window.mermaid) return Promise.resolve(window.mermaid);
        if (window.decisionFlow.mermaidPromise) return window.decisionFlow.mermaidPromise;

        const script = document.createElement("script");
        window.decisionFlow.mermaidPromise = new Promise((resolve, reject) => {
            script.src = "vendor/mermaid/mermaid.min.js";
            script.onload = () => resolve(window.mermaid);
            script.onerror = () => reject(new Error("The Mermaid runtime could not be loaded."));
            document.head.appendChild(script);
        }).catch(error => {
            script.remove();
            window.decisionFlow.mermaidPromise = null;
            throw error;
        });
        return window.decisionFlow.mermaidPromise;
    },

    observe: (containerId, sourceUrl, loadingId, descriptionId, errorId) => {
        const container = document.getElementById(containerId);
        const section = container?.closest("section");
        if (!container || !section) return;

        const registration = { containerId, sourceUrl, loadingId, descriptionId, errorId, render: null };
        window.decisionFlow.registrations.set(containerId, registration);
        const render = async (focusSection = false) => {
            window.decisionFlow.disconnect(containerId, false);
            const loading = document.getElementById(loadingId);
            const target = document.getElementById(errorId);
            if (loading) {
                loading.hidden = false;
                loading.removeAttribute("aria-hidden");
            }
            if (target) {
                target.hidden = true;
                target.removeAttribute("role");
                target.textContent = "";
            }
            try {
                await window.decisionFlow.render(containerId, sourceUrl, loadingId, descriptionId);
            } catch (error) {
                if (loading) {
                    loading.hidden = true;
                    loading.setAttribute("aria-hidden", "true");
                }
                if (target) {
                    target.hidden = false;
                    target.setAttribute("role", "alert");
                    target.textContent = `The decision flow could not be rendered. ${error.message}`;
                }
            } finally {
                if (focusSection) {
                    section.focus({ preventScroll: true });
                }
            }
        };
        registration.render = render;

        if (window.location.hash === `#${section.id}`) {
            void render(true);
            return;
        }

        const observer = new IntersectionObserver(entries => {
            if (entries.some(entry => entry.isIntersecting)) void render();
        }, { rootMargin: "300px" });
        window.decisionFlow.observers.set(containerId, observer);
        observer.observe(section);
    },

    reveal: async (sectionId) => {
        const section = document.getElementById(sectionId);
        if (!section) return;
        window.history.replaceState(window.history.state, "", `#${sectionId}`);
        section.scrollIntoView({ block: "start", behavior: "smooth" });
        const registration = [...window.decisionFlow.registrations.values()]
            .find(value => document.getElementById(value.containerId)?.closest("section") === section);
        await registration?.render?.(true);
    },

    disconnect: (containerId, removeRegistration = true) => {
        window.decisionFlow.observers.get(containerId)?.disconnect();
        window.decisionFlow.observers.delete(containerId);
        if (removeRegistration) window.decisionFlow.registrations.delete(containerId);
    },

    rerenderAll: async () => {
        for (const registration of window.decisionFlow.registrations.values()) {
            if (document.getElementById(registration.containerId)?.querySelector("svg")) {
                await registration.render?.(false);
            }
        }
    },

    showError: (errorId, message, loadingId) => {
        const loading = document.getElementById(loadingId);
        if (loading) {
            loading.hidden = true;
            loading.setAttribute("aria-hidden", "true");
        }
        const target = document.getElementById(errorId);
        if (!target) return;
        target.hidden = false;
        target.setAttribute("role", "alert");
        target.textContent = `The diagram control could not be applied. ${message}`;
    },

    render: async (containerId, sourceUrl, loadingId, descriptionId) => {
        const container = document.getElementById(containerId);
        if (!container) {
            throw new Error(`Decision-flow container '${containerId}' was not found.`);
        }

        const response = await fetch(sourceUrl, { cache: "no-cache" });
        if (!response.ok) {
            throw new Error(`Decision-flow source could not be loaded (${response.status}).`);
        }

        const source = await response.text();
        const mermaid = await window.decisionFlow.loadMermaid();
        const dark = document.documentElement.getAttribute("data-theme") !== "light";
        mermaid.initialize({
            startOnLoad: false,
            securityLevel: "strict",
            theme: dark ? "dark" : "neutral",
            flowchart: { htmlLabels: true, useMaxWidth: false }
        });

        const renderId = `decision-flow-${crypto.randomUUID()}`;
        const { svg, bindFunctions } = await mermaid.render(renderId, source);
        container.innerHTML = svg;
        bindFunctions?.(container);

        const diagram = container.querySelector("svg");
        if (!diagram) {
            throw new Error("Mermaid returned no SVG diagram.");
        }

        diagram.setAttribute("role", "img");
        diagram.setAttribute("aria-label", "GitHub Copilot AI-credit financial decision flow");
        diagram.setAttribute("aria-describedby", descriptionId);
        diagram.setAttribute("preserveAspectRatio", "xMinYMin meet");
        diagram.removeAttribute("height");
        container.dataset.zoom = window.matchMedia("(max-width: 760px)").matches ? "1.5" : "1";
        window.decisionFlow.applyZoom(container);
        const loading = document.getElementById(loadingId);
        if (loading) {
            loading.hidden = true;
            loading.setAttribute("aria-hidden", "true");
        }
        window.decisionFlow.center(container);
    },

    zoom: (containerId, change) => {
        const container = document.getElementById(containerId);
        if (!container) return;
        const current = Number(container.dataset.zoom || "1");
        container.dataset.zoom = String(Math.min(2, Math.max(0.5, current + change)));
        window.decisionFlow.applyZoom(container);
    },

    fit: (containerId) => {
        const container = document.getElementById(containerId);
        if (!container) return;
        const viewport = container.parentElement;
        const available = Math.max(1, (viewport?.clientWidth || container.clientWidth) - 24);
        container.dataset.zoom = String(Math.min(1, available / container.clientWidth));
        window.decisionFlow.applyZoom(container);
        viewport?.scrollTo({ top: 0, left: 0, behavior: "smooth" });
    },

    applyZoom: (container) => {
        const diagram = container.querySelector("svg");
        if (!diagram) return;
        const zoom = Number(container.dataset.zoom || "1");
        diagram.style.width = `${zoom * 100}%`;
        diagram.style.maxWidth = "none";
        diagram.style.height = "auto";
    },

    center: (container, smooth = false) => {
        const viewport = container.parentElement;
        if (!viewport) return;
        viewport.scrollTo({
            top: 0,
            left: Math.max(0, (viewport.scrollWidth - viewport.clientWidth) / 2),
            behavior: smooth ? "smooth" : "auto"
        });
    }
};
