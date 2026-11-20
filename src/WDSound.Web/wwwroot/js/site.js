const consentDialog = document.querySelector("[data-consent]");
const analyticsId = document.body.dataset.analytics;
let consent = localStorage.getItem("wdsound-analytics");
let analyticsLoaded = false;

function activateAnalytics() {
    if (analyticsLoaded || consent !== "yes" || !/^G-[A-Z0-9]+$/.test(analyticsId ?? "")) return;
    window["ga-disable-" + analyticsId] = false;
    window.dataLayer = window.dataLayer || [];
    window.gtag = function () { window.dataLayer.push(arguments); };
    window.gtag("js", new Date());
    window.gtag("config", analyticsId, { allow_google_signals: false });
    const kind = document.body.dataset.itemKind;
    if (kind === "product" || kind === "package") window.gtag("event", "view_" + kind);
    const script = document.createElement("script");
    script.src = "https://www.googletagmanager.com/gtag/js?id=" + analyticsId;
    script.async = true;
    document.head.append(script);
    analyticsLoaded = true;
}

if (consentDialog) {
    if (consent === null) consentDialog.showModal();
    document.querySelector("[data-consent-open]").addEventListener("click", () => consentDialog.showModal());
    consentDialog.querySelectorAll("[data-consent-choice]").forEach(button => button.addEventListener("click", () => {
        consent = button.dataset.consentChoice;
        localStorage.setItem("wdsound-analytics", consent);
        window["ga-disable-" + analyticsId] = consent !== "yes";
        activateAnalytics();
        consentDialog.close();
    }));
    activateAnalytics();
}

document.querySelectorAll("[data-channel]").forEach(link => link.addEventListener("click", event => {
    const context = link.closest("[data-contextual]");
    if (context) {
        const required = context.querySelectorAll("[required]");
        for (const field of required) {
            if (!field.reportValidity()) {
                event.preventDefault();
                return;
            }
        }
        const extra = [...context.querySelectorAll("[data-inquiry-field]")]
            .filter(field => field.value.trim())
            .map(field => field.labels[0].textContent + ": " + field.value.trim());
        const base = link.dataset.baseHref || link.href;
        link.dataset.baseHref = base;
        const url = new URL(base);
        const parameter = link.dataset.channel === "email" ? "body" : "text";
        const text = [url.searchParams.get(parameter), ...extra].join("\n");
        if (link.dataset.channel === "email") {
            const subject = url.searchParams.get("subject") ?? "";
            url.search = "subject=" + encodeURIComponent(subject) + "&body=" + encodeURIComponent(text);
        } else {
            url.searchParams.set(parameter, text);
        }
        link.href = url.href;
    }
    if (consent === "yes" && window.gtag) {
        window.gtag("event", "contact_" + link.dataset.channel + "_click");
    }
}));
