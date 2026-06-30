(() => {
    const root = document.body;
    if (!root) {
        return;
    }

    const collapseClass = "app-side-collapsed";
    const collapseStorageKey = "calibra.sidebar.collapsed";
    const workspaceTabsStoragePrefix = "calibra.workspace.tabs.";
    const workspaceTabsPendingPrefix = "calibra.workspace.tabs.pending.";
    const formDraftStoragePrefix = "calibra.formdraft.";
    let requestWorkspaceToolbarSync = () => {};
    let _workspaceTabsApi = null;

    const clearWorkspaceTabsState = () => {
        if (root.getAttribute("data-reset-workspace-tabs") !== "1") {
            return;
        }

        try {
            for (let index = window.localStorage.length - 1; index >= 0; index -= 1) {
                const key = window.localStorage.key(index);
                if (typeof key === "string" && key.startsWith(workspaceTabsStoragePrefix)) {
                    window.localStorage.removeItem(key);
                }
            }
        } catch {
            // Ignore storage failures in restricted environments.
        }

        try {
            for (let index = window.sessionStorage.length - 1; index >= 0; index -= 1) {
                const key = window.sessionStorage.key(index);
                if (typeof key === "string" && key.startsWith(workspaceTabsPendingPrefix)) {
                    window.sessionStorage.removeItem(key);
                }
            }
        } catch {
            // Ignore storage failures in restricted environments.
        }
    };

    clearWorkspaceTabsState();

    const setupBrowserSuggestionSuppression = () => {
        const textInputTypes = new Set(["", "text", "search", "email", "url", "tel", "password", "number"]);
        const decoyClassName = "calibra-autocomplete-decoy";

        const shouldPreserveAutocomplete = (element) => element?.dataset?.autocompletePreserve === "1";

        const isTextEntryField = (field) => {
            if (field instanceof HTMLTextAreaElement) {
                return true;
            }

            if (!(field instanceof HTMLInputElement)) {
                return false;
            }

            const rawType = field.getAttribute("type") ?? field.type ?? "";
            const normalizedType = rawType.trim().toLowerCase();
            return textInputTypes.has(normalizedType);
        };

        const applyFieldSuppression = (field) => {
            if (!isTextEntryField(field) || shouldPreserveAutocomplete(field)) {
                return;
            }

            field.setAttribute("autocomplete", "off");
            field.setAttribute("autocorrect", "off");
            field.setAttribute("autocapitalize", "none");
            field.setAttribute("spellcheck", "false");
            field.setAttribute("aria-autocomplete", "none");
            field.setAttribute("data-lpignore", "true");
            field.setAttribute("data-1p-ignore", "true");
            field.setAttribute("data-form-type", "other");
        };

        const createAutocompleteDecoy = (type, autocomplete) => {
            const field = document.createElement("input");
            field.type = type;
            field.tabIndex = -1;
            field.name = "";
            field.dataset.autocompletePreserve = "1";
            field.setAttribute("aria-hidden", "true");
            field.setAttribute("autocomplete", autocomplete);
            field.className = decoyClassName;
            field.style.position = "absolute";
            field.style.inlineSize = "1px";
            field.style.blockSize = "1px";
            field.style.opacity = "0";
            field.style.pointerEvents = "none";
            field.style.margin = "0";
            field.style.padding = "0";
            field.style.border = "0";
            field.style.inset = "0 auto auto 0";
            return field;
        };

        const injectAutocompleteDecoys = (form) => {
            if (!(form instanceof HTMLFormElement) ||
                shouldPreserveAutocomplete(form) ||
                form.dataset.autocompleteDecoysInjected === "1") {
                return;
            }

            const hasTextEntryField = Array.from(form.elements).some((element) =>
                isTextEntryField(element) && !shouldPreserveAutocomplete(element));

            if (!hasTextEntryField) {
                return;
            }

            const textDecoy = createAutocompleteDecoy("text", "username");
            const passwordDecoy = createAutocompleteDecoy("password", "new-password");
            form.prepend(passwordDecoy);
            form.prepend(textDecoy);
            form.dataset.autocompleteDecoysInjected = "1";
        };

        const applyFormSuppression = (form) => {
            if (!(form instanceof HTMLFormElement) || shouldPreserveAutocomplete(form)) {
                return;
            }

            form.setAttribute("autocomplete", "off");
            form.setAttribute("data-lpignore", "true");
            injectAutocompleteDecoys(form);
        };

        const processScope = (scope) => {
            if (scope instanceof HTMLFormElement) {
                applyFormSuppression(scope);
                scope.querySelectorAll("input, textarea").forEach((field) => {
                    applyFieldSuppression(field);
                });
                return;
            }

            if (scope instanceof HTMLInputElement || scope instanceof HTMLTextAreaElement) {
                applyFieldSuppression(scope);
                if (scope.form instanceof HTMLFormElement) {
                    applyFormSuppression(scope.form);
                }
                return;
            }

            const rootElement = scope instanceof Document || scope instanceof HTMLElement
                ? scope
                : document;

            rootElement.querySelectorAll("form").forEach((form) => {
                applyFormSuppression(form);
            });

            rootElement.querySelectorAll("input, textarea").forEach((field) => {
                applyFieldSuppression(field);
            });
        };

        processScope(document);

        if (typeof MutationObserver === "undefined" || !(document.body instanceof HTMLElement)) {
            return;
        }

        const observer = new MutationObserver((mutations) => {
            mutations.forEach((mutation) => {
                mutation.addedNodes.forEach((node) => {
                    if (!(node instanceof HTMLElement)) {
                        return;
                    }

                    processScope(node);
                });
            });
        });

        observer.observe(document.body, { childList: true, subtree: true });
    };

    const setupWorkspaceFrameNavigation = () => {
        if (!root.classList.contains("workspace-frame-body") || !(document.body instanceof HTMLElement)) {
            return;
        }

        const workspaceKey = "workspace";
        const workspaceValue = "1";

        const toRelativeUrl = (url) => `${url.pathname}${url.search}${url.hash}`;

        const preserveWorkspaceUrl = (rawUrl) => {
            const value = typeof rawUrl === "string" ? rawUrl.trim() : "";
            if (!value) {
                return null;
            }

            try {
                const parsed = new URL(value, window.location.href);
                if (parsed.origin !== window.location.origin) {
                    return null;
                }

                if (parsed.protocol !== "http:" && parsed.protocol !== "https:") {
                    return null;
                }

                parsed.searchParams.set(workspaceKey, workspaceValue);
                return toRelativeUrl(parsed);
            } catch {
                return null;
            }
        };

        const shouldSkipAnchor = (anchor) => {
            if (!(anchor instanceof HTMLAnchorElement)) {
                return true;
            }

            const rawHref = anchor.getAttribute("href")?.trim() ?? "";
            if (!rawHref || rawHref.startsWith("#")) {
                return true;
            }

            if (anchor.dataset.workspacePreserveSkip === "1" ||
                (anchor.target && anchor.target !== "_self") ||
                anchor.hasAttribute("download")) {
                return true;
            }

            const schemeMatch = rawHref.match(/^([a-z][a-z0-9+.-]*:)/i);
            return Boolean(schemeMatch && !/^https?:$/i.test(schemeMatch[1]));
        };

        const rewriteAnchor = (anchor) => {
            if (shouldSkipAnchor(anchor)) {
                return;
            }

            const nextHref = preserveWorkspaceUrl(anchor.getAttribute("href") ?? anchor.href);
            if (!nextHref) {
                return;
            }

            if (anchor.getAttribute("href") !== nextHref) {
                anchor.setAttribute("href", nextHref);
            }
        };

        const rewriteForm = (form) => {
            if (!(form instanceof HTMLFormElement) ||
                form.dataset.workspacePreserveSkip === "1" ||
                (form.target && form.target !== "_self")) {
                return;
            }

            const currentLocation = `${window.location.pathname}${window.location.search}${window.location.hash}`;
            const rawAction = form.getAttribute("action")?.trim() || currentLocation;
            const nextAction = preserveWorkspaceUrl(rawAction);
            if (!nextAction) {
                return;
            }

            if (form.getAttribute("action") !== nextAction) {
                form.setAttribute("action", nextAction);
            }
        };

        const processScope = (scope) => {
            if (scope instanceof HTMLAnchorElement) {
                rewriteAnchor(scope);
                return;
            }

            if (scope instanceof HTMLFormElement) {
                rewriteForm(scope);
                return;
            }

            const rootElement = scope instanceof Document || scope instanceof HTMLElement
                ? scope
                : document;

            rootElement.querySelectorAll("a[href]").forEach((anchor) => {
                rewriteAnchor(anchor);
            });

            rootElement.querySelectorAll("form").forEach((form) => {
                rewriteForm(form);
            });
        };

        processScope(document);
        document.addEventListener("submit", (event) => {
            if (event.target instanceof HTMLFormElement) {
                rewriteForm(event.target);
            }
        }, true);

        if (typeof MutationObserver === "undefined") {
            return;
        }

        const observer = new MutationObserver((mutations) => {
            mutations.forEach((mutation) => {
                mutation.addedNodes.forEach((node) => {
                    if (!(node instanceof HTMLElement)) {
                        return;
                    }

                    processScope(node);
                });
            });
        });

        observer.observe(document.body, { childList: true, subtree: true });
    };

    const applyCollapseState = (collapsed) => {
        root.classList.toggle(collapseClass, collapsed);

        if (collapsed) {
            document.querySelectorAll("details.side-group, details.admin-group").forEach((group) => {
                group.removeAttribute("open");
            });
        }

        document.querySelectorAll("[data-sidebar-toggle]").forEach((button) => {
            button.setAttribute("aria-expanded", (!collapsed).toString());
        });
    };

    let collapsedByDefault = false;
    try {
        collapsedByDefault = window.localStorage.getItem(collapseStorageKey) === "1";
    } catch {
        collapsedByDefault = false;
    }

    applyCollapseState(collapsedByDefault);

    document.querySelectorAll("[data-sidebar-toggle]").forEach((button) => {
        button.addEventListener("click", () => {
            const nextCollapsed = !root.classList.contains(collapseClass);
            applyCollapseState(nextCollapsed);

            try {
                window.localStorage.setItem(collapseStorageKey, nextCollapsed ? "1" : "0");
            } catch {
                // Ignore storage failures in restricted environments.
            }
        });
    });

    const brandLogo = document.querySelector(".brand-logo");
    if (brandLogo) {
        brandLogo.addEventListener("click", (e) => {
            if (root.classList.contains(collapseClass)) {
                e.preventDefault();
                applyCollapseState(false);
                try {
                    window.localStorage.setItem(collapseStorageKey, "0");
                } catch {
                    // Ignore storage failures in restricted environments.
                }
            }
        });
    }

    const normalize = (value) => value.toLocaleLowerCase("tr-TR").trim();
    const favoritesChangedEventName = "calibra:favorites-changed";

    const setupToastSystem = () => {
        const toastRootId = "calibra-toast-root";
        const closeAnimationMs = 220;
        const defaultDurationMs = 5000;
        const maxDurationMs = 60000;
        const dismissTimerByToast = new WeakMap();

        const getToastRoot = () => {
            const existing = document.getElementById(toastRootId);
            if (existing instanceof HTMLElement) {
                return existing;
            }

            const rootElement = document.createElement("div");
            rootElement.id = toastRootId;
            rootElement.className = "toast-root toast-root--top-right";
            rootElement.setAttribute("role", "status");
            rootElement.setAttribute("aria-live", "polite");
            rootElement.setAttribute("aria-relevant", "additions text");
            document.body.appendChild(rootElement);
            return rootElement;
        };

        const iconMarkupByType = {
            success: "<svg viewBox='0 0 24 24' aria-hidden='true'><circle cx='12' cy='12' r='9'></circle><path d='m8.7 12.2 2.2 2.3 4.6-4.8'></path></svg>",
            error: "<svg viewBox='0 0 24 24' aria-hidden='true'><circle cx='12' cy='12' r='9'></circle><path d='M9 9 15 15M15 9 9 15'></path></svg>",
            warning: "<svg viewBox='0 0 24 24' aria-hidden='true'><path d='M12 3.5 21 19H3z'></path><path d='M12 9v5'></path><path d='M12 16h.01'></path></svg>",
            info: "<svg viewBox='0 0 24 24' aria-hidden='true'><circle cx='12' cy='12' r='9'></circle><path d='M12 10v6'></path><path d='M12 7h.01'></path></svg>"
        };

        const normalizeType = (value) => {
            const raw = typeof value === "string" ? value.trim().toLowerCase() : "";
            return raw === "success" || raw === "error" || raw === "warning" ? raw : "info";
        };

        const normalizePosition = (value) => {
            const raw = typeof value === "string" ? value.trim().toLowerCase() : "";
            return raw === "top-center" ? "top-center" : "top-right";
        };

        const resolveDuration = (value) => {
            if (value === null || value === undefined) {
                return defaultDurationMs;
            }

            const parsed = Number(value);
            if (!Number.isFinite(parsed)) {
                return defaultDurationMs;
            }

            if (parsed <= 0) {
                return 0;
            }

            return Math.min(Math.max(Math.round(parsed), 1000), maxDurationMs);
        };

        const updateRootPosition = (toastRoot, position) => {
            toastRoot.classList.remove("toast-root--top-right", "toast-root--top-center");
            toastRoot.classList.add(position === "top-center" ? "toast-root--top-center" : "toast-root--top-right");
        };

        const dismissToast = (toast, force = false) => {
            if (!(toast instanceof HTMLElement) || !toast.isConnected) {
                return;
            }

            if (!force && toast.dataset.closing === "1") {
                return;
            }

            toast.dataset.closing = "1";
            toast.classList.add("is-closing");

            const dismissTimer = dismissTimerByToast.get(toast);
            if (dismissTimer) {
                window.clearTimeout(dismissTimer);
                dismissTimerByToast.delete(toast);
            }

            const removeToast = () => {
                if (toast.isConnected) {
                    toast.remove();
                }
            };

            toast.addEventListener("animationend", removeToast, { once: true });
            window.setTimeout(removeToast, closeAnimationMs + 40);
        };

        window.showToast = (options) => {
            const payload = typeof options === "string"
                ? { message: options }
                : (options ?? {});

            const message = typeof payload.message === "string"
                ? payload.message.trim()
                : "";

            if (!message) {
                return;
            }

            const type = normalizeType(payload.type);
            const position = normalizePosition(payload.position);
            const duration = resolveDuration(payload.duration);

            const toastRoot = getToastRoot();
            updateRootPosition(toastRoot, position);

            const toast = document.createElement("article");
            toast.className = `calibra-toast calibra-toast--${type}`;
            toast.setAttribute("role", "status");
            toast.setAttribute("aria-live", "polite");
            toast.setAttribute("aria-atomic", "true");

            const icon = document.createElement("span");
            icon.className = "calibra-toast__icon";
            icon.innerHTML = iconMarkupByType[type] ?? iconMarkupByType.info;

            const messageElement = document.createElement("div");
            messageElement.className = "calibra-toast__message";
            messageElement.textContent = message;

            const closeButton = document.createElement("button");
            closeButton.type = "button";
            closeButton.className = "calibra-toast__close";
            closeButton.setAttribute("aria-label", "Bildirimi kapat");
            closeButton.innerHTML = "<svg viewBox='0 0 24 24' aria-hidden='true'><path d='M6 6 18 18M18 6 6 18'></path></svg>";
            closeButton.addEventListener("click", () => {
                dismissToast(toast);
            });

            if (duration > 0) {
                toast.style.setProperty("--toast-duration-ms", `${duration}ms`);

                const progress = document.createElement("span");
                progress.className = "calibra-toast__progress";
                progress.setAttribute("aria-hidden", "true");
                toast.append(icon, messageElement, closeButton, progress);

                const dismissTimer = window.setTimeout(() => {
                    dismissToast(toast);
                }, duration);
                dismissTimerByToast.set(toast, dismissTimer);
            } else {
                toast.append(icon, messageElement, closeButton);
            }

            toastRoot.appendChild(toast);
        };

        const emitToast = (type, message, options) => {
            const payload = typeof options === "object" && options !== null
                ? { ...options }
                : {};

            payload.type = type;
            payload.message = message;

            if (!payload.position) {
                payload.position = "top-right";
            }

            if (payload.duration === undefined || payload.duration === null) {
                payload.duration = defaultDurationMs;
            }

            window.showToast(payload);
        };

        const toastApi = (typeof window.toast === "object" && window.toast !== null)
            ? window.toast
            : {};

        if (typeof toastApi.success !== "function") {
            toastApi.success = (message, options) => emitToast("success", message, options);
        }

        if (typeof toastApi.error !== "function") {
            toastApi.error = (message, options) => emitToast("error", message, options);
        }

        if (typeof toastApi.warning !== "function") {
            toastApi.warning = (message, options) => emitToast("warning", message, options);
        }

        if (typeof toastApi.info !== "function") {
            toastApi.info = (message, options) => emitToast("info", message, options);
        }

        window.toast = toastApi;

        window.dismissToast = (selectorOrElement) => {
            if (selectorOrElement instanceof HTMLElement) {
                dismissToast(selectorOrElement);
                return;
            }

            if (typeof selectorOrElement === "string" && selectorOrElement.trim()) {
                const matched = document.querySelector(selectorOrElement);
                if (matched instanceof HTMLElement) {
                    dismissToast(matched);
                }
            }
        };
    };

    const setupValidationToasts = () => {
        const $ = window.jQuery;
        if (!$ || !$.validator) {
            return;
        }

        const emitErrorToast = (message) => {
            const normalizedMessage = typeof message === "string" ? message.trim() : "";
            if (!normalizedMessage) {
                return;
            }

            if (window.toast && typeof window.toast.error === "function") {
                window.toast.error(normalizedMessage);
                return;
            }

            if (typeof window.showToast === "function") {
                window.showToast({
                    type: "error",
                    message: normalizedMessage,
                    position: "top-right",
                    duration: 5000
                });
            }
        };

        const bindValidationToast = (form) => {
            if (!(form instanceof HTMLFormElement) || form.dataset.validationToastBound === "1") {
                return;
            }

            const formRef = $(form);

            const validator = formRef.data("validator");
            if (validator?.settings) {
                validator.settings.errorPlacement = (error, element) => {
                    error.addClass("field-validation-error");
                    const field = element.closest(".integrator-field, .form-group, .mb-3");
                    if (field.length) {
                        field.append(error);
                    } else {
                        error.insertAfter(element);
                    }
                };

                validator.settings.highlight = (element) => {
                    $(element).addClass("input-validation-error");
                    $(element).closest(".integrator-field").addClass("has-error");
                };

                validator.settings.unhighlight = (element) => {
                    $(element).removeClass("input-validation-error");
                    $(element).closest(".integrator-field").removeClass("has-error");
                    $(element).siblings(".field-validation-error").remove();
                };

                validator.settings.success = (label) => {
                    label.remove();
                };

                validator.settings.invalidHandler = (_event, validatorInstance) => {
                    const firstError = validatorInstance?.errorList?.[0]?.element;
                    if (firstError) {
                        firstError.scrollIntoView({ behavior: "smooth", block: "center" });
                        firstError.focus();
                    }
                };
            }

            form.dataset.validationToastBound = "1";
        };

        const bindAllForms = () => {
            document.querySelectorAll("form").forEach((form) => bindValidationToast(form));
        };

        bindAllForms();
        window.setTimeout(bindAllForms, 0);
    };

    const setupFavorites = (menuRoot) => {
        const scope = menuRoot.getAttribute("data-menu-scope") ?? "default";
        const favoritesStorageKey = `calibra.sidebar.favorites.${scope}`;
        const favoritesPanel = menuRoot.querySelector("[data-favorites-panel]");
        const favoritesList = menuRoot.querySelector("[data-favorites-list]");

        const entries = Array.from(menuRoot.querySelectorAll("[data-menu-entry]"))
            .filter((entry) => entry.hasAttribute("data-favorite-key"));

        if (entries.length === 0) {
            if (favoritesPanel) {
                favoritesPanel.hidden = true;
            }
            return;
        }

        const entryByKey = new Map();
        entries.forEach((entry) => {
            const key = entry.getAttribute("data-favorite-key");
            if (!key || entryByKey.has(key)) {
                return;
            }

            const anchor = entry.querySelector("a[href]");
            if (!(anchor instanceof HTMLAnchorElement)) {
                return;
            }

            const toggleButton = entry.querySelector("[data-favorite-toggle]");
            entryByKey.set(key, { entry, anchor, toggleButton });
        });

        let favorites = [];
        try {
            const serialized = window.localStorage.getItem(favoritesStorageKey);
            if (serialized) {
                const parsed = JSON.parse(serialized);
                if (Array.isArray(parsed)) {
                    favorites = parsed
                        .filter((key) => typeof key === "string" && entryByKey.has(key));
                }
            }
        } catch {
            favorites = [];
        }

        const persistFavorites = () => {
            try {
                window.localStorage.setItem(favoritesStorageKey, JSON.stringify(favorites));
            } catch {
                // Ignore storage failures in restricted environments.
            }
        };

        const notifyFavoritesChanged = () => {
            window.dispatchEvent(new CustomEvent(favoritesChangedEventName, {
                detail: {
                    scope,
                    favorites: [...favorites]
                }
            }));
        };

        const createFavoriteRow = (key, toggleFavorite) => {
            const source = entryByKey.get(key);
            if (!source) {
                return null;
            }

            const sourceClassList = source.anchor.classList;
            const linkClass = sourceClassList.contains("admin-nav-item")
                ? "admin-nav-item"
                : sourceClassList.contains("settings-link")
                    ? "settings-link"
                    : sourceClassList.contains("side-subitem")
                        ? "side-subitem"
                        : "side-item";

            const text = source.anchor.querySelector(".menu-text")?.textContent?.trim() ?? source.anchor.textContent?.trim() ?? key;
            const icon = source.anchor.querySelector(".menu-icon");

            const row = document.createElement("div");
            row.className = "menu-entry is-favorite quick-access-entry";
            row.dataset.menuEntry = "";
            row.dataset.searchExclude = "1";
            row.dataset.favoriteKey = key;

            const link = document.createElement("a");
            link.className = linkClass;
            link.href = source.anchor.href;
            link.title = text;

            const iconHolder = icon instanceof HTMLElement
                ? icon.cloneNode(true)
                : document.createElement("span");

            if (!(iconHolder instanceof HTMLElement)) {
                return null;
            }

            if (!iconHolder.classList.contains("menu-icon")) {
                iconHolder.classList.add("menu-icon");
            }

            const textSpan = document.createElement("span");
            textSpan.className = "menu-text";
            textSpan.textContent = text;

            link.append(iconHolder, textSpan);

            const button = document.createElement("button");
            button.type = "button";
            button.className = "favorite-toggle";
            button.setAttribute("data-favorite-toggle", "");
            button.setAttribute("aria-label", `${text} favorilerden cikar`);
            button.innerHTML = "<svg viewBox='0 0 24 24'><path d='m12 3 2.9 5.8 6.4.9-4.6 4.5 1.1 6.3L12 17.4 6.2 20.5l1.1-6.3L2.7 9.7l6.4-.9z'></path></svg>";
            button.addEventListener("click", (event) => {
                event.preventDefault();
                event.stopPropagation();
                toggleFavorite(key);
            });

            row.append(link, button);
            return row;
        };

        const syncEntryFavorites = () => {
            entries.forEach((entry) => {
                const key = entry.getAttribute("data-favorite-key");
                if (!key) {
                    return;
                }

                const isFavorite = favorites.includes(key);
                entry.classList.toggle("is-favorite", isFavorite);

                const toggleButton = entry.querySelector("[data-favorite-toggle]");
                if (toggleButton instanceof HTMLButtonElement) {
                    toggleButton.setAttribute("aria-pressed", isFavorite.toString());

                    const anchor = entry.querySelector("a[href]");
                    const text = anchor?.querySelector(".menu-text")?.textContent?.trim() ?? anchor?.textContent?.trim() ?? "Sayfa";
                    toggleButton.setAttribute(
                        "aria-label",
                        isFavorite ? `${text} favorilerden cikar` : `${text} favorilere ekle`);
                }
            });
        };

        const renderFavorites = (toggleFavorite) => {
            syncEntryFavorites();

            if (!(favoritesPanel instanceof HTMLElement) || !(favoritesList instanceof HTMLElement)) {
                return;
            }

            favoritesList.replaceChildren();

            favorites
                .map((key) => createFavoriteRow(key, toggleFavorite))
                .filter((row) => row instanceof HTMLElement)
                .forEach((row) => favoritesList.appendChild(row));

            favoritesPanel.hidden = favorites.length === 0;
        };

        const toggleFavorite = (key) => {
            const existingIndex = favorites.indexOf(key);
            if (existingIndex >= 0) {
                favorites.splice(existingIndex, 1);
            } else {
                favorites.unshift(key);
            }

            persistFavorites();
            renderFavorites(toggleFavorite);
            notifyFavoritesChanged();
        };

        entries.forEach((entry) => {
            const key = entry.getAttribute("data-favorite-key");
            const button = entry.querySelector("[data-favorite-toggle]");

            if (!key || !(button instanceof HTMLButtonElement)) {
                return;
            }

            button.addEventListener("click", (event) => {
                event.preventDefault();
                event.stopPropagation();
                toggleFavorite(key);
            });
        });

        renderFavorites(toggleFavorite);
        notifyFavoritesChanged();
    };

    const setupHomeFavorites = () => {
        const host = document.querySelector("[data-home-favorites]");
        if (!(host instanceof HTMLElement)) {
            return;
        }

        const emptyState = document.querySelector("[data-home-favorites-empty]");
        const scope = "main";
        const storageKey = `calibra.sidebar.favorites.${scope}`;

        const readFavorites = () => {
            try {
                const serialized = window.localStorage.getItem(storageKey);
                if (!serialized) {
                    return [];
                }

                const parsed = JSON.parse(serialized);
                return Array.isArray(parsed)
                    ? parsed.filter((key) => typeof key === "string")
                    : [];
            } catch {
                return [];
            }
        };

        const getEntryMap = () => {
            const map = new Map();
            const menuRoot = document.querySelector("[data-menu-root][data-menu-scope='main']");
            if (!(menuRoot instanceof HTMLElement)) {
                return map;
            }

            const entries = Array.from(menuRoot.querySelectorAll("[data-menu-entry][data-favorite-key]"));
            entries.forEach((entry) => {
                const key = entry.getAttribute("data-favorite-key");
                const anchor = entry.querySelector("a[href]");
                if (!key || !(anchor instanceof HTMLAnchorElement) || map.has(key)) {
                    return;
                }

                map.set(key, {
                    href: anchor.href,
                    label: anchor.querySelector(".menu-text")?.textContent?.trim() ?? anchor.textContent?.trim() ?? key,
                    icon: anchor.querySelector(".menu-icon")
                });
            });

            return map;
        };

        const render = () => {
            const entryMap = getEntryMap();
            const favorites = readFavorites().filter((key) => entryMap.has(key));

            host.replaceChildren();

            favorites.forEach((key) => {
                const item = entryMap.get(key);
                if (!item) {
                    return;
                }

                const link = document.createElement("a");
                link.className = "home-favorite-link";
                link.href = item.href;
                link.title = item.label;

                const icon = item.icon instanceof HTMLElement
                    ? item.icon.cloneNode(true)
                    : document.createElement("span");

                if (icon instanceof HTMLElement) {
                    if (!icon.classList.contains("menu-icon")) {
                        icon.classList.add("menu-icon");
                    }
                    link.appendChild(icon);
                }

                const text = document.createElement("span");
                text.textContent = item.label;
                link.appendChild(text);

                host.appendChild(link);
            });

            if (emptyState instanceof HTMLElement) {
                emptyState.hidden = favorites.length > 0;
            }
        };

        window.addEventListener(favoritesChangedEventName, (event) => {
            const detail = event.detail;
            if (!detail || detail.scope !== scope) {
                return;
            }
            render();
        });

        window.addEventListener("storage", (event) => {
            if (event.key !== storageKey) {
                return;
            }
            render();
        });

        render();
    };

    const setupWorkspaceTabs = () => {
        const host = document.querySelector("[data-page-tabs]");
        const tabList = host?.querySelector("[data-page-tabs-list]");
        const panelStack = document.querySelector("[data-workspace-panel-stack]");
        const nativePanel = panelStack?.querySelector("[data-workspace-native-page]");
        const setWorkspaceTabsHeight = () => {
            const nextHeight = host instanceof HTMLElement && !host.hidden
                ? Math.ceil(host.getBoundingClientRect().height)
                : 0;

            document.documentElement.style.setProperty("--workspace-tabs-sticky-height", `${nextHeight}px`);
        };

        if (!(host instanceof HTMLElement) || !(tabList instanceof HTMLElement) || !(panelStack instanceof HTMLElement)) {
            return;
        }

        if (root.getAttribute("data-page-tabs-enabled") !== "1") {
            host.hidden = true;
            setWorkspaceTabsHeight();
            return;
        }

        const userKeyRaw = root.getAttribute("data-user-key")?.trim() ?? "anonymous";
        const userKey = encodeURIComponent((userKeyRaw || "anonymous").toLocaleLowerCase("tr-TR"));
        const storageKey = `${workspaceTabsStoragePrefix}${userKey}`;
        const maxTabs = 24;

        const normalizeUrl = (value) => {
            const raw = typeof value === "string" ? value.trim() : "";
            if (!raw) {
                return "";
            }

            try {
                const parsed = new URL(raw, window.location.origin);
                let path = parsed.pathname || "/";
                if (path.length > 1 && path.endsWith("/")) {
                    path = path.slice(0, -1);
                }

                return `${path}${parsed.search}`;
            } catch {
                return raw;
            }
        };

        const resolveWorkspaceKey = (value) => {
            const normalizedUrl = normalizeUrl(value);
            if (!normalizedUrl) {
                return "";
            }

            // URL üzerindeki parametreleri silmiyoruz! Böylece farklı Fatura ID'leri veya Instance ID'leri farklı sekmeler açar.
            return normalizedUrl;
        };

        const normalizeTitle = (value) => {
            const raw = typeof value === "string" ? value.trim() : "";
            if (!raw) {
                return "";
            }

            return raw.replace(/\s*-\s*CalibraHub\s*$/i, "").trim();
        };

        const homeUrl = normalizeUrl(root.getAttribute("data-home-url") ?? "/") || "/";
        const homeKey = resolveWorkspaceKey(homeUrl) || "/";
        const currentUrl = normalizeUrl(root.getAttribute("data-page-url") ?? window.location.href) || homeUrl;
        const currentKey = resolveWorkspaceKey(currentUrl) || homeKey;
        const currentPageTitle = normalizeTitle(root.getAttribute("data-page-title") ?? document.title) || "Sayfa";

        const readTabs = () => {
            try {
                const serialized = window.localStorage.getItem(storageKey);
                if (!serialized) {
                    return [];
                }

                const parsed = JSON.parse(serialized);
                if (!Array.isArray(parsed)) {
                    return [];
                }

                const seen = new Set();
                return parsed.reduce((result, tab) => {
                    const url = normalizeUrl(tab?.url);
                    const key = resolveWorkspaceKey(tab?.key ?? url);
                    const title = normalizeTitle(tab?.title) || "Sayfa";

                    if (!key || !url || seen.has(key) || key === homeKey) {
                        return result;
                    }

                    seen.add(key);
                    result.push({ key, url, title });
                    return result;
                }, []);
            } catch {
                return [];
            }
        };

        const writeTabs = (tabs) => {
            try {
                window.localStorage.setItem(storageKey, JSON.stringify(tabs));
            } catch {
                // Ignore storage failures in restricted environments.
            }
        };

        const resolveTitleFontSize = (title) => {
            const length = typeof title === "string" ? title.trim().length : 0;

            if (length >= 34) {
                return "0.70rem";
            }

            if (length >= 28) {
                return "0.74rem";
            }

            if (length >= 22) {
                return "0.79rem";
            }

            if (length >= 18) {
                return "0.84rem";
            }

            return "0.89rem";
        };

        let tabs = readTabs();
        const menuLinks = Array.from(document.querySelectorAll("[data-menu-root] a[href], [data-home-favorites] a[href]"))
            .filter((link) => link instanceof HTMLAnchorElement);
        const currentTab = {
            key: currentKey,
            url: currentUrl,
            title: currentPageTitle
        };

        if (nativePanel instanceof HTMLElement) {
            nativePanel.dataset.workspaceKey = currentTab.key;
            nativePanel.dataset.workspaceUrl = currentTab.url;
            nativePanel.dataset.workspaceTitle = currentTab.title;
        }

        const upsertTab = (tab) => {
            const normalizedTab = {
                key: resolveWorkspaceKey(tab?.key ?? tab?.url),
                url: normalizeUrl(tab?.url ?? tab?.key),
                title: normalizeTitle(tab?.title) || "Sayfa"
            };

            if (!normalizedTab.key || !normalizedTab.url) {
                return null;
            }

            const existingIndex = tabs.findIndex((entry) => entry.key === normalizedTab.key);
            if (existingIndex >= 0) {
                tabs[existingIndex] = normalizedTab;
            } else {
                tabs.push(normalizedTab);
                if (tabs.length > maxTabs) {
                    tabs = tabs.slice(tabs.length - maxTabs);
                }
            }

            writeTabs(tabs);
            return normalizedTab;
        };

        if (currentKey && currentKey !== homeKey) {
            upsertTab(currentTab);
        } else {
            writeTabs(tabs);
        }

        let activeKey = tabs.find((tab) => tab.key === currentKey)?.key
            ?? tabs[0]?.key
            ?? "";

        const shouldTrackNavigation = (event, link) => {
            if (!(link instanceof HTMLAnchorElement) || event.defaultPrevented) {
                return false;
            }

            // Sadece CTRL tuşunu dinlemek ve özel Multi-Instance (Aynı sekmeyi kopyalama) için geçiş veriyoruz!
            if (event.button !== 0 || event.metaKey || event.shiftKey || event.altKey) {
                return false;
            }

            if (link.target && link.target !== "_self") {
                return false;
            }

            if (link.hasAttribute("download")) {
                return false;
            }

            try {
                const destination = new URL(link.href, window.location.origin);
                return destination.origin === window.location.origin;
            } catch {
                return false;
            }
        };

        const openAncestorMenus = (link) => {
            let parentDetails = link.closest("details");
            while (parentDetails instanceof HTMLDetailsElement) {
                parentDetails.open = true;
                parentDetails = parentDetails.parentElement?.closest("details") ?? null;
            }
        };

        const syncMenuState = (url) => {
            const workspaceKey = resolveWorkspaceKey(url);
            menuLinks.forEach((link) => {
                if (!(link instanceof HTMLAnchorElement)) {
                    return;
                }

                const isActive = resolveWorkspaceKey(link.href) === workspaceKey;
                link.classList.toggle("active", isActive);

                if (isActive) {
                    openAncestorMenus(link);
                }
            });
        };

        const buildFrameUrl = (url) => {
            try {
                const parsed = new URL(url, window.location.origin);
                parsed.searchParams.set("workspace", "1");
                return parsed.toString();
            } catch {
                return url;
            }
        };

        const findPanelByKey = (key) => Array.from(panelStack.querySelectorAll("[data-workspace-panel]"))
            .find((panel) => panel instanceof HTMLElement && panel.dataset.workspaceKey === key) ?? null;

        // Bir workspace iframe'inin OTORITER basligini okur.
        // Tek dogru kaynak: yuklenen sayfanin `body[data-page-title]` (ViewData["Title"]).
        // Boylece sekme basligi, linkin metninden degil sayfanin kendi basligindan gelir.
        const readFrameTitle = (frame) => {
            if (!(frame instanceof HTMLIFrameElement)) {
                return "";
            }

            try {
                const doc = frame.contentDocument;
                if (!doc) {
                    return "";
                }

                const bodyTitle = doc.body?.getAttribute("data-page-title");
                return normalizeTitle(bodyTitle || doc.title) || "";
            } catch {
                // Farkli origin / erisilemez durum — sessizce gec.
                return "";
            }
        };

        // Sekme basligini sayfanin gercek basligiyla uzlastirir (idempotent).
        // Yanlis/teknik bir placeholder ( or. action adi) yuklenmeden once gosterilmis olsa
        // bile, sayfa yuklenince burada dogru baslikla degistirilir ve kalici hale gelir.
        const reconcileTabTitle = (key, rawTitle) => {
            const title = normalizeTitle(rawTitle);
            if (!key || !title) {
                return;
            }

            const index = tabs.findIndex((entry) => entry.key === key);
            if (index < 0 || tabs[index].title === title) {
                return;
            }

            tabs[index] = { ...tabs[index], title };
            writeTabs(tabs);

            const panel = findPanelByKey(key);
            if (panel instanceof HTMLElement) {
                panel.dataset.workspaceTitle = title;
                const frame = panel.querySelector("iframe");
                if (frame instanceof HTMLIFrameElement) {
                    frame.title = title;
                }
            }

            if (key === activeKey) {
                root.setAttribute("data-page-title", title);
                document.title = `${title} - CalibraHub`;
            }

            render();
        };

        const createFramePanel = (tab) => {
            const panel = document.createElement("section");
            panel.className = "workspace-panel";
            panel.dataset.workspacePanel = "";
            panel.dataset.workspaceKey = tab.key;
            panel.dataset.workspaceUrl = tab.url;
            panel.dataset.workspaceTitle = tab.title;
            panel.hidden = true;
            panel.setAttribute("aria-hidden", "true");

            const frame = document.createElement("iframe");
            frame.className = "workspace-panel__frame";
            frame.src = buildFrameUrl(tab.url);
            frame.title = tab.title;
            frame.setAttribute("loading", "eager");
            frame.setAttribute("data-workspace-frame", "");
            frame.addEventListener("load", () => {
                // Sayfa yuklendi: sekme basligini sayfanin gercek basligiyla uzlastir.
                reconcileTabTitle(tab.key, readFrameTitle(frame));
                if (panel.classList.contains("is-active")) {
                    requestWorkspaceToolbarSync();
                }
            });

            panel.appendChild(frame);
            panelStack.appendChild(panel);
            return panel;
        };

        const getOrCreatePanel = (tab) => {
            const existing = findPanelByKey(tab.key);
            if (existing instanceof HTMLElement) {
                existing.dataset.workspaceKey = tab.key;
                existing.dataset.workspaceTitle = tab.title;
                existing.dataset.workspaceUrl = tab.url;
                return existing;
            }

            return createFramePanel(tab);
        };

        const updatePageIdentity = (tab) => {
            root.setAttribute("data-page-url", tab.url);
            root.setAttribute("data-page-title", tab.title);
            document.title = `${tab.title} - CalibraHub`;
        };

        const activatePanel = (key) => {
            const workspaceKey = resolveWorkspaceKey(key);
            Array.from(panelStack.querySelectorAll("[data-workspace-panel]")).forEach((panel) => {
                if (!(panel instanceof HTMLElement)) {
                    return;
                }

                const isActive = panel.dataset.workspaceKey === workspaceKey;
                panel.hidden = !isActive;
                panel.classList.toggle("is-active", isActive);
                panel.setAttribute("aria-hidden", (!isActive).toString());
            });
        };

        const setActiveTab = (value, options = {}) => {
            const workspaceKey = resolveWorkspaceKey(value);
            const tab = tabs.find((entry) => entry.key === workspaceKey);
            if (!tab) {
                return;
            }

            activeKey = tab.key;
            getOrCreatePanel(tab);
            activatePanel(tab.key);
            syncMenuState(tab.url);
            updatePageIdentity(tab);
            render();
            requestWorkspaceToolbarSync();

            if (options.history === "skip") {
                return;
            }

            const state = { workspaceKey: tab.key, workspaceUrl: tab.url };
            const currentHistoryUrl = normalizeUrl(window.location.href);
            if (options.history === "replace" || currentHistoryUrl === tab.url) {
                window.history.replaceState(state, "", tab.url);
                return;
            }

            window.history.pushState(state, "", tab.url);
        };

        const render = () => {
            tabs = readTabs();
            if (tabs.length === 0) {
                if (currentKey && currentKey !== homeKey) {
                    tabs = [currentTab];
                    writeTabs(tabs);
                } else {
                    activeKey = "";
                }
            }

            if (activeKey && !tabs.some((tab) => tab.key === activeKey)) {
                activeKey = tabs[0]?.key ?? "";
            }

            tabList.replaceChildren();

            tabs.forEach((tab, index) => {
                const item = document.createElement("div");
                const isActive = tab.key === activeKey;
                item.className = `workspace-tab${isActive ? " is-active" : ""}`;
                item.setAttribute("role", "presentation");

                const link = document.createElement("a");
                link.className = "workspace-tab__link";
                link.href = tab.url;
                link.textContent = tab.title;
                link.title = tab.title;
                link.setAttribute("role", "tab");
                link.setAttribute("aria-selected", isActive.toString());
                link.style.setProperty("--workspace-tab-title-size", resolveTitleFontSize(tab.title));
                link.addEventListener("click", (event) => {
                    event.preventDefault();
                    setActiveTab(tab.key);
                });

                const closeButton = document.createElement("button");
                closeButton.type = "button";
                closeButton.className = "workspace-tab__close";
                closeButton.setAttribute("aria-label", `${tab.title} sekmesini kapat`);
                closeButton.innerHTML = "<svg viewBox='0 0 24 24' aria-hidden='true'><path d='M6 6 18 18M18 6 6 18'></path></svg>";
                closeButton.addEventListener("click", (event) => {
                    event.preventDefault();
                    event.stopPropagation();

                    const nextTabs = tabs.filter((entry) => entry.key !== tab.key);
                    tabs = nextTabs;
                    writeTabs(tabs);

                    const panel = findPanelByKey(tab.key);
                    if (panel instanceof HTMLElement && !panel.hasAttribute("data-workspace-native-page")) {
                        panel.remove();
                    }

                    if (nextTabs.length === 0) {
                        window.location.replace(homeUrl);
                        return;
                    }

                    if (!isActive) {
                        render();
                        return;
                    }

                    const fallbackTab = nextTabs[Math.max(0, index - 1)]
                        ?? nextTabs[0]
                        ?? { key: homeKey, url: homeUrl };

                    setActiveTab(fallbackTab.key, { history: "replace" });
                });

                item.append(link, closeButton);
                tabList.appendChild(item);
            });

            host.hidden = tabs.length === 0;
            root.classList.toggle("workspace-tabs-visible", tabs.length > 0);
            window.requestAnimationFrame(setWorkspaceTabsHeight);

            // ---- AÇIK PENCERELER MODAL SENKRONİZASYONU ----
            const opsPanel = document.querySelector("[data-open-windows-panel]");
            if (opsPanel instanceof HTMLElement) {
                opsPanel.hidden = tabs.length === 0;
            }

            const opsBadge = document.getElementById("openWindowsBadge");
            if (opsBadge) {
                opsBadge.style.display = tabs.length > 0 ? "inline-block" : "none";
                opsBadge.textContent = tabs.length;
            }

            // Modal güncellemeleri
            const modalList = document.getElementById("openWindowsModalList");
            if (modalList) {
                modalList.replaceChildren();
                
                if (tabs.length === 0) {
                     modalList.innerHTML = '<div class="text-center p-3 text-muted" style="font-size: 0.9rem;">Hiç açık sayfa bulunmuyor.</div>';
                }

                tabs.forEach((tab) => {
                    const entry = document.createElement("div");
                    entry.className = "d-flex justify-content-between align-items-center p-2 rounded border border-light";
                    entry.style.backgroundColor = tab.key === activeKey ? "var(--bs-primary-bg-subtle, rgba(13,110,253,0.1))" : "var(--bs-tertiary-bg, #f8f9fa)";

                    const btnLink = document.createElement("button");
                    btnLink.type = "button";
                    btnLink.className = "btn btn-link text-decoration-none text-start p-0 d-flex align-items-center flex-grow-1 text-truncate";
                    btnLink.style.color = "var(--bs-body-color)";
                    
                    let shortTitle = tab.title.trim() || "Sayfa";
                    
                    btnLink.innerHTML = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="me-2" width="16" height="16" style="color:var(--bs-secondary);"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><polyline points="14 2 14 8 20 8"></polyline></svg><span class="text-truncate fw-medium" style="font-size: 0.85rem; max-width: 320px;" title="${tab.title}">${shortTitle}</span>`;
                    
                    btnLink.addEventListener("click", () => {
                        const modal = bootstrap.Modal.getInstance(document.getElementById('openWindowsModal'));
                        if (modal) modal.hide();
                        setActiveTab(tab.key);
                    });

                    // En sagda belirgin kapatma (X) butonu — tek aksiyon olarak.
                    // Satir basligina tiklama zaten o tab'a gecisi yapiyor (btnLink.click).
                    // Explicit kirmizi stroke + background'la her temada gorunur:
                    const btnClose = document.createElement("button");
                    btnClose.type = "button";
                    btnClose.className = "d-inline-flex align-items-center justify-content-center";
                    btnClose.style.cssText =
                        "width: 32px; height: 32px; padding: 0; border-radius: 6px; flex-shrink: 0; " +
                        "background: rgba(239, 68, 68, 0.15); " +
                        "border: 1px solid rgba(239, 68, 68, 0.35); " +
                        "color: #ef4444; cursor: pointer; " +
                        "transition: background 0.12s, border-color 0.12s;";
                    btnClose.title = "Sayfayi kapat";
                    btnClose.setAttribute("aria-label", "Sayfayi kapat");
                    btnClose.innerHTML =
                        "<svg viewBox='0 0 24 24' width='16' height='16' " +
                             "fill='none' stroke='#ef4444' stroke-width='3' stroke-linecap='round' stroke-linejoin='round' " +
                             "style='display:block;'>" +
                            "<line x1='18' y1='6' x2='6' y2='18'></line>" +
                            "<line x1='6' y1='6' x2='18' y2='18'></line>" +
                        "</svg>";
                    btnClose.addEventListener("mouseenter", () => {
                        btnClose.style.background   = "rgba(239, 68, 68, 0.28)";
                        btnClose.style.borderColor  = "rgba(239, 68, 68, 0.55)";
                    });
                    btnClose.addEventListener("mouseleave", () => {
                        btnClose.style.background   = "rgba(239, 68, 68, 0.15)";
                        btnClose.style.borderColor  = "rgba(239, 68, 68, 0.35)";
                    });
                    btnClose.addEventListener("click", (ev) => {
                        ev.stopPropagation();
                        const idx = tabs.findIndex(t => t.key === tab.key);
                        if (idx === -1) return;
                        const nextTabs = tabs.filter(t => t.key !== tab.key);
                        tabs = nextTabs;
                        writeTabs(tabs);
                        const panel = findPanelByKey(tab.key);
                        if (panel instanceof HTMLElement && !panel.hasAttribute("data-workspace-native-page")) {
                            panel.remove();
                        }
                        // Modal'daki satiri hemen kaldir (render'dan hizli feedback)
                        entry.remove();
                        if (nextTabs.length === 0) {
                            // Tum sayfalar kapali — modali kapat ve anasayfaya git
                            const modalInst = bootstrap.Modal.getInstance(document.getElementById('openWindowsModal'));
                            if (modalInst) modalInst.hide();
                            window.location.replace(homeUrl);
                            return;
                        }
                        const wasActive = tab.key === activeKey;
                        if (!wasActive) {
                            render();
                            return;
                        }
                        const fallback = nextTabs[Math.max(0, idx - 1)] ?? nextTabs[0] ?? { key: homeKey, url: homeUrl };
                        setActiveTab(fallback.key, { history: "replace" });
                    });

                    entry.append(btnLink, btnClose);
                    modalList.appendChild(entry);
                });
            }

            const activeTab = tabList.querySelector(".workspace-tab.is-active");
            const scroller = tabList.closest(".workspace-tabs__scroller");
            if (activeTab instanceof HTMLElement && scroller instanceof HTMLElement) {
                const scrollerRect = scroller.getBoundingClientRect();
                const tabRect = activeTab.getBoundingClientRect();
                const overflowLeft = tabRect.left < scrollerRect.left;
                const overflowRight = tabRect.right > scrollerRect.right;

                if (overflowLeft) {
                    scroller.scrollLeft += Math.round(tabRect.left - scrollerRect.left);
                } else if (overflowRight) {
                    scroller.scrollLeft += Math.round(tabRect.right - scrollerRect.right);
                }
            }
        };

        document.addEventListener("click", (event) => {
            const target = event.target;
            if (!(target instanceof Element)) {
                return;
            }

            const link = target.closest("[data-menu-root] a[href], [data-home-favorites] a[href], [data-workspace-link]");
            if (!(link instanceof HTMLAnchorElement) || !shouldTrackNavigation(event, link)) {
                return;
            }

            let nextUrl = normalizeUrl(link.href);
            if (!nextUrl) {
                return;
            }

            event.preventDefault();

            // Eğer sol menüden CTRL+Tıklama yapılırsa sayfanın sonuna Rastgele (Instance) ID ekler, aynı sayfa yep yeni bir SPA sekmesi olarak ÇOĞALTILIR!
            if (event.ctrlKey) {
                nextUrl += (nextUrl.includes("?") ? "&" : "?") + "instance=" + Date.now();
            }

            const nextTitle = normalizeTitle(
                link.querySelector(".menu-text")?.textContent ??
                link.getAttribute("title") ??
                link.textContent ??
                "") || currentPageTitle;

            const nextTab = upsertTab({
                key: resolveWorkspaceKey(nextUrl),
                url: nextUrl,
                title: nextTitle
            });

            if (!nextTab) {
                return;
            }

            setActiveTab(nextTab.key);
        });

        window.addEventListener("storage", (event) => {
            if (event.key !== storageKey) {
                return;
            }

            render();
        });

        window.addEventListener("popstate", () => {
            const nextUrl = normalizeUrl(window.location.href) || homeUrl;
            const nextKey = resolveWorkspaceKey(nextUrl) || homeKey;

            if (tabs.some((tab) => tab.key === nextKey)) {
                upsertTab({
                    key: nextKey,
                    url: nextUrl,
                    title: normalizeTitle(document.title) || currentPageTitle
                });
                setActiveTab(nextKey, { history: "skip" });
                return;
            }

            window.location.assign(nextUrl);
        });

        window.addEventListener("resize", setWorkspaceTabsHeight);

        if (typeof ResizeObserver !== "undefined") {
            const resizeObserver = new ResizeObserver(() => {
                setWorkspaceTabsHeight();
            });

            resizeObserver.observe(host);
            resizeObserver.observe(tabList);
        }

        // Public API — consumed by setupOpenWindowsManager (outside this closure)
        _workspaceTabsApi = {
            closeAll() {
                writeTabs([]);
                tabs = [];
                render();
                window.location.replace(homeUrl);
            },
            refreshModalList() {
                render();
            },
            openTab(url, title) {
                const tab = upsertTab({ url, title: title || "Sayfa" });
                if (tab) setActiveTab(tab.key);
            }
        };

        // iframe'lerden çağrılabilen cross-frame API
        window.calibraOpenWorkspaceTab = (url, title) => _workspaceTabsApi?.openTab?.(url, title);

        render();
        if (activeKey) {
            setActiveTab(activeKey, { history: "replace" });
        }

        // Guvence: setActiveTab cagrisindan ONCE yuklenmis (cache'li) iframe'ler load
        // olayini kacirmis olabilir — acilista bir kez mevcut frame'leri uzlastir.
        panelStack.querySelectorAll("[data-workspace-panel] iframe").forEach((frame) => {
            if (!(frame instanceof HTMLIFrameElement)) {
                return;
            }

            const panel = frame.closest("[data-workspace-panel]");
            const key = panel instanceof HTMLElement ? panel.dataset.workspaceKey : "";
            const title = readFrameTitle(frame);
            if (key && title) {
                reconcileTabTitle(key, title);
            }
        });
    };

    const setupFormDrafts = () => {
        const forms = Array.from(document.querySelectorAll("form.integrator-form"))
            .filter((form) => form instanceof HTMLFormElement)
            .filter((form) => !form.classList.contains("integrator-inline-form"));

        if (forms.length === 0) {
            return;
        }

        const userKeyRaw = root.getAttribute("data-user-key")?.trim() ?? "anonymous";
        const userKey = encodeURIComponent((userKeyRaw || "anonymous").toLocaleLowerCase("tr-TR"));

        const normalizeUrl = (value) => {
            const raw = typeof value === "string" ? value.trim() : "";
            if (!raw) {
                return "";
            }

            try {
                const parsed = new URL(raw, window.location.origin);
                let path = `${parsed.pathname}`.replace(/\/+$/, "");
                if (!path) {
                    path = "/";
                }

                return `${path}${parsed.search}`;
            } catch {
                return raw;
            }
        };

        const pageUrl = normalizeUrl(root.getAttribute("data-page-url") ?? window.location.href) || "/";

        const readDraft = (storageKey) => {
            try {
                const serialized = window.sessionStorage.getItem(storageKey);
                if (!serialized) {
                    return null;
                }

                const parsed = JSON.parse(serialized);
                return parsed && typeof parsed === "object" ? parsed : null;
            } catch {
                return null;
            }
        };

        const writeDraft = (storageKey, payload) => {
            try {
                window.sessionStorage.setItem(storageKey, JSON.stringify(payload));
            } catch {
                // Ignore storage failures in restricted environments.
            }
        };

        const clearDraft = (storageKey) => {
            try {
                window.sessionStorage.removeItem(storageKey);
            } catch {
                // Ignore storage failures in restricted environments.
            }
        };

        const trackedDrafts = [];

        const ensureFormId = (form, index) => {
            const existingId = form.getAttribute("id")?.trim();
            if (existingId) {
                return existingId;
            }

            const baseId = `calibra-form-draft-${index + 1}`;
            let candidateId = baseId;
            let suffix = 1;

            while (document.getElementById(candidateId)) {
                candidateId = `${baseId}-${suffix}`;
                suffix += 1;
            }

            form.id = candidateId;
            return candidateId;
        };

        const isTrackableControl = (element) => {
            if (!(element instanceof HTMLInputElement) &&
                !(element instanceof HTMLSelectElement) &&
                !(element instanceof HTMLTextAreaElement)) {
                return false;
            }

            if (element.disabled || !element.name) {
                return false;
            }

            if (element instanceof HTMLInputElement) {
                const type = (element.type ?? "").trim().toLowerCase();
                if (type === "hidden" ||
                    type === "submit" ||
                    type === "reset" ||
                    type === "button" ||
                    type === "image" ||
                    type === "file" ||
                    type === "password") {
                    return false;
                }
            }

            return true;
        };

        const serializeForm = (form) => {
            const snapshot = {};

            Array.from(form.elements).forEach((element) => {
                if (!isTrackableControl(element)) {
                    return;
                }

                if (element instanceof HTMLInputElement) {
                    const type = (element.type ?? "").trim().toLowerCase();

                    if (type === "checkbox") {
                        snapshot[element.name] = element.checked;
                        return;
                    }

                    if (type === "radio") {
                        if (element.checked) {
                            snapshot[element.name] = element.value;
                        } else if (!(element.name in snapshot)) {
                            snapshot[element.name] = null;
                        }
                        return;
                    }

                    snapshot[element.name] = element.value;
                    return;
                }

                if (element instanceof HTMLSelectElement && element.multiple) {
                    snapshot[element.name] = Array.from(element.selectedOptions).map((option) => option.value);
                    return;
                }

                snapshot[element.name] = element.value;
            });

            return snapshot;
        };

        const applySnapshot = (form, snapshot) => {
            if (!snapshot || typeof snapshot !== "object") {
                return;
            }

            Array.from(form.elements).forEach((element) => {
                if (!isTrackableControl(element) || !(element.name in snapshot)) {
                    return;
                }

                const nextValue = snapshot[element.name];

                if (element instanceof HTMLInputElement) {
                    const type = (element.type ?? "").trim().toLowerCase();

                    if (type === "checkbox") {
                        element.checked = nextValue === true;
                        element.dispatchEvent(new Event("change", { bubbles: true }));
                        return;
                    }

                    if (type === "radio") {
                        element.checked = nextValue !== null && String(nextValue) === element.value;
                        return;
                    }

                    element.value = nextValue ?? "";
                    element.dispatchEvent(new Event("input", { bubbles: true }));
                    return;
                }

                if (element instanceof HTMLSelectElement && element.multiple) {
                    const values = Array.isArray(nextValue)
                        ? nextValue.map((value) => String(value))
                        : [];

                    Array.from(element.options).forEach((option) => {
                        option.selected = values.includes(option.value);
                    });
                    element.dispatchEvent(new Event("change", { bubbles: true }));
                    return;
                }

                element.value = nextValue ?? "";
                element.dispatchEvent(new Event("change", { bubbles: true }));
            });
        };

        const clearBoundDrafts = (event) => {
            const trigger = event.target.closest("a[href], button, input");
            if (!(trigger instanceof HTMLElement)) {
                return;
            }

            const isResetButton = trigger instanceof HTMLButtonElement && trigger.type === "reset";
            const isNewLink = trigger instanceof HTMLAnchorElement &&
                trigger.closest(".entity-form-toolbar") &&
                (trigger.textContent ?? "").toLocaleLowerCase("tr-TR").includes("yeni");

            const formId = trigger.getAttribute("form");
            let targetForm = formId ? document.getElementById(formId) : null;

            if (!(targetForm instanceof HTMLFormElement)) {
                targetForm = trigger.closest(".entity-form-card")?.querySelector("form.integrator-form") ?? null;
            }

            if (!(targetForm instanceof HTMLFormElement) || !targetForm.dataset.formDraftStorageKey) {
                return;
            }

            if (!isResetButton && !isNewLink && trigger.getAttribute("data-form-draft-clear") !== "1") {
                return;
            }

            clearDraft(targetForm.dataset.formDraftStorageKey);
        };

        document.addEventListener("click", clearBoundDrafts);

        const flushDrafts = () => {
            trackedDrafts.forEach((flushDraft) => {
                try {
                    flushDraft();
                } catch {
                    // Ignore flush failures during page transitions.
                }
            });
        };

        window.addEventListener("pagehide", flushDrafts);
        window.addEventListener("beforeunload", flushDrafts);

        forms.forEach((form, index) => {
            const formId = ensureFormId(form, index);
            const actionUrl = normalizeUrl(form.getAttribute("action") ?? pageUrl) || pageUrl;
            const storageKey = `${formDraftStoragePrefix}${userKey}.${encodeURIComponent(pageUrl)}.${encodeURIComponent(actionUrl)}.${encodeURIComponent(formId)}`;
            const initialSnapshot = JSON.stringify(serializeForm(form));
            let writeTimer = null;

            form.dataset.formDraftStorageKey = storageKey;

            const persistSnapshot = () => {
                const snapshot = serializeForm(form);
                const serialized = JSON.stringify(snapshot);

                if (serialized === initialSnapshot) {
                    clearDraft(storageKey);
                    return;
                }

                writeDraft(storageKey, {
                    pageUrl,
                    actionUrl,
                    formId,
                    snapshot,
                    updatedAt: new Date().toISOString()
                });
            };

            const queuePersist = () => {
                if (writeTimer !== null) {
                    window.clearTimeout(writeTimer);
                }

                writeTimer = window.setTimeout(() => {
                    writeTimer = null;
                    persistSnapshot();
                }, 180);
            };

            const restorePayload = readDraft(storageKey);
            if (restorePayload?.snapshot && typeof restorePayload.snapshot === "object") {
                applySnapshot(form, restorePayload.snapshot);
            }

            form.addEventListener("input", queuePersist);
            form.addEventListener("change", queuePersist);
            form.addEventListener("submit", () => {
                if (writeTimer !== null) {
                    window.clearTimeout(writeTimer);
                    writeTimer = null;
                }

                clearDraft(storageKey);
            });
            form.addEventListener("reset", () => {
                if (writeTimer !== null) {
                    window.clearTimeout(writeTimer);
                    writeTimer = null;
                }

                window.setTimeout(() => {
                    clearDraft(storageKey);
                }, 0);
            });

            trackedDrafts.push(() => {
                if (writeTimer !== null) {
                    window.clearTimeout(writeTimer);
                    writeTimer = null;
                }

                persistSnapshot();
            });
        });
    };

    const setupMenuSearch = (menuRoot) => {
        const searchInput = menuRoot.querySelector("[data-menu-search]");
        if (!(searchInput instanceof HTMLInputElement)) {
            return;
        }

        const itemSelector = "[data-menu-entry]";
        const groupSelector = "details.side-group, details.admin-group";
        const items = Array.from(menuRoot.querySelectorAll(itemSelector))
            .filter((item) => item.getAttribute("data-search-exclude") !== "1");
        const groups = Array.from(menuRoot.querySelectorAll(groupSelector));

        const getGroupDepth = (group) => {
            let depth = 0;
            let parent = group.parentElement;

            while (parent) {
                if (parent.matches(groupSelector)) {
                    depth += 1;
                }
                parent = parent.parentElement;
            }

            return depth;
        };

        const groupsByDepthDesc = [...groups]
            .sort((left, right) => getGroupDepth(right) - getGroupDepth(left));

        groups.forEach((group) => {
            group.dataset.defaultOpen = group.hasAttribute("open") ? "1" : "0";
        });

        const getEntryText = (entry) => {
            const anchor = entry.querySelector("a[href]");
            if (anchor instanceof HTMLAnchorElement) {
                return normalize(anchor.textContent ?? "");
            }

            return normalize(entry.textContent ?? "");
        };

        const applySearch = () => {
            const query = normalize(searchInput.value ?? "");

            if (!query) {
                items.forEach((item) => {
                    item.hidden = false;
                });

                groups.forEach((group) => {
                    group.hidden = false;
                    if (group.dataset.defaultOpen === "1") {
                        group.setAttribute("open", "");
                    } else {
                        group.removeAttribute("open");
                    }
                });

                return;
            }

            items.forEach((item) => {
                item.hidden = !getEntryText(item).includes(query);
            });

            groupsByDepthDesc.forEach((group) => {
                const summaryText = normalize(group.querySelector("summary")?.textContent ?? "");
                const summaryMatch = summaryText.includes(query);
                const childItems = Array.from(group.querySelectorAll(itemSelector))
                    .filter((item) => item.getAttribute("data-search-exclude") !== "1");

                if (summaryMatch) {
                    childItems.forEach((item) => {
                        item.hidden = false;
                    });
                }

                const hasVisibleChild = childItems.some((item) => !item.hidden);
                const groupVisible = summaryMatch || hasVisibleChild;

                group.hidden = !groupVisible;
                if (groupVisible) {
                    group.setAttribute("open", "");
                }
            });
        };

        const findFirstVisibleEntry = () => {
            for (const item of items) {
                if (item.hidden || item.closest("[hidden]")) {
                    continue;
                }

                const anchor = item.querySelector("a[href]");
                if (anchor instanceof HTMLAnchorElement) {
                    return anchor;
                }
            }

            return null;
        };

        searchInput.addEventListener("input", applySearch);
        searchInput.addEventListener("keydown", (event) => {
            if (event.key !== "Enter") {
                return;
            }

            const query = normalize(searchInput.value ?? "");
            if (!query) {
                return;
            }

            applySearch();
            const firstResult = findFirstVisibleEntry();
            if (!(firstResult instanceof HTMLAnchorElement)) {
                return;
            }

            event.preventDefault();
            window.location.assign(firstResult.href);
        });
    };

    const setupCollapsibleCards = () => {
        const cards = Array.from(document.querySelectorAll("[data-collapsible-card][data-collapsible-id]"));
        if (cards.length === 0) {
            return;
        }

        const pinIconMarkup = "<svg viewBox='0 0 24 24' aria-hidden='true'><path d='M9 3h6l1 5 3 3v2H5v-2l3-3z'></path><path d='M12 13v8'></path></svg>";

        const userKeyRaw = root.getAttribute("data-user-key")?.trim() ?? "anonymous";
        const userKey = encodeURIComponent((userKeyRaw || "anonymous").toLocaleLowerCase("tr-TR"));
        const pageKey = encodeURIComponent((window.location.pathname || "/").toLocaleLowerCase("tr-TR"));

        const readStorage = (key) => {
            try {
                return window.localStorage.getItem(key);
            } catch {
                return null;
            }
        };

        const writeStorage = (key, value) => {
            try {
                window.localStorage.setItem(key, value);
            } catch {
                // Ignore storage failures in restricted environments.
            }
        };

        const removeStorage = (key) => {
            try {
                window.localStorage.removeItem(key);
            } catch {
                // Ignore storage failures in restricted environments.
            }
        };

        cards.forEach((card, index) => {
            if (!(card instanceof HTMLElement)) {
                return;
            }

            const cardId = card.getAttribute("data-collapsible-id")?.trim() || `card-${index + 1}`;
            const toggleButton = card.querySelector("[data-collapsible-toggle]");
            const pinButton = card.querySelector("[data-collapsible-pin]");
            const content = card.querySelector("[data-collapsible-content]");

            if (!(toggleButton instanceof HTMLButtonElement) || !(content instanceof HTMLElement)) {
                return;
            }

            if (pinButton instanceof HTMLButtonElement) {
                pinButton.innerHTML = pinIconMarkup;
            }

            const storagePrefix = `calibra.collapsible.${userKey}.${pageKey}.${cardId}`;
            const pinnedKey = `${storagePrefix}.pinned`;
            const collapsedKey = `${storagePrefix}.collapsed`;

            let isPinned = readStorage(pinnedKey) === "1";
            let isCollapsed = false;
            const defaultState = (card.getAttribute("data-collapsible-default") ?? "").trim().toLowerCase();

            if (isPinned) {
                isCollapsed = readStorage(collapsedKey) === "1";
            } else if (defaultState === "collapsed") {
                isCollapsed = true;
            }

            const applyPinnedState = () => {
                card.classList.toggle("is-pinned", isPinned);

                if (pinButton instanceof HTMLButtonElement) {
                    pinButton.setAttribute("aria-pressed", isPinned.toString());
                    pinButton.setAttribute(
                        "title",
                        isPinned
                            ? "Sabitli: acik/kapali durumu bu kullanici icin kaydediliyor"
                            : "Sabitle: acik/kapali durumu bu kullanici icin kaydet");
                }
            };

            const applyCollapsedState = () => {
                card.classList.toggle("is-collapsed", isCollapsed);
                content.hidden = isCollapsed;
                toggleButton.setAttribute("aria-expanded", (!isCollapsed).toString());
                toggleButton.setAttribute("title", isCollapsed ? "Karti ac" : "Karti kapat");
            };

            toggleButton.addEventListener("click", () => {
                isCollapsed = !isCollapsed;
                applyCollapsedState();

                if (isPinned) {
                    writeStorage(collapsedKey, isCollapsed ? "1" : "0");
                }
            });

            if (pinButton instanceof HTMLButtonElement) {
                pinButton.addEventListener("click", () => {
                    isPinned = !isPinned;
                    applyPinnedState();

                    if (isPinned) {
                        writeStorage(pinnedKey, "1");
                        writeStorage(collapsedKey, isCollapsed ? "1" : "0");
                    } else {
                        removeStorage(pinnedKey);
                        removeStorage(collapsedKey);
                    }
                });
            }

            applyPinnedState();
            applyCollapsedState();
        });
    };

    const setupEntityFormToolbars = () => {
        const toolbarSelector = ".entity-form-toolbar, .integrator-form-actions, .integrator-settings-modern__action-bar";
        const toolbars = Array.from(document.querySelectorAll(toolbarSelector))
            .filter((toolbar) => toolbar instanceof HTMLElement)
            .filter((toolbar) => !toolbar.closest(".integrator-inline-form"))
            .filter((toolbar) => !toolbar.closest("[data-workspace-action-bar]"));
        const isEmbeddedWorkspaceFrame = root.classList.contains("workspace-frame-body") && window.parent !== window;
        const hasGlobalWorkspaceActionBar = window.parent === window
            && document.querySelector("[data-workspace-action-bar]") instanceof HTMLElement
            && document.querySelector("[data-workspace-panel-stack]") instanceof HTMLElement;
        const shouldProxyToolbar = isEmbeddedWorkspaceFrame || hasGlobalWorkspaceActionBar;

        const ensureFormId = (form, index) => {
            const existingId = form.getAttribute("id")?.trim();
            if (existingId) {
                return existingId;
            }

            const baseId = `calibra-entity-form-${index + 1}`;
            let candidateId = baseId;
            let suffix = 1;

            while (document.getElementById(candidateId)) {
                candidateId = `${baseId}-${suffix}`;
                suffix += 1;
            }

            form.id = candidateId;
            return candidateId;
        };

        const shouldBindControlToForm = (control) => {
            if (control instanceof HTMLButtonElement) {
                const type = (control.getAttribute("type") ?? "").trim().toLowerCase();
                return type === "" || type === "submit" || type === "reset";
            }

            if (control instanceof HTMLInputElement) {
                const type = (control.type ?? "").trim().toLowerCase();
                return type === "submit" || type === "reset" || type === "image";
            }

            return false;
        };

        const normalizeText = (value) => typeof value === "string"
            ? value.replace(/\s+/g, " ").trim()
            : "";

        const getDirectChild = (element, predicate) => Array.from(element.children)
            .find((child) => child instanceof HTMLElement && predicate(child)) ?? null;

        const getCanonicalToolbarHost = (card) => {
            const collapsibleContent = getDirectChild(card, (child) =>
                child.classList.contains("integrator-collapsible-card__content"));
            if (collapsibleContent instanceof HTMLElement) {
                return collapsibleContent;
            }

            return card;
        };

        const placeToolbarAtCanonicalPosition = (toolbar, card) => {
            if (!(toolbar instanceof HTMLElement) || !(card instanceof HTMLElement)) {
                return;
            }

            const host = getCanonicalToolbarHost(card);
            if (!(host instanceof HTMLElement)) {
                return;
            }

            const firstEligibleChild = Array.from(host.children)
                .find((child) => child instanceof HTMLElement && child !== toolbar) ?? null;

            if (host !== card) {
                if (toolbar.parentElement !== host || host.firstElementChild !== toolbar) {
                    host.insertBefore(toolbar, firstEligibleChild);
                }

                return;
            }

            const header = getDirectChild(card, (child) =>
                child.classList.contains("integrator-card__header")
                || child.classList.contains("integrator-collapsible-card__header"));

            const referenceNode = header instanceof HTMLElement
                ? header.nextElementSibling
                : firstEligibleChild;

            if (toolbar.parentElement !== card || referenceNode !== toolbar) {
                card.insertBefore(toolbar, referenceNode);
            }
        };

        const getToolbarControls = (toolbar) => Array.from(toolbar.children)
            .filter((child) =>
                child instanceof HTMLButtonElement ||
                child instanceof HTMLAnchorElement ||
                child instanceof HTMLInputElement);

        const isControlHidden = (control) => {
            if (!(control instanceof HTMLElement)) {
                return true;
            }

            if (control.hidden) {
                return true;
            }

            const styles = window.getComputedStyle(control);
            return styles.display === "none" || styles.visibility === "hidden";
        };

        const ensureToolbarId = (toolbar, index) => {
            const existingId = toolbar.getAttribute("data-workspace-toolbar-id")?.trim();
            if (existingId) {
                return existingId;
            }

            const toolbarId = `calibra-toolbar-${index + 1}`;
            toolbar.setAttribute("data-workspace-toolbar-id", toolbarId);
            return toolbarId;
        };

        const createActionId = (toolbarId, index) => `${toolbarId}-action-${index + 1}`;

        let activeToolbar = null;
        const requestHostSync = () => {
            requestWorkspaceToolbarSync();

            if (isEmbeddedWorkspaceFrame) {
                try {
                    window.parent?.calibraRequestWorkspaceToolbarSync?.();
                } catch {
                    // Ignore cross-window sync failures.
                }
            }
        };

        const getActiveToolbar = () =>
            activeToolbar instanceof HTMLElement && toolbars.includes(activeToolbar)
                ? activeToolbar
                : toolbars[0] ?? null;

        const updateActiveToolbar = (toolbar) => {
            if (!(toolbar instanceof HTMLElement) || !toolbars.includes(toolbar) || activeToolbar === toolbar) {
                return;
            }

            activeToolbar = toolbar;
            requestHostSync();
        };

        const invokeToolbarControl = (control) => {
            if (control instanceof HTMLAnchorElement) {
                const href = control.href?.trim();
                if (!href) {
                    return false;
                }

                window.location.assign(href);
                return true;
            }

            if (!(control instanceof HTMLButtonElement) && !(control instanceof HTMLInputElement)) {
                return false;
            }

            if (control.disabled) {
                return false;
            }

            const type = control instanceof HTMLButtonElement
                ? (control.getAttribute("type") ?? "submit").trim().toLowerCase()
                : (control.type ?? "text").trim().toLowerCase();

            const associatedForm = control.form
                ?? (() => {
                    const formId = control.getAttribute("form")?.trim();
                    return formId ? document.getElementById(formId) : null;
                })();

            if (type === "reset" && associatedForm instanceof HTMLFormElement) {
                associatedForm.reset();
                return true;
            }

            if ((type === "" || type === "submit" || type === "image") && associatedForm instanceof HTMLFormElement) {
                if (typeof associatedForm.requestSubmit === "function") {
                    try {
                        // submitter parametresi verilince, buton form dışındaysa
                        // spec gereği NotFoundError fırlatılır — bu durumda parametresiz çağır.
                        associatedForm.requestSubmit(control);
                    } catch {
                        associatedForm.requestSubmit();
                    }
                } else {
                    control.click();
                }

                return true;
            }

            control.click();
            return true;
        };

        const readToolbarContext = () => {
            const toolbar = getActiveToolbar();
            if (!(toolbar instanceof HTMLElement) || toolbar.hasAttribute("data-no-proxy")) {
                return { title: root.getAttribute("data-page-title") ?? "", actions: [] };
            }

            const toolbarId = toolbar.getAttribute("data-workspace-toolbar-id")?.trim() || "calibra-toolbar";
            const actions = getToolbarControls(toolbar)
                .filter((control) => !isControlHidden(control))
                .map((control, index) => {
                    const actionId = control.getAttribute("data-workspace-toolbar-action-id")?.trim()
                        || createActionId(toolbarId, index);

                    control.setAttribute("data-workspace-toolbar-action-id", actionId);

                    const label = control instanceof HTMLInputElement
                        ? normalizeText(control.value)
                        : normalizeText(control.textContent);

                    return {
                        id: actionId,
                        className: control.className || "",
                        title: control.getAttribute("title") || label,
                        ariaLabel: control.getAttribute("aria-label") || label,
                        html: control instanceof HTMLInputElement ? "" : control.innerHTML,
                        text: control instanceof HTMLInputElement ? (control.value || label) : "",
                        disabled: "disabled" in control ? control.disabled : false,
                        tagName: control.tagName
                    };
                });

            return {
                title: root.getAttribute("data-page-title") ?? "",
                actions
            };
        };

        toolbars.forEach((toolbar, index) => {
            if (!(toolbar instanceof HTMLElement)) {
                return;
            }

            const card = toolbar.closest(".integrator-card--form, .integrator-card");
            let primaryForm = toolbar.closest("form.integrator-form");
            if (!(primaryForm instanceof HTMLFormElement) && card instanceof HTMLElement) {
                primaryForm = Array.from(card.querySelectorAll("form.integrator-form"))
                    .find((form) => !form.classList.contains("integrator-inline-form")) ?? null;
            }

            if (primaryForm instanceof HTMLFormElement) {
                const formId = ensureFormId(primaryForm, index);

                toolbar.querySelectorAll("button, input").forEach((control) => {
                    if (!(control instanceof HTMLButtonElement) && !(control instanceof HTMLInputElement)) {
                        return;
                    }

                    if (control.hasAttribute("form") || !shouldBindControlToForm(control)) {
                        return;
                    }

                    control.setAttribute("form", formId);
                });
            }

            if (card instanceof HTMLElement) {
                card.classList.add("entity-form-card");
            }

            toolbar.classList.add("entity-form-toolbar");
            toolbar.setAttribute("role", "toolbar");
            toolbar.classList.remove("entity-form-toolbar--floating");
            toolbar.style.removeProperty("top");
            toolbar.style.removeProperty("left");
            toolbar.style.removeProperty("width");
            ensureToolbarId(toolbar, index);

            if (card instanceof HTMLElement) {
                placeToolbarAtCanonicalPosition(toolbar, card);
            }

            toolbar.addEventListener("pointerdown", () => {
                updateActiveToolbar(toolbar);
            });

            toolbar.addEventListener("focusin", () => {
                updateActiveToolbar(toolbar);
            });

            if (primaryForm instanceof HTMLFormElement) {
                primaryForm.addEventListener("focusin", () => {
                    updateActiveToolbar(toolbar);
                });

                primaryForm.addEventListener("pointerdown", () => {
                    updateActiveToolbar(toolbar);
                });

                primaryForm.addEventListener("input", () => {
                    updateActiveToolbar(toolbar);
                });
            }

            if (shouldProxyToolbar && !toolbar.hasAttribute("data-no-proxy")) {
                toolbar.classList.add("workspace-toolbar-source");
            } else {
                toolbar.classList.remove("workspace-toolbar-source");
            }
        });

        activeToolbar = toolbars[0] ?? null;

        window.calibraGetWorkspaceToolbarContext = readToolbarContext;
        window.calibraInvokeWorkspaceToolbarAction = (actionId) => {
            const toolbar = getActiveToolbar();
            if (!(toolbar instanceof HTMLElement) || !actionId) {
                return false;
            }

            const targetControl = getToolbarControls(toolbar)
                .find((control) => control.getAttribute("data-workspace-toolbar-action-id") === actionId);

            return invokeToolbarControl(targetControl ?? null);
        };

        const scheduleToolbarSync = () => {
            window.requestAnimationFrame(() => {
                requestHostSync();
            });
        };

        if (shouldProxyToolbar) {
            document.addEventListener("click", (event) => {
                const target = event.target;
                if (!(target instanceof Element)) {
                    return;
                }

                const toolbar = target.closest(toolbarSelector);
                if (toolbar instanceof HTMLElement && toolbars.includes(toolbar)) {
                    updateActiveToolbar(toolbar);
                }

                if (target.closest(toolbarSelector)) {
                    scheduleToolbarSync();
                }
            });

            document.addEventListener("change", scheduleToolbarSync);
            document.addEventListener("input", scheduleToolbarSync);
            document.addEventListener("submit", scheduleToolbarSync);

            requestHostSync();
        }

        /* ── Veritabani Bilgi Butonu ─────────────────────────────── */
        (function () {
            var infoDiv = document.getElementById("calibra-db-info");
            if (!infoDiv) return;
            var infoHtml = infoDiv.innerHTML;
            var infoTitle = (infoDiv.getAttribute("data-page-title") || "Sayfa") + " \u2014 Veritabani Yapisi";

            toolbars.forEach(function (toolbar) {
                if (!(toolbar instanceof HTMLElement)) return;
                if (toolbar.querySelector(".calibra-dbinfo-btn")) return;
                var btn = document.createElement("button");
                btn.type = "button";
                btn.className = "calibra-dbinfo-btn";
                btn.title = "Veritabani Yapisi";
                btn.innerHTML = '<svg width="16" height="16" viewBox="0 0 24 24" fill="white" stroke="none"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z"/></svg>';
                btn.setAttribute("data-workspace-toolbar-action-id", "calibra-dbinfo-action");
                btn.addEventListener("click", function (e) {
                    e.preventDefault();
                    e.stopPropagation();
                    calibraShowDbInfoModal(infoTitle, infoHtml);
                });
                toolbar.appendChild(btn);
            });
        })();
    };

    /* ── Veritabani Bilgi Modal ──────────────────────────────────── */
    window.calibraShowDbInfoModal = function (title, html) {
        var existing = document.getElementById("calibra-dbinfo-modal");
        if (existing) existing.remove();

        var overlay = document.createElement("div");
        overlay.id = "calibra-dbinfo-modal";
        overlay.style.cssText = "position:fixed;top:0;left:0;right:0;bottom:0;z-index:9999;background:rgba(0,0,0,0.4);display:flex;align-items:center;justify-content:center;";

        var box = document.createElement("div");
        box.style.cssText = "background:#fff;border-radius:10px;box-shadow:0 8px 32px rgba(0,0,0,0.18);max-width:540px;width:90%;max-height:80vh;display:flex;flex-direction:column;overflow:hidden;";

        var header = document.createElement("div");
        header.style.cssText = "display:flex;align-items:center;justify-content:space-between;padding:12px 18px;border-bottom:1px solid #e2e8f0;flex-shrink:0;";
        header.innerHTML = '<span style="font-weight:700;font-size:0.88rem;color:#1e293b;">' + title + '</span>';

        var closeBtn = document.createElement("button");
        closeBtn.type = "button";
        closeBtn.style.cssText = "background:none;border:none;font-size:1.2rem;color:#94a3b8;cursor:pointer;padding:0 4px;line-height:1;";
        closeBtn.innerHTML = "\u00d7";
        closeBtn.addEventListener("click", function () { overlay.remove(); });
        header.appendChild(closeBtn);

        var body = document.createElement("div");
        body.style.cssText = "padding:16px 18px;overflow-y:auto;font-size:0.8rem;color:#334155;line-height:1.6;";
        body.innerHTML = html;

        box.appendChild(header);
        box.appendChild(body);
        overlay.appendChild(box);
        document.body.appendChild(overlay);

        overlay.addEventListener("click", function (e) { if (e.target === overlay) overlay.remove(); });
        document.addEventListener("keydown", function handler(e) {
            if (e.key === "Escape") { overlay.remove(); document.removeEventListener("keydown", handler); }
        });
    };

    const setupWorkspaceActionBarProxy = () => {
        const workspaceActionBar = document.querySelector("[data-workspace-action-bar]");
        const workspaceActionBarPrimary = workspaceActionBar?.querySelector("[data-workspace-action-bar-primary]");
        const panelStack = document.querySelector("[data-workspace-panel-stack]");

        if (!(workspaceActionBar instanceof HTMLElement) ||
            !(workspaceActionBarPrimary instanceof HTMLElement) ||
            !(panelStack instanceof HTMLElement) ||
            root.classList.contains("workspace-frame-body")) {
            requestWorkspaceToolbarSync = () => {};
            return;
        }

        const syncWorkspaceActionBarState = () => {
            const shouldShowWorkspaceActionBar = workspaceActionBarPrimary.childElementCount > 0;
            workspaceActionBar.hidden = !shouldShowWorkspaceActionBar;

            const nextHeight = shouldShowWorkspaceActionBar
                ? Math.ceil(workspaceActionBar.getBoundingClientRect().height)
                : 0;

            document.documentElement.style.setProperty("--workspace-action-bar-height", `${nextHeight}px`);
        };

        const resolveActiveSourceWindow = () => {
            const activePanel = panelStack.querySelector("[data-workspace-panel].is-active");
            if (!(activePanel instanceof HTMLElement)) {
                return window;
            }

            if (activePanel.hasAttribute("data-workspace-native-page")) {
                return window;
            }

            const frame = activePanel.querySelector("[data-workspace-frame]");
            if (!(frame instanceof HTMLIFrameElement)) {
                return null;
            }

            try {
                return frame.contentWindow;
            } catch {
                return null;
            }
        };

        const renderActionProxy = () => {
            workspaceActionBarPrimary.replaceChildren();

            const sourceWindow = resolveActiveSourceWindow();
            const context = typeof sourceWindow?.calibraGetWorkspaceToolbarContext === "function"
                ? sourceWindow.calibraGetWorkspaceToolbarContext()
                : null;

            if (!context || !Array.isArray(context.actions) || context.actions.length === 0) {
                syncWorkspaceActionBarState();
                return;
            }

            const proxyToolbar = document.createElement("div");
            proxyToolbar.className = "entity-form-toolbar workspace-action-bar__proxy";
            proxyToolbar.setAttribute("role", "toolbar");

            context.actions.forEach((action) => {
                if (!action || !action.id) {
                    return;
                }

                const proxyButton = document.createElement("button");
                proxyButton.type = "button";
                proxyButton.className = action.className || "btn btn-primary";
                proxyButton.disabled = action.disabled === true;

                if (action.ariaLabel) {
                    proxyButton.setAttribute("aria-label", action.ariaLabel);
                }

                if (action.title) {
                    proxyButton.setAttribute("title", action.title);
                }

                if (action.html) {
                    proxyButton.innerHTML = action.html;
                } else {
                    proxyButton.textContent = action.text || action.ariaLabel || action.title || "Aksiyon";
                }

                proxyButton.addEventListener("click", () => {
                    if (typeof sourceWindow?.calibraInvokeWorkspaceToolbarAction === "function") {
                        sourceWindow.calibraInvokeWorkspaceToolbarAction(action.id);
                    }

                    window.requestAnimationFrame(renderActionProxy);
                });

                proxyToolbar.appendChild(proxyButton);
            });

            workspaceActionBarPrimary.appendChild(proxyToolbar);
            syncWorkspaceActionBarState();
        };

        requestWorkspaceToolbarSync = () => {
            renderActionProxy();
        };

        window.calibraRequestWorkspaceToolbarSync = () => {
            renderActionProxy();
        };

        renderActionProxy();
    };

    const setupEntityLayoutTabs = () => {
        document.querySelectorAll("[data-entity-layout-tabs]").forEach((root) => {
            if (!(root instanceof HTMLElement)) {
                return;
            }

            const buttons = Array.from(root.querySelectorAll("[data-entity-layout-tab-button]"))
                .filter((button) => button instanceof HTMLButtonElement);
            const panes = Array.from(root.querySelectorAll("[data-entity-layout-pane]"))
                .filter((pane) => pane instanceof HTMLElement);

            if (buttons.length === 0 || panes.length === 0) {
                return;
            }

            const activateTab = (tabKey) => {
                buttons.forEach((button) => {
                    const isActive = button.getAttribute("data-entity-layout-tab-button") === tabKey;
                    button.classList.toggle("is-active", isActive);
                    button.setAttribute("aria-selected", isActive ? "true" : "false");
                });

                panes.forEach((pane) => {
                    const isActive = pane.getAttribute("data-entity-layout-pane") === tabKey;
                    pane.classList.toggle("is-active", isActive);
                });
            };

            buttons.forEach((button) => {
                button.addEventListener("click", () => {
                    activateTab(button.getAttribute("data-entity-layout-tab-button") || "");
                });
            });

            const initialButton = buttons.find((button) => button.classList.contains("is-active")) ?? buttons[0];
            activateTab(initialButton.getAttribute("data-entity-layout-tab-button") || "");
        });
    };

    const setupCollaborationProxyButtons = () => {
        document.querySelectorAll("[data-collaboration-proxy-toggle]").forEach((button) => {
            if (!(button instanceof HTMLButtonElement) || button.dataset.collaborationProxyBound === "1") {
                return;
            }

            button.dataset.collaborationProxyBound = "1";
            button.addEventListener("click", (event) => {
                event.preventDefault();

                try {
                    const targetWindow = window.parent && window.parent !== window ? window.parent : window;
                    const collaborationApi = targetWindow.calibraCollaborationApi;

                    if (collaborationApi && typeof collaborationApi.togglePanel === "function") {
                        collaborationApi.togglePanel();
                        return;
                    }

                    const fallbackToggle = targetWindow.document?.querySelector("[data-collaboration-toggle]");
                    if (fallbackToggle instanceof HTMLElement) {
                        fallbackToggle.click();
                    }
                } catch {
                    // Ignore parent window access errors.
                }
            });
        });
    };

    const setupGridSearchAutoSubmit = () => {
        const debounceDelayMs = 420;

        const isTextSearchInput = (input) => {
            if (!(input instanceof HTMLInputElement)) {
                return false;
            }

            const type = (input.getAttribute("type") ?? "text").trim().toLowerCase();
            return type === "search" || type === "text" || type === "";
        };

        const isSearchActionButton = (button) => {
            if (!(button instanceof HTMLButtonElement) || button.disabled) {
                return false;
            }

            const buttonText = normalize(button.textContent ?? "");
            if (buttonText.includes("temizle") || buttonText.includes("clear") || buttonText.includes("reset")) {
                return false;
            }

            const type = (button.getAttribute("type") ?? "submit").trim().toLowerCase();
            return type === "submit" || type === "button";
        };

        const resolveSearchAction = (toolbar) => {
            if (!(toolbar instanceof HTMLElement)) {
                return null;
            }

            const buttons = Array.from(toolbar.querySelectorAll("button"))
                .filter((button) => isSearchActionButton(button));
            const preferredButton = buttons.find((button) => button.classList.contains("integrator-btn-primary"))
                ?? buttons[0];

            if (preferredButton instanceof HTMLButtonElement) {
                return () => {
                    if (preferredButton.type === "submit") {
                        const form = preferredButton.form ?? preferredButton.closest("form");
                        if (form instanceof HTMLFormElement) {
                            form.requestSubmit(preferredButton);
                            return;
                        }
                    }

                    preferredButton.click();
                };
            }

            if (toolbar instanceof HTMLFormElement) {
                const method = (toolbar.getAttribute("method") ?? "get").trim().toLowerCase();
                if (method === "get") {
                    return () => {
                        toolbar.requestSubmit();
                    };
                }
            }

            return null;
        };

        const bindToolbar = (toolbar) => {
            if (!(toolbar instanceof HTMLElement) || toolbar.dataset.gridSearchAutoBound === "1") {
                return;
            }

            const searchInputs = Array.from(toolbar.querySelectorAll("input"))
                .filter((input) => isTextSearchInput(input));
            if (searchInputs.length === 0) {
                return;
            }

            const triggerSearch = resolveSearchAction(toolbar);
            if (typeof triggerSearch !== "function") {
                return;
            }

            toolbar.dataset.gridSearchAutoBound = "1";

            searchInputs.forEach((input, index) => {
                if (!(input instanceof HTMLInputElement)) {
                    return;
                }

                const storageKey = `gridSearchAutoValue${index}`;
                let debounceTimer = 0;
                let isComposing = false;

                input.dataset[storageKey] = input.value ?? "";

                const scheduleSearch = () => {
                    if (isComposing) {
                        return;
                    }

                    const currentValue = input.value ?? "";
                    if (input.dataset[storageKey] === currentValue) {
                        return;
                    }

                    if (debounceTimer) {
                        window.clearTimeout(debounceTimer);
                    }

                    debounceTimer = window.setTimeout(() => {
                        input.dataset[storageKey] = currentValue;
                        triggerSearch();
                    }, debounceDelayMs);
                };

                input.addEventListener("input", scheduleSearch);
                input.addEventListener("search", scheduleSearch);
                input.addEventListener("compositionstart", () => {
                    isComposing = true;
                });
                input.addEventListener("compositionend", () => {
                    isComposing = false;
                    scheduleSearch();
                });
            });
        };

        const bindToolbars = (scope) => {
            const rootElement = scope instanceof Document || scope instanceof HTMLElement
                ? scope
                : document;

            if (rootElement instanceof HTMLElement && rootElement.matches(".integrator-table-search")) {
                bindToolbar(rootElement);
            }

            rootElement.querySelectorAll(".integrator-table-search").forEach((toolbar) => {
                bindToolbar(toolbar);
            });
        };

        bindToolbars(document);

        if (typeof MutationObserver === "undefined" || !(document.body instanceof HTMLElement)) {
            return;
        }

        const observer = new MutationObserver((mutations) => {
            mutations.forEach((mutation) => {
                mutation.addedNodes.forEach((node) => {
                    if (!(node instanceof HTMLElement)) {
                        return;
                    }

                    bindToolbars(node);
                });
            });
        });

        observer.observe(document.body, { childList: true, subtree: true });
    };

    const setupInteractiveGrids = () => {
        const minColumnWidth = 72;
        const actionHeaderTokens = new Set(["", "action", "actions", "aksiyon", "islem", "secenek", "secenekler"]);
        const collator = new Intl.Collator("tr", {
            numeric: true,
            sensitivity: "base"
        });

        const normalizeText = (value) => typeof value === "string"
            ? value.replace(/\s+/g, " ").trim()
            : "";

        const normalizeHeaderToken = (value) => normalize(value).replace(/\s+/g, "");

        const isMissingValue = (value) => {
            const text = normalizeText(value);
            return text === "" || text === "-" || text === "--";
        };

        const parseNumberValue = (value) => {
            const text = normalizeText(value).replace(/\s+/g, "");
            if (!text) {
                return null;
            }

            const sanitized = text.replace(/[%$€₺]/g, "");
            if (!/^[+-]?[\d.,]+$/.test(sanitized)) {
                return null;
            }

            const lastCommaIndex = sanitized.lastIndexOf(",");
            const lastDotIndex = sanitized.lastIndexOf(".");
            let normalizedNumber = sanitized;

            if (lastCommaIndex >= 0 && lastDotIndex >= 0) {
                normalizedNumber = lastCommaIndex > lastDotIndex
                    ? sanitized.replace(/\./g, "").replace(",", ".")
                    : sanitized.replace(/,/g, "");
            } else if (lastCommaIndex >= 0) {
                normalizedNumber = sanitized.replace(/\./g, "").replace(",", ".");
            } else {
                normalizedNumber = sanitized.replace(/,/g, "");
            }

            const parsed = Number(normalizedNumber);
            return Number.isFinite(parsed) ? parsed : null;
        };

        const parseDateValue = (value) => {
            const text = normalizeText(value);
            if (!text) {
                return null;
            }

            let match = text.match(/^(\d{4})-(\d{2})-(\d{2})(?:\s+(\d{2}):(\d{2})(?::(\d{2}))?)?$/);
            if (match) {
                const year = Number(match[1]);
                const month = Number(match[2]) - 1;
                const day = Number(match[3]);
                const hour = Number(match[4] ?? "0");
                const minute = Number(match[5] ?? "0");
                const second = Number(match[6] ?? "0");
                return Date.UTC(year, month, day, hour, minute, second);
            }

            match = text.match(/^(\d{1,2})[./-](\d{1,2})[./-](\d{2,4})(?:\s+(\d{1,2}):(\d{2})(?::(\d{2}))?)?$/);
            if (!match) {
                return null;
            }

            const day = Number(match[1]);
            const month = Number(match[2]) - 1;
            const year = Number(match[3].length === 2 ? `20${match[3]}` : match[3]);
            const hour = Number(match[4] ?? "0");
            const minute = Number(match[5] ?? "0");
            const second = Number(match[6] ?? "0");
            return Date.UTC(year, month, day, hour, minute, second);
        };

        const parseBooleanValue = (value) => {
            const text = normalize(value);
            if (!text) {
                return null;
            }

            if (["1", "true", "yes", "evet", "aktif", "active", "on"].includes(text)) {
                return 1;
            }

            if (["0", "false", "no", "hayir", "pasif", "inactive", "off"].includes(text)) {
                return 0;
            }

            return null;
        };

        const extractCellValue = (cell) => {
            if (!(cell instanceof HTMLTableCellElement)) {
                return "";
            }

            const explicitValue = cell.getAttribute("data-sort-value");
            if (!isMissingValue(explicitValue ?? "")) {
                return explicitValue ?? "";
            }

            const control = cell.querySelector("input:not([type='hidden']), select, textarea");
            if (control instanceof HTMLInputElement) {
                const controlType = (control.type ?? "").trim().toLowerCase();
                if (controlType === "checkbox" || controlType === "radio") {
                    return control.checked ? "1" : "0";
                }

                return control.value ?? "";
            }

            if (control instanceof HTMLSelectElement) {
                return control.selectedOptions[0]?.text ?? control.value ?? "";
            }

            if (control instanceof HTMLTextAreaElement) {
                return control.value ?? "";
            }

            return normalizeText(cell.textContent ?? "");
        };

        const isPlaceholderRow = (row) => {
            if (!(row instanceof HTMLTableRowElement)) {
                return true;
            }

            if (row.querySelector(".integrator-table__empty")) {
                return true;
            }

            return Array.from(row.cells).some((cell) => cell.colSpan > 1);
        };

        const getDataRows = (tbody) => Array.from(tbody.rows).filter((row) => !isPlaceholderRow(row));

        const detectSortMode = (rows, columnIndex) => {
            const sampleValues = rows
                .map((row) => extractCellValue(row.cells[columnIndex]))
                .filter((value) => !isMissingValue(value))
                .slice(0, 24);

            if (sampleValues.length === 0) {
                return "string";
            }

            if (sampleValues.every((value) => parseNumberValue(value) !== null)) {
                return "number";
            }

            if (sampleValues.every((value) => parseDateValue(value) !== null)) {
                return "date";
            }

            if (sampleValues.every((value) => parseBooleanValue(value) !== null)) {
                return "boolean";
            }

            return "string";
        };

        const compareValues = (leftValue, rightValue, mode) => {
            const leftEmpty = isMissingValue(leftValue);
            const rightEmpty = isMissingValue(rightValue);

            if (leftEmpty || rightEmpty) {
                if (leftEmpty && rightEmpty) {
                    return 0;
                }

                return leftEmpty ? 1 : -1;
            }

            if (mode === "number") {
                return (parseNumberValue(leftValue) ?? 0) - (parseNumberValue(rightValue) ?? 0);
            }

            if (mode === "date") {
                return (parseDateValue(leftValue) ?? 0) - (parseDateValue(rightValue) ?? 0);
            }

            if (mode === "boolean") {
                return (parseBooleanValue(leftValue) ?? 0) - (parseBooleanValue(rightValue) ?? 0);
            }

            return collator.compare(normalizeText(leftValue), normalizeText(rightValue));
        };

        const getHeaderCells = (table) => {
            const headerRow = table.tHead?.rows[0];
            if (!(headerRow instanceof HTMLTableRowElement)) {
                return [];
            }

            return Array.from(headerRow.cells)
                .filter((cell) => cell instanceof HTMLTableCellElement && cell.tagName === "TH");
        };

        const ensureGridWrap = (table) => {
            const parentElement = table.parentElement;
            if (parentElement instanceof HTMLElement &&
                (parentElement.classList.contains("integrator-table-wrap") || parentElement.classList.contains("calibra-grid-wrap"))) {
                return parentElement;
            }

            if (!(table.parentNode instanceof Node)) {
                return null;
            }

            const wrapper = document.createElement("div");
            wrapper.className = "calibra-grid-wrap";
            table.parentNode.insertBefore(wrapper, table);
            wrapper.appendChild(table);
            return wrapper;
        };

        const ensureColgroup = (table, columnCount) => {
            const existingColgroup = Array.from(table.children)
                .find((child) => child instanceof HTMLElement && child.tagName === "COLGROUP");
            const colgroup = existingColgroup instanceof HTMLElement
                ? existingColgroup
                : document.createElement("colgroup");

            if (!(existingColgroup instanceof HTMLElement)) {
                table.insertBefore(colgroup, table.firstChild);
            }

            while (colgroup.children.length < columnCount) {
                colgroup.appendChild(document.createElement("col"));
            }

            while (colgroup.children.length > columnCount) {
                colgroup.lastElementChild?.remove();
            }

            return colgroup;
        };

        const measureColumnWidths = (table, headerCells) => headerCells.map((headerCell, index) => {
            const headerWidth = Math.round(headerCell.getBoundingClientRect().width || headerCell.offsetWidth || 0);
            if (headerWidth > 0) {
                return Math.max(minColumnWidth, headerWidth);
            }

            const sampleCell = table.tBodies[0]?.rows[0]?.cells[index];
            const sampleWidth = Math.round(sampleCell?.getBoundingClientRect().width || sampleCell?.offsetWidth || 0);
            return Math.max(minColumnWidth, sampleWidth || minColumnWidth);
        });

        const applyColumnWidths = (table, colgroup, widths) => {
            Array.from(colgroup.children).forEach((col, index) => {
                if (!(col instanceof HTMLElement)) {
                    return;
                }

                const width = widths[index] ?? minColumnWidth;
                col.style.width = `${width}px`;
                col.style.minWidth = `${width}px`;
            });

            const wrapper = table.parentElement;
            const contentWidth = widths.reduce((sum, width) => sum + width, 0);
            const visibleWidth = wrapper instanceof HTMLElement ? Math.round(wrapper.clientWidth) : 0;
            const nextTableWidth = Math.max(contentWidth, visibleWidth);
            table.style.width = `${nextTableWidth}px`;
            table.style.minWidth = `${nextTableWidth}px`;
        };

        const updateSortState = (table, headerCells, sortableIndices, sortedColumnIndex, direction) => {
            headerCells.forEach((headerCell, index) => {
                if (!sortableIndices.has(index)) {
                    headerCell.removeAttribute("aria-sort");
                    return;
                }

                if (index === sortedColumnIndex) {
                    headerCell.setAttribute("aria-sort", direction === "asc" ? "ascending" : "descending");
                    return;
                }

                headerCell.setAttribute("aria-sort", "none");
            });

            table.dataset.gridSortIndex = String(sortedColumnIndex);
            table.dataset.gridSortDirection = direction;
        };

        const sortByColumn = (table, columnIndex, headerCells, sortableIndices) => {
            const tbody = table.tBodies[0];
            if (!(tbody instanceof HTMLTableSectionElement)) {
                return;
            }

            const dataRows = getDataRows(tbody);
            if (dataRows.length < 2) {
                updateSortState(table, headerCells, sortableIndices, columnIndex, "asc");
                return;
            }

            const currentSortIndex = Number(table.dataset.gridSortIndex ?? "-1");
            const currentDirection = table.dataset.gridSortDirection === "desc" ? "desc" : "asc";
            const nextDirection = currentSortIndex === columnIndex && currentDirection === "asc" ? "desc" : "asc";
            const sortMode = detectSortMode(dataRows, columnIndex);

            const sortedRows = dataRows
                .map((row, index) => ({
                    row,
                    index,
                    value: extractCellValue(row.cells[columnIndex])
                }))
                .sort((left, right) => {
                    const comparison = compareValues(left.value, right.value, sortMode);
                    if (comparison === 0) {
                        return left.index - right.index;
                    }

                    return nextDirection === "asc" ? comparison : -comparison;
                });

            const placeholderRows = Array.from(tbody.rows).filter((row) => isPlaceholderRow(row));
            tbody.append(...sortedRows.map((entry) => entry.row), ...placeholderRows);
            updateSortState(table, headerCells, sortableIndices, columnIndex, nextDirection);
        };

        let activeResize = null;

        const stopResize = () => {
            if (!activeResize) {
                return;
            }

            activeResize.table.classList.remove("is-resizing");
            document.body.classList.remove("calibra-grid-resizing");
            activeResize = null;
        };

        document.addEventListener("pointermove", (event) => {
            if (!activeResize || event.pointerId !== activeResize.pointerId) {
                return;
            }

            event.preventDefault();

            const deltaX = event.clientX - activeResize.startX;
            const nextWidths = [...activeResize.widths];
            nextWidths[activeResize.columnIndex] = Math.max(minColumnWidth, Math.round(activeResize.startWidth + deltaX));
            activeResize.widths = nextWidths;
            applyColumnWidths(activeResize.table, activeResize.colgroup, nextWidths);
        }, { passive: false });

        document.addEventListener("pointerup", stopResize);
        document.addEventListener("pointercancel", stopResize);

        const enhanceGrid = (table) => {
            if (!(table instanceof HTMLTableElement) ||
                table.dataset.gridEnhanced === "1" ||
                table.getAttribute("data-grid-enhancement-skip") === "1") {
                return;
            }

            const tbody = table.tBodies[0];
            const headerCells = getHeaderCells(table);
            if (!(tbody instanceof HTMLTableSectionElement) || headerCells.length === 0) {
                return;
            }

            table.dataset.gridEnhanced = "1";
            table.classList.add("calibra-grid");
            ensureGridWrap(table);

            const sortableIndices = new Set();

            headerCells.forEach((headerCell, columnIndex) => {
                headerCell.classList.add("calibra-grid__header-cell");

                const headerToken = normalizeHeaderToken(headerCell.textContent ?? "");
                const sortable = !actionHeaderTokens.has(headerToken);

                if (sortable) {
                    sortableIndices.add(columnIndex);

                    const sortButton = document.createElement("button");
                    sortButton.type = "button";
                    sortButton.className = "calibra-grid__sort-button";
                    sortButton.setAttribute("aria-label", `${normalizeText(headerCell.textContent ?? "") || "Kolon"} alanina gore sirala`);

                    const label = document.createElement("span");
                    label.className = "calibra-grid__sort-label";

                    while (headerCell.firstChild) {
                        label.appendChild(headerCell.firstChild);
                    }

                    const indicator = document.createElement("span");
                    indicator.className = "calibra-grid__sort-indicator";
                    indicator.setAttribute("aria-hidden", "true");

                    sortButton.append(label, indicator);
                    sortButton.addEventListener("click", () => {
                        sortByColumn(table, columnIndex, headerCells, sortableIndices);
                    });

                    headerCell.appendChild(sortButton);
                    headerCell.setAttribute("aria-sort", "none");
                }

                const resizeHandle = document.createElement("span");
                resizeHandle.className = "calibra-grid__resize-handle";
                resizeHandle.setAttribute("aria-hidden", "true");
                resizeHandle.addEventListener("pointerdown", (event) => {
                    if (event.button !== 0) {
                        return;
                    }

                    event.preventDefault();
                    event.stopPropagation();

                    const currentHeaderCells = getHeaderCells(table);
                    const currentWidths = measureColumnWidths(table, currentHeaderCells);
                    const colgroup = ensureColgroup(table, currentHeaderCells.length);

                    applyColumnWidths(table, colgroup, currentWidths);
                    activeResize = {
                        pointerId: event.pointerId,
                        table,
                        colgroup,
                        columnIndex,
                        startX: event.clientX,
                        startWidth: currentWidths[columnIndex] ?? minColumnWidth,
                        widths: currentWidths
                    };

                    table.classList.add("is-resizing");
                    document.body.classList.add("calibra-grid-resizing");

                    if (typeof resizeHandle.setPointerCapture === "function") {
                        resizeHandle.setPointerCapture(event.pointerId);
                    }
                });

                headerCell.appendChild(resizeHandle);
            });
        };

        const enhanceGrids = (scope) => {
            const rootElement = scope instanceof Document || scope instanceof HTMLElement
                ? scope
                : document;

            if (rootElement instanceof HTMLTableElement && rootElement.matches("table.table")) {
                enhanceGrid(rootElement);
            }

            rootElement.querySelectorAll("table.table").forEach((table) => {
                enhanceGrid(table);
            });
        };

        enhanceGrids(document);

        if (typeof MutationObserver === "undefined" || !(document.body instanceof HTMLElement)) {
            return;
        }

        const observer = new MutationObserver((mutations) => {
            mutations.forEach((mutation) => {
                mutation.addedNodes.forEach((node) => {
                    if (!(node instanceof HTMLElement)) {
                        return;
                    }

                    enhanceGrids(node);
                });
            });
        });

        observer.observe(document.body, { childList: true, subtree: true });
    };

    const setupCompactGridActions = () => {
        const iconMarkupByType = {
            add: "<svg viewBox='0 0 24 24' aria-hidden='true'><path d='M12 5v14'></path><path d='M5 12h14'></path></svg>",
            default: "<svg viewBox='0 0 24 24' aria-hidden='true'><path d='M8 12h8'></path><path d='m13 7 5 5-5 5'></path></svg>",
            delete: "<svg viewBox='0 0 24 24' aria-hidden='true'><path d='M4 7h16'></path><path d='M9 7V5.5A1.5 1.5 0 0 1 10.5 4h3A1.5 1.5 0 0 1 15 5.5V7'></path><path d='M7.5 7 8 19a1 1 0 0 0 1 .96h6a1 1 0 0 0 1-.96L16.5 7'></path><path d='M10 11v5'></path><path d='M14 11v5'></path></svg>",
            details: "<svg viewBox='0 0 24 24' aria-hidden='true'><path d='M8 7h12'></path><path d='M8 12h12'></path><path d='M8 17h12'></path><path d='M4 7h.01'></path><path d='M4 12h.01'></path><path d='M4 17h.01'></path></svg>",
            edit: "<svg viewBox='0 0 24 24' aria-hidden='true'><path d='m4 20 4.2-1 9.7-9.7a2.1 2.1 0 0 0-3-3L5.2 16 4 20z'></path><path d='m13.5 7.5 3 3'></path></svg>",
            navigate: "<svg viewBox='0 0 24 24' aria-hidden='true'><path d='M5 12h12'></path><path d='m13 6 6 6-6 6'></path></svg>",
            select: "<svg viewBox='0 0 24 24' aria-hidden='true'><rect x='5' y='5' width='14' height='14' rx='3'></rect></svg>"
        };
        const actionHeaderTokens = new Set(["", "action", "actions", "aksiyon", "islem", "islemler", "secenek", "secenekler"]);

        const resolveActionType = (label) => {
            const normalizedLabel = normalize(label).replace(/\s+/g, " ");
            if (!normalizedLabel) {
                return null;
            }

            if (normalizedLabel.includes("sil")) {
                return "delete";
            }

            if (normalizedLabel.includes("duzenle")) {
                return "edit";
            }

            if (normalizedLabel.includes("sec")) {
                return "select";
            }

            if (normalizedLabel.includes("ekle")) {
                return "add";
            }

            if (normalizedLabel.includes("gec") || normalizedLabel.includes("yukle")) {
                return "navigate";
            }

            if (normalizedLabel.includes("liste") || normalizedLabel.includes("detay") || normalizedLabel.includes("goruntule")) {
                return "details";
            }

            return "default";
        };

        const getControlButton = (element) => {
            if (element instanceof HTMLAnchorElement || element instanceof HTMLButtonElement) {
                return element;
            }

            if (element instanceof HTMLFormElement) {
                return element.querySelector("button.btn, a.btn");
            }

            return null;
        };

        const getControlLabel = (element) => {
            const button = getControlButton(element);
            if (!(button instanceof HTMLElement)) {
                return "";
            }

            return (button.getAttribute("data-grid-action-label")
                || button.getAttribute("aria-label")
                || button.textContent
                || "")
                .replace(/\s+/g, " ")
                .trim();
        };

        const classifyControl = (element) => resolveActionType(getControlLabel(element)) || "default";

        const shouldCompactGridAction = (button) => {
            if (!(button instanceof HTMLElement)) {
                return false;
            }

            if (button.getAttribute("data-grid-action-compact") === "0") {
                return false;
            }

            if (button.getAttribute("data-grid-action-compact") === "1") {
                return true;
            }

            if (button.closest(".material-row-actions") instanceof HTMLElement &&
                button.closest(".integrator-table tbody, .calibra-grid tbody") instanceof HTMLElement) {
                return true;
            }

            const tableCell = button.closest("td");
            const tableRow = tableCell?.parentElement;
            if (tableCell instanceof HTMLTableCellElement &&
                tableRow instanceof HTMLTableRowElement &&
                tableCell.cellIndex === tableRow.cells.length - 1 &&
                tableCell.closest(".integrator-table tbody, .calibra-grid tbody") instanceof HTMLElement) {
                return true;
            }

            return button.closest("[data-grid-compact-actions='1']") instanceof HTMLElement;
        };

        const enhanceGridAction = (button) => {
            if (!(button instanceof HTMLElement) || button.dataset.gridActionEnhanced === "1") {
                return;
            }

            if (!shouldCompactGridAction(button)) {
                return;
            }

            if (button.closest(".modal") || button.closest(".dropdown-menu")) {
                return;
            }

            if (button.classList.contains("integrator-row-menu__trigger")) {
                button.dataset.gridActionEnhanced = "1";
                return;
            }

            const existingSvg = button.querySelector("svg");
            const existingText = (button.textContent || "").replace(/\s+/g, " ").trim();
            if (existingSvg && !existingText) {
                button.dataset.gridActionEnhanced = "1";
                return;
            }

            const label = (button.getAttribute("data-grid-action-label")
                || button.getAttribute("aria-label")
                || existingText)
                .replace(/\s+/g, " ")
                .trim();

            if (!label) {
                return;
            }

            const actionType = resolveActionType(label);
            if (!actionType) {
                return;
            }

            button.dataset.gridActionEnhanced = "1";
            button.classList.add("integrator-grid-action-btn", `integrator-grid-action-btn--${actionType}`);

            if (!button.getAttribute("aria-label")) {
                button.setAttribute("aria-label", label);
            }

            if (!button.getAttribute("title")) {
                button.setAttribute("title", label);
            }

            const icon = document.createElement("span");
            icon.className = "integrator-grid-action-btn__icon";
            icon.setAttribute("aria-hidden", "true");
            icon.innerHTML = iconMarkupByType[actionType] || iconMarkupByType.default;

            const srOnly = document.createElement("span");
            srOnly.className = "visually-hidden";
            srOnly.textContent = label;

            button.replaceChildren(icon, srOnly);
        };

        const createPlaceholderButton = (actionType, label) => {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "btn btn-outline-secondary integrator-btn-tertiary";
            button.setAttribute("data-grid-action-label", label);
            button.setAttribute("aria-label", label);
            button.setAttribute("title", label);
            button.disabled = true;
            button.dataset.gridActionCompact = "1";
            button.dataset.gridActionType = actionType;
            return button;
        };


        const detectActionColumnIndex = (table) => {
            const headerRow = table.tHead?.rows[0];
            const dataRows = Array.from(table.tBodies[0]?.rows ?? [])
                .filter((row) => row instanceof HTMLTableRowElement && row.cells.length > 0);

            if (headerRow instanceof HTMLTableRowElement) {
                for (let index = 0; index < headerRow.cells.length; index += 1) {
                    const headerCell = headerRow.cells[index];
                    const headerToken = normalize(headerCell.textContent ?? "").replace(/\s+/g, "");
                    if (actionHeaderTokens.has(headerToken)) {
                        return index;
                    }
                }
            }

            const columnCount = headerRow?.cells.length ?? dataRows[0]?.cells.length ?? 0;
            for (let index = columnCount - 1; index >= 0; index -= 1) {
                const hasActionControl = dataRows.some((row) =>
                    row.cells[index]?.querySelector("a.btn, button.btn, form button.btn"));
                if (hasActionControl) {
                    return index;
                }
            }

            return -1;
        };

        const moveColumnToFront = (table, columnIndex) => {
            if (columnIndex <= 0) {
                return;
            }

            const moveCell = (row) => {
                if (!(row instanceof HTMLTableRowElement) || row.cells.length <= columnIndex) {
                    return;
                }

                const cell = row.cells[columnIndex];
                if (!(cell instanceof HTMLTableCellElement)) {
                    return;
                }

                row.insertBefore(cell, row.firstElementChild);
            };

            Array.from(table.tHead?.rows ?? []).forEach(moveCell);
            Array.from(table.tBodies).forEach((tbody) => Array.from(tbody.rows).forEach(moveCell));
            Array.from(table.tFoot?.rows ?? []).forEach(moveCell);

            const colgroup = table.querySelector("colgroup");
            if (colgroup instanceof HTMLElement && colgroup.children.length > columnIndex) {
                const column = colgroup.children[columnIndex];
                if (column instanceof HTMLElement) {
                    colgroup.insertBefore(column, colgroup.firstElementChild);
                }
            }
        };

        const ensureActionColumn = (table) => {
            if (!(table instanceof HTMLTableElement) || table.getAttribute("data-grid-actions-skip") === "1") {
                return;
            }

            const tbody = table.tBodies[0];
            if (!(tbody instanceof HTMLTableSectionElement)) {
                return;
            }

            if (table.dataset.gridActionColumnEnhanced !== "1") {
                const actionColumnIndex = detectActionColumnIndex(table);
                if (actionColumnIndex >= 0) {
                    moveColumnToFront(table, actionColumnIndex);
                } else {
                    const headerRow = table.tHead?.rows[0];
                    if (headerRow instanceof HTMLTableRowElement) {
                        const headerCell = document.createElement("th");
                        headerRow.insertBefore(headerCell, headerRow.firstElementChild);
                    }

                    Array.from(tbody.rows).forEach((row) => {
                        if (!(row instanceof HTMLTableRowElement)) {
                            return;
                        }

                        const cell = document.createElement("td");
                        row.insertBefore(cell, row.firstElementChild);
                    });
                }

                table.dataset.gridActionColumnEnhanced = "1";
            }

            const headerCell = table.tHead?.rows[0]?.cells[0];
            if (headerCell instanceof HTMLTableCellElement) {
                headerCell.classList.add("calibra-grid__actions-header");
                headerCell.setAttribute("data-grid-action-column", "1");
                if (!headerCell.textContent?.trim()) {
                    headerCell.innerHTML = "<span class='visually-hidden'>Islem</span>";
                }
            }

            Array.from(tbody.rows).forEach((row) => {
                if (!(row instanceof HTMLTableRowElement) || row.cells.length === 0) {
                    return;
                }

                if (row.querySelector(".integrator-table__empty")) {
                    return;
                }

                row.classList.remove("is-selected");
                row.classList.remove("integrator-table__row--selected");
                row.setAttribute("aria-selected", "false");
                const actionCell = row.cells[0];
                if (!(actionCell instanceof HTMLTableCellElement)) {
                    return;
                }

                actionCell.classList.add("calibra-grid__actions-cell");

                let actionContainer = actionCell.querySelector(".material-row-actions");
                if (!(actionContainer instanceof HTMLElement)) {
                    actionContainer = document.createElement("div");
                    actionContainer.className = "material-row-actions";
                    const existingNodes = Array.from(actionCell.childNodes);
                    actionCell.replaceChildren(actionContainer);
                    existingNodes.forEach((node) => {
                        if (node instanceof HTMLElement && node.matches("a.btn, button.btn, form")) {
                            actionContainer.appendChild(node);
                        }
                    });
                }

                const controls = Array.from(actionContainer.children)
                    .filter((child) => child instanceof HTMLElement)
                    .filter((child) => child instanceof HTMLAnchorElement
                        || child instanceof HTMLButtonElement
                        || child instanceof HTMLFormElement);

                const editControls = controls.filter((control) => classifyControl(control) === "edit");
                const deleteControls = controls.filter((control) => classifyControl(control) === "delete");

                let editControl = editControls[0];
                let deleteControl = deleteControls[0];
                const otherControls = controls.filter((control) =>
                    !editControls.includes(control) &&
                    !deleteControls.includes(control));

                const noEdit = table.hasAttribute("data-grid-no-edit");
                if (!noEdit && !(editControl instanceof HTMLElement)) {
                    editControl = createPlaceholderButton("edit", "Duzenle");
                }

                const noDelete = table.hasAttribute("data-grid-no-delete");
                if (!noDelete && !(deleteControl instanceof HTMLElement)) {
                    deleteControl = createPlaceholderButton("delete", "Sil");
                }

                const actionItems = [
                    noEdit ? null : editControl,
                    noDelete ? null : deleteControl,
                    ...otherControls
                ];
                actionContainer.replaceChildren(...actionItems.filter(Boolean));
            });
        };

        const enhanceGridActions = (scope) => {
            const rootElement = scope instanceof Document || scope instanceof HTMLElement
                ? scope
                : document;

            if (rootElement instanceof HTMLTableElement && rootElement.matches(".integrator-table, .calibra-grid")) {
                ensureActionColumn(rootElement);
            }

            rootElement.querySelectorAll(".integrator-table, .calibra-grid").forEach((table) => {
                ensureActionColumn(table);
            });

            rootElement.querySelectorAll(".integrator-table tbody button.btn, .integrator-table tbody a.btn, .calibra-grid tbody button.btn, .calibra-grid tbody a.btn").forEach((button) => {
                enhanceGridAction(button);
            });
        };

        enhanceGridActions(document);

        if (typeof MutationObserver === "undefined" || !(document.body instanceof HTMLElement)) {
            return;
        }

        const observer = new MutationObserver((mutations) => {
            mutations.forEach((mutation) => {
                mutation.addedNodes.forEach((node) => {
                    if (!(node instanceof HTMLElement)) {
                        return;
                    }

                    if (node.matches(".integrator-table tbody button.btn, .integrator-table tbody a.btn, .calibra-grid tbody button.btn, .calibra-grid tbody a.btn")) {
                        enhanceGridAction(node);
                    }

                    enhanceGridActions(node);
                });
            });
        });

        observer.observe(document.body, { childList: true, subtree: true });
    };

    const setupDeleteConfirmModal = () => {
        const modalElement = document.querySelector("[data-delete-confirm-modal]");
        const confirmButton = modalElement?.querySelector("[data-delete-confirm-submit]");
        const titleElement = modalElement?.querySelector("[data-delete-confirm-title]");
        const textElement = modalElement?.querySelector("[data-delete-confirm-text]");
        const recordElement = modalElement?.querySelector("[data-delete-confirm-record]");

        if (!(modalElement instanceof HTMLElement) ||
            !(confirmButton instanceof HTMLButtonElement) ||
            !(titleElement instanceof HTMLElement) ||
            !(textElement instanceof HTMLElement) ||
            !(recordElement instanceof HTMLElement) ||
            !window.bootstrap ||
            !window.bootstrap.Modal) {
            return;
        }

        const modalInstance = window.bootstrap.Modal.getOrCreateInstance(modalElement);
        const defaultTitle = titleElement.textContent?.trim() || "Kayit silinsin mi?";
        const defaultText = textElement.textContent?.trim() || "Bu islem geri alinamaz. Silmek uzere oldugunuz kayit:";
        let pendingForm = null;

        const resolveFieldValue = (fieldName) => {
            const candidates = Array.from(document.getElementsByName(fieldName));
            const field = candidates.find((candidate) =>
                candidate instanceof HTMLInputElement ||
                candidate instanceof HTMLSelectElement ||
                candidate instanceof HTMLTextAreaElement);

            if (field instanceof HTMLSelectElement) {
                return field.selectedOptions[0]?.text?.trim() || "";
            }

            if (field instanceof HTMLInputElement || field instanceof HTMLTextAreaElement) {
                return field.value?.trim() || "";
            }

            return "";
        };

        const resolveRecordLabel = (form, submitter) => {
            const explicitRecord = submitter?.getAttribute("data-delete-record")
                || form.getAttribute("data-delete-record");
            if (explicitRecord?.trim()) {
                return explicitRecord.trim();
            }

            const rawFieldList = submitter?.getAttribute("data-delete-record-fields")
                || form.getAttribute("data-delete-record-fields")
                || "";

            const values = rawFieldList
                .split(",")
                .map((value) => value.trim())
                .filter(Boolean)
                .map(resolveFieldValue)
                .filter(Boolean);

            if (values.length > 0) {
                return values.join(" - ");
            }

            return submitter?.getAttribute("data-delete-record-fallback")
                || form.getAttribute("data-delete-record-fallback")
                || "-";
        };

        const resetModal = () => {
            pendingForm = null;
            titleElement.textContent = defaultTitle;
            textElement.textContent = defaultText;
            recordElement.textContent = "-";
        };

        document.addEventListener("submit", (event) => {
            const form = event.target;
            if (!(form instanceof HTMLFormElement) || form.getAttribute("data-delete-confirm") !== "1") {
                return;
            }

            event.preventDefault();

            const submitter = event.submitter instanceof HTMLElement ? event.submitter : null;
            const title = submitter?.getAttribute("data-delete-title")
                || form.getAttribute("data-delete-title")
                || defaultTitle;
            const text = submitter?.getAttribute("data-delete-message")
                || form.getAttribute("data-delete-message")
                || defaultText;
            const record = resolveRecordLabel(form, submitter);

            pendingForm = form;
            titleElement.textContent = title;
            textElement.textContent = text;
            recordElement.textContent = record;
            modalInstance.show();
        });

        confirmButton.addEventListener("click", () => {
            if (!(pendingForm instanceof HTMLFormElement)) {
                return;
            }

            const targetForm = pendingForm;
            pendingForm = null;
            modalInstance.hide();
            targetForm.submit();
        });

        modalElement.addEventListener("hidden.bs.modal", resetModal);
    };

    const setupInlineEditNavigation = () => {
        const isEditTarget = (el) => {
            if (el.closest(".integrator-grid-action-btn--edit")) return true;
            const a = el.closest("a[href]");
            if (!a) return false;
            const rawLabel = (
                a.getAttribute("aria-label") ||
                a.getAttribute("title") ||
                a.getAttribute("data-grid-action-label") ||
                (a.textContent || "")
            ).trim().toLowerCase();
            if (!rawLabel.includes("duzenle")) return false;
            return !!(
                a.closest(".calibra-grid-actions-first, .integrator-row-menu__list, .material-row-actions") ||
                a.dataset.gridActionEnhanced === "1"
            );
        };

        const getEditUrl = (el) => {
            const a = el.closest("a[href]");
            return a ? a.href : null;
        };

        const reinitContent = () => {
            setupEntityLayoutTabs();
            setupEntityFormToolbars();
            setupDeleteConfirmModal();
            setupCompactGridActions();
            setupInteractiveGrids();
        };

        const executePageScripts = (doc) => {
            doc.querySelectorAll("body script:not([src])").forEach((s) => {
                // Skip workspace-shell infrastructure scripts, but allow
                // view-specific scripts inside workspace panels.
                if (s.closest("[data-workspace-shell]") && !s.closest("[data-workspace-panel]")) return;
                const el = document.createElement("script");
                el.textContent = s.textContent;
                document.body.appendChild(el);
                document.body.removeChild(el);
            });
        };

        const loadEdit = (url, isPopState) => {
            const activePanel = document.querySelector(".workspace-panel--native.is-active");
            if (!activePanel) { window.location.assign(url); return; }

            fetch(url, { credentials: "same-origin" })
                .then((r) => { if (!r.ok) throw new Error("fetch-error"); return r.text(); })
                .then((html) => {
                    const parser = new DOMParser();
                    const doc = parser.parseFromString(html, "text/html");
                    const newPanel = doc.querySelector(".workspace-panel--native");
                    if (!newPanel) { window.location.assign(url); return; }

                    activePanel.innerHTML = newPanel.innerHTML;
                    if (activePanel.dataset.workspaceUrl !== undefined) {
                        activePanel.dataset.workspaceUrl = url;
                    }

                    if (!isPopState) {
                        history.pushState({ calibraInlineEdit: url }, "", url);
                    }

                    reinitContent();
                    executePageScripts(doc);

                    const firstField = activePanel.querySelector(
                        ".integrator-card--form input:not([type='hidden']):not([readonly]), .integrator-card--form select"
                    );
                    if (firstField) firstField.focus();
                })
                .catch(() => window.location.assign(url));
        };

        document.addEventListener("click", (e) => {
            if (!isEditTarget(e.target)) return;
            const url = getEditUrl(e.target);
            if (!url) return;
            e.preventDefault();
            loadEdit(url, false);
        });

        // Intercept "Yeni" anchor clicks inside entity-form-toolbar for inline navigation
        document.addEventListener("click", (e) => {
            const a = e.target.closest("a[href]");
            if (!a) return;
            if (!a.closest(".entity-form-toolbar")) return;
            if ((a.textContent || "").trim().toLowerCase() !== "yeni") return;
            const activePanel = document.querySelector(".workspace-panel--native.is-active");
            if (!activePanel) return;
            e.preventDefault();
            loadEdit(a.href, false);
        });

        // Intercept entity form submissions for inline save → new-record navigation
        document.addEventListener("submit", (e) => {
            const form = e.target;
            if (!(form instanceof HTMLFormElement)) return;
            if ((form.method || "get").toUpperCase() !== "POST") return;

            const activePanel = document.querySelector(".workspace-panel--native.is-active");
            if (!activePanel || !activePanel.contains(form)) return;

            // Only intercept forms that belong to an entity edit card (have a toolbar inside or are inside entity-form-card)
            const hasToolbar =
                form.querySelector(".entity-form-toolbar") !== null ||
                form.closest(".entity-form-card") !== null;
            if (!hasToolbar) return;

            e.preventDefault();

            const formData = new FormData(form);
            const action = form.action || location.href;

            fetch(action, {
                method: "POST",
                body: formData,
                credentials: "same-origin",
                redirect: "follow",
            })
                .then((r) => {
                    if (r.redirected) {
                        loadEdit(r.url, false);
                    } else {
                        return r.text().then((html) => {
                            const parser = new DOMParser();
                            const doc = parser.parseFromString(html, "text/html");
                            const newPanel = doc.querySelector(".workspace-panel--native");
                            if (!newPanel) { window.location.assign(action); return; }
                            activePanel.innerHTML = newPanel.innerHTML;
                            reinitContent();
                            executePageScripts(doc);
                        });
                    }
                })
                .catch(() => window.location.assign(action));
        });

        // Intercept GET form submissions inside the active native panel
        // (e.g. grid pager page-size change, filter forms)
        // In iframe: activePanel is null → interceptor skips → setupWorkspaceFrameNavigation
        // has already rewritten form.action with ?workspace=1 → natural submit navigates correctly.
        document.addEventListener("submit", (e) => {
            const form = e.target;
            if (!(form instanceof HTMLFormElement)) return;
            if ((form.method || "get").toUpperCase() !== "GET") return;

            const activePanel = document.querySelector(".workspace-panel--native.is-active");
            if (!activePanel || !activePanel.contains(form)) return;

            e.preventDefault();
            try {
                const u = new URL(form.action, location.href);
                new FormData(form).forEach((v, k) => u.searchParams.set(k, String(v)));
                loadEdit(u.toString(), false);
            } catch {
                window.location.assign(form.action);
            }
        }, true);

        window.addEventListener("popstate", (e) => {
            if (e.state && e.state.calibraInlineEdit) {
                loadEdit(location.href, true);
            }
        });

        // Expose loadEdit globally so inline scripts (e.g. _GridPager) can use it
        window.__calibraNav = { loadEdit };

        // Intercept ALL link clicks inside the active native panel — prevents new tabs from opening
        document.addEventListener("click", (e) => {
            const a = e.target.closest("a[href]");
            if (!a) return;

            // Skip links with explicit external target
            const target = (a.getAttribute("target") || "").toLowerCase();
            if (target && target !== "_self") return;

            // Skip download links and non-navigable hrefs
            if (a.hasAttribute("download")) return;
            const href = a.href || "";
            if (!href) return;
            try {
                const u = new URL(href);
                if (u.origin !== location.origin) return;
                if (!["http:", "https:"].includes(u.protocol)) return;
                // Skip pure fragment-only navigation
                if (u.pathname === location.pathname && u.search === location.search && u.hash) return;
            } catch { return; }

            // Only intercept links inside the active workspace panel
            const activePanel = document.querySelector(".workspace-panel--native.is-active");
            if (!activePanel || !activePanel.contains(a)) return;

            e.preventDefault();
            loadEdit(href, false);
        }, true); // capture phase — fires before inline onclick handlers
    };

    setupToastSystem();
    window.initializeValidationToasts = setupValidationToasts;
    setupValidationToasts();
    setupBrowserSuggestionSuppression();
    setupHomeFavorites();
    setupWorkspaceFrameNavigation();
    setupWorkspaceTabs();
    setupFormDrafts();
    setupCollapsibleCards();
    setupEntityFormToolbars();
    setupWorkspaceActionBarProxy();
    setupEntityLayoutTabs();
    setupCollaborationProxyButtons();
    setupGridSearchAutoSubmit();
    setupCompactGridActions();
    setupInteractiveGrids();
    setupDeleteConfirmModal();
    setupInlineEditNavigation();

    // Tarayıcı standart sağ tık menüsünü engelle (özel context menu olan gridler hariç)
    document.addEventListener("contextmenu", (e) => {
        // Input/textarea'da sağ tık izin ver (yazım denetimi vs.)
        if (e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement) return;
        // Özel context menu olan grid tabloları kendi handler'ları ile yönetir
        if (e.target.closest("[data-allow-context-menu]")) return;
        e.preventDefault();
    });

    document.querySelectorAll("[data-menu-root]").forEach((menuRoot) => {
        setupFavorites(menuRoot);
        setupMenuSearch(menuRoot);
    });

    // Açık Sayfalar Modal Event Bağlamaları
    const setupOpenWindowsManager = () => {
        const modalEl = document.getElementById('openWindowsModal');

        const getModal = () => {
            if (!modalEl || !window.bootstrap?.Modal) return null;
            // Move to body once to escape z-index traps from ancestor stacking contexts
            if (modalEl.parentNode !== document.body) {
                document.body.appendChild(modalEl);
            }
            return bootstrap.Modal.getOrCreateInstance(modalEl);
        };

        const btnOpen = document.getElementById("btnOpenWindowsModal");
        if (btnOpen instanceof HTMLElement) {
            btnOpen.addEventListener("click", (e) => {
                e.preventDefault();
                e.stopPropagation();
                // Refresh modal list with the latest tab state before showing
                if (_workspaceTabsApi) _workspaceTabsApi.refreshModalList();
                getModal()?.show();
            });
        }

        const btnCloseAll = document.getElementById("btnActiveCloseAll");
        if (btnCloseAll instanceof HTMLElement) {
            btnCloseAll.addEventListener("click", () => {
                // Directly operate on the data model — no DOM button simulation
                if (_workspaceTabsApi) {
                    _workspaceTabsApi.closeAll();
                } else {
                    // Fallback: navigate home
                    window.location.replace("/");
                }
                getModal()?.hide();
            });
        }
    };
    setupOpenWindowsManager();
})();
