// FixTrack: comportamiento de la interfaz. Todo funciona sin JavaScript; estas funciones mejoran la experiencia.
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

    // --- Filtros que se aplican al cambiar (por ejemplo, el tipo de equipo) ---

    document.querySelectorAll("select[data-autosubmit]").forEach((select) =>
        select.addEventListener("change", () => select.form?.requestSubmit()));

    // --- Buscador de clientes (patrón combobox de ARIA) -----------------------

    document.querySelectorAll("[data-combobox]").forEach(setUpCombobox);

    function setUpCombobox(container) {
        const input = container.querySelector("[role='combobox']");
        const hidden = container.querySelector("[data-combobox-value]");
        const list = container.querySelector("[role='listbox']");
        const selected = container.querySelector("[data-combobox-selected]");
        const source = container.dataset.source;
        if (!input || !hidden || !list || !source) return;

        let options = [];
        let active = -1;
        let timer;
        let pending;

        const describe = (option) => [option.phone, option.email].filter(Boolean).join(" · ");

        function close() {
            list.hidden = true;
            input.setAttribute("aria-expanded", "false");
            input.removeAttribute("aria-activedescendant");
            active = -1;
        }

        function highlight(index) {
            active = index;
            list.querySelectorAll("[role='option']").forEach((item, i) => item.setAttribute("aria-selected", String(i === index)));
            const current = list.querySelector(`[data-index="${index}"]`);
            if (current) {
                input.setAttribute("aria-activedescendant", current.id);
                current.scrollIntoView({ block: "nearest" });
            } else {
                input.removeAttribute("aria-activedescendant");
            }
        }

        function render() {
            list.replaceChildren();
            if (options.length === 0) {
                const empty = document.createElement("li");
                empty.className = "combobox__empty";
                empty.setAttribute("role", "presentation");
                empty.textContent = "No hay clientes activos que coincidan.";
                list.append(empty);
            }

            options.forEach((option, index) => {
                const item = document.createElement("li");
                item.id = `${list.id}-${index}`;
                item.className = "combobox__option";
                item.setAttribute("role", "option");
                item.dataset.index = String(index);

                const name = document.createElement("span");
                name.className = "combobox__option-name";
                name.textContent = option.fullName;
                const meta = document.createElement("span");
                meta.className = "combobox__option-meta";
                meta.textContent = describe(option);
                item.append(name, meta);
                list.append(item);
            });

            list.hidden = false;
            input.setAttribute("aria-expanded", "true");
            highlight(options.length > 0 ? 0 : -1);
        }

        async function search(text) {
            pending?.abort();
            pending = new AbortController();
            const url = new URL(source, window.location.origin);
            if (text) url.searchParams.set("buscar", text);
            try {
                const response = await fetch(url, { signal: pending.signal, headers: { Accept: "application/json" } });
                if (!response.ok) return;
                options = await response.json();
                if (document.activeElement === input) render();
            } catch (error) {
                if (error.name !== "AbortError") close();
            }
        }

        function choose(index) {
            const option = options[index];
            if (!option) return;
            hidden.value = String(option.id);
            input.value = option.fullName;
            if (selected) {
                selected.textContent = describe(option);
                selected.hidden = false;
            }
            close();
            container.dispatchEvent(new CustomEvent("combobox:change", { bubbles: true, detail: { id: option.id } }));
        }

        // Escribir invalida la selección anterior: el servidor exige un cliente elegido de la lista.
        input.addEventListener("input", () => {
            if (hidden.value) {
                container.dispatchEvent(new CustomEvent("combobox:change", { bubbles: true, detail: { id: null } }));
            }
            hidden.value = "";
            if (selected) selected.hidden = true;
            // Los resultados anteriores ya no corresponden al texto: se ocultan hasta recibir los nuevos.
            options = [];
            close();
            clearTimeout(timer);
            timer = setTimeout(() => search(input.value.trim()), 200);
        });

        input.addEventListener("focus", () => {
            if (!hidden.value) search(input.value.trim());
        });

        input.addEventListener("keydown", (event) => {
            const open = !list.hidden;
            if (event.key === "ArrowDown") {
                event.preventDefault();
                if (!open) search(input.value.trim());
                else highlight(Math.min(active + 1, options.length - 1));
            } else if (event.key === "ArrowUp" && open) {
                event.preventDefault();
                highlight(Math.max(active - 1, 0));
            } else if (event.key === "Enter" && open && active >= 0) {
                event.preventDefault();
                choose(active);
            } else if (event.key === "Enter" && !hidden.value) {
                // Sin un cliente elegido, Enter no envía el formulario: todavía se está buscando.
                event.preventDefault();
            } else if (event.key === "Escape" && open) {
                event.preventDefault();
                close();
            }
        });

        // mousedown en lugar de click: elige antes de que el campo pierda el foco.
        list.addEventListener("mousedown", (event) => {
            const item = event.target.closest("[role='option']");
            if (!item) return;
            event.preventDefault();
            choose(Number(item.dataset.index));
        });

        input.addEventListener("blur", close);
    }

    // --- Equipos del cliente elegido (formulario de nueva orden) -------------

    document.querySelectorAll("[data-device-field]").forEach((field) => {
        const select = field.querySelector("select");
        const hint = field.querySelector("[data-device-hint]");
        const template = field.dataset.sourceTemplate;
        const form = field.closest("form");
        if (!select || !hint || !template || !form) return;

        let pending;

        function reset(message, disabled) {
            select.replaceChildren(new Option("Selecciona el equipo", ""));
            select.disabled = disabled;
            hint.textContent = message;
        }

        async function load(customerId) {
            pending?.abort();
            if (!customerId) {
                reset("Primero elige el cliente para ver sus equipos.", true);
                return;
            }

            pending = new AbortController();
            reset("Cargando los equipos del cliente…", true);
            try {
                const url = new URL(template.replace(/\/0\/equipos$/, `/${customerId}/equipos`), window.location.origin);
                const response = await fetch(url, { signal: pending.signal, headers: { Accept: "application/json" } });
                if (!response.ok) throw new Error(String(response.status));
                const devices = await response.json();
                if (devices.length === 0) {
                    reset("Este cliente no tiene equipos registrados. Regístralo primero desde el módulo Equipos.", true);
                    return;
                }

                reset("Equipos registrados a nombre del cliente.", false);
                devices.forEach((device) => {
                    const details = [device.title, device.type, device.serialNumber].filter(Boolean).join(" · ");
                    select.append(new Option(details, String(device.id)));
                });
                if (devices.length === 1) select.value = String(devices[0].id);
            } catch (error) {
                if (error.name !== "AbortError") reset("No se pudieron cargar los equipos. Intenta de nuevo.", true);
            }
        }

        form.addEventListener("combobox:change", (event) => load(event.detail.id));
    });

    // --- Cambio de estado: el costo final solo se pide al entregar -----------

    document.querySelectorAll("[data-status-form]").forEach((form) => {
        const conditional = form.querySelectorAll("[data-show-for-status]");
        const update = () => {
            const chosen = form.querySelector("input[name='Status']:checked")?.value;
            conditional.forEach((element) => {
                element.hidden = element.dataset.showForStatus !== chosen;
            });
        };

        form.addEventListener("change", (event) => {
            if (event.target instanceof HTMLInputElement && event.target.name === "Status") update();
        });
        update();
    });

    // --- Mostrar u ocultar la contraseña ---------------------------------------

    document.querySelectorAll("[data-password-toggle]").forEach((button) => {
        const input = document.getElementById(button.dataset.passwordToggle);
        if (!(input instanceof HTMLInputElement)) return;

        button.hidden = false;
        button.addEventListener("click", () => {
            const show = input.type === "password";
            input.type = show ? "text" : "password";
            button.setAttribute("aria-pressed", String(show));
            button.setAttribute("aria-label", show ? "Ocultar la contraseña" : "Mostrar la contraseña");
            input.focus();
        });
    });

    // --- Menú de la cuenta: se cierra al hacer clic fuera o con Escape --------

    const userMenu = document.querySelector("[data-user-menu]");
    if (userMenu instanceof HTMLDetailsElement) {
        document.addEventListener("click", (event) => {
            if (userMenu.open && !userMenu.contains(event.target)) userMenu.open = false;
        });
        document.addEventListener("keydown", (event) => {
            if (event.key === "Escape" && userMenu.open) {
                userMenu.open = false;
                userMenu.querySelector("summary")?.focus();
            }
        });
    }

    // --- Resumen de errores: recibe el foco para que se anuncie y se vea ------

    document.querySelector("[data-error-summary]")?.focus();
})();
