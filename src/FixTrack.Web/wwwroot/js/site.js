// FixTrack: comportamiento de la interfaz (menú lateral móvil y tema de color).
(() => {
    "use strict";

    const root = document.documentElement;

    // --- Menú lateral en pantallas pequeñas -----------------------------------

    const app = document.querySelector("[data-app]");
    const sidebar = document.querySelector("[data-sidebar]");
    const openButton = document.querySelector("[data-sidebar-open]");
    const desktop = window.matchMedia("(min-width: 1024px)");

    const isSidebarOpen = () => app?.hasAttribute("data-sidebar-expanded") ?? false;

    function setSidebarOpen(open, { restoreFocus = true } = {}) {
        if (!app || !sidebar || !openButton || open === isSidebarOpen()) return;

        app.toggleAttribute("data-sidebar-expanded", open);
        root.classList.toggle("is-scroll-locked", open);
        openButton.setAttribute("aria-expanded", String(open));

        if (open) {
            const target = sidebar.querySelector("a[aria-current='page']") ?? sidebar.querySelector("a[href]");
            target?.focus();
        } else if (restoreFocus) {
            openButton.focus();
        }
    }

    openButton?.addEventListener("click", () => setSidebarOpen(true));

    document.querySelectorAll("[data-sidebar-close]").forEach((element) =>
        element.addEventListener("click", () => setSidebarOpen(false)));

    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape" && isSidebarOpen()) setSidebarOpen(false);
    });

    // Mantiene el foco dentro del menú mientras está abierto.
    sidebar?.addEventListener("keydown", (event) => {
        if (event.key !== "Tab" || !isSidebarOpen()) return;

        const focusable = [...sidebar.querySelectorAll("a[href], button:not([disabled])")]
            .filter((element) => element.offsetParent !== null);
        const first = focusable[0];
        const last = focusable.at(-1);

        if (event.shiftKey && document.activeElement === first) {
            event.preventDefault();
            last?.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault();
            first?.focus();
        }
    });

    // Al pasar a escritorio el menú vuelve a ser fijo.
    desktop.addEventListener("change", (event) => {
        if (event.matches) setSidebarOpen(false, { restoreFocus: false });
    });

    // --- Tema claro / oscuro --------------------------------------------------

    const THEME_KEY = "fixtrack-theme";
    const themeToggle = document.querySelector("[data-theme-toggle]");
    const systemPrefersDark = window.matchMedia("(prefers-color-scheme: dark)");

    const currentTheme = () => root.dataset.theme ?? (systemPrefersDark.matches ? "dark" : "light");

    function updateThemeToggle() {
        if (!themeToggle) return;
        const label = currentTheme() === "dark" ? "Cambiar a tema claro" : "Cambiar a tema oscuro";
        themeToggle.setAttribute("aria-label", label);
        themeToggle.setAttribute("title", label);
    }

    themeToggle?.addEventListener("click", () => {
        const next = currentTheme() === "dark" ? "light" : "dark";
        root.dataset.theme = next;
        try {
            localStorage.setItem(THEME_KEY, next);
        } catch {
            // Sin almacenamiento disponible: el tema se aplica solo a esta página.
        }
        updateThemeToggle();
    });

    systemPrefersDark.addEventListener("change", updateThemeToggle);
    updateThemeToggle();

    // --- Diálogos de confirmación (<dialog> nativo) ---------------------------

    document.querySelectorAll("[data-dialog-open]").forEach((button) => {
        const dialog = document.getElementById(button.dataset.dialogOpen);
        if (!(dialog instanceof HTMLDialogElement)) return;

        button.addEventListener("click", () => dialog.showModal());
        dialog.querySelectorAll("[data-dialog-close]").forEach((close) =>
            close.addEventListener("click", () => dialog.close()));

        // Un clic en el fondo (fuera del contenido) cierra el diálogo.
        dialog.addEventListener("click", (event) => {
            if (event.target === dialog) dialog.close();
        });
    });

    // --- Avisos que se pueden cerrar ------------------------------------------

    document.querySelectorAll("[data-alert-close]").forEach((button) =>
        button.addEventListener("click", () => button.closest("[data-alert]")?.remove()));

    // --- Contador de caracteres de los campos de texto largos -----------------

    document.querySelectorAll("[data-char-counter-for]").forEach((counter) => {
        const field = document.getElementById(counter.dataset.charCounterFor);
        if (!field || !field.maxLength || field.maxLength < 0) return;

        const update = () => { counter.textContent = `${field.value.length}/${field.maxLength}`; };
        field.addEventListener("input", update);
        update();
    });

    // --- Resumen de errores: recibe el foco para que se anuncie y se vea ------

    document.querySelector("[data-error-summary]")?.focus();
})();
