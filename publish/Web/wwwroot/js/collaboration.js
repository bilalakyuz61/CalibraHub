(() => {
    const config = window.calibraCollaborationConfig;
    if (!config || config.enabled !== true || !window.signalR) {
        return;
    }

    const currentUserId = typeof config.currentUserId === "string" ? config.currentUserId.trim().toLowerCase() : "";
    if (!currentUserId) {
        return;
    }

    const root = document.body;
    const sessionStorageKey = "calibra.collaboration.session";
    const conversationsStorageKey = "calibra.collaboration.conversations";
    const unreadStorageKey = "calibra.collaboration.unread";
    const heartbeatIntervalMs = 15000;

    const widget = document.querySelector("[data-collaboration-widget]");
    const toggleButton = widget?.querySelector("[data-collaboration-toggle]") ?? null;
    const closeButton = widget?.querySelector("[data-collaboration-close]") ?? null;
    const panel = widget?.querySelector("[data-collaboration-panel]") ?? null;
    const unreadBadge = widget?.querySelector("[data-collaboration-unread]") ?? null;
    const statusNode = widget?.querySelector("[data-collaboration-status]") ?? null;
    const usersHost = widget?.querySelector("[data-collaboration-users]") ?? null;
    const usersEmptyState = widget?.querySelector("[data-collaboration-users-empty]") ?? null;
    const threadTitle = widget?.querySelector("[data-collaboration-thread-title]") ?? null;
    const messagesHost = widget?.querySelector("[data-collaboration-messages]") ?? null;
    const messagesEmptyState = widget?.querySelector("[data-collaboration-messages-empty]") ?? null;
    const composeForm = widget?.querySelector("[data-collaboration-form]") ?? null;
    const messageInput = widget?.querySelector("[data-collaboration-input]") ?? null;

    const readJson = (key, fallback) => {
        try {
            const raw = window.sessionStorage.getItem(key);
            return raw ? JSON.parse(raw) : fallback;
        } catch {
            return fallback;
        }
    };

    const writeJson = (key, value) => {
        try {
            window.sessionStorage.setItem(key, JSON.stringify(value));
        } catch {
            // Ignore storage failures.
        }
    };

    const getOrCreateSessionId = () => {
        try {
            const existing = window.sessionStorage.getItem(sessionStorageKey);
            if (existing) {
                return existing;
            }

            const nextValue = typeof crypto?.randomUUID === "function"
                ? crypto.randomUUID()
                : `session-${Date.now()}-${Math.round(Math.random() * 100000)}`;

            window.sessionStorage.setItem(sessionStorageKey, nextValue);
            return nextValue;
        } catch {
            return `session-${Date.now()}`;
        }
    };

    const sessionId = getOrCreateSessionId();
    const state = {
        selectedUserId: null,
        panelOpen: false,
        hadDisconnect: false,
        onlineUsers: [],
        trackedForms: [],
        conversations: readJson(conversationsStorageKey, {}),
        unreadByUser: readJson(unreadStorageKey, {})
    };

    const ui = {
        noUsers: config.ui?.noUsers ?? "Aktif kullanici bulunmuyor.",
        noConversation: config.ui?.noConversation ?? "Mesajlasmak icin bir kullanici secin.",
        messagePlaceholder: config.ui?.messagePlaceholder ?? "Mesajinizi yazin...",
        statusConnected: config.ui?.statusConnected ?? "Canli baglanti hazir",
        statusConnecting: config.ui?.statusConnecting ?? "Baglanti kuruluyor",
        statusDisconnected: config.ui?.statusDisconnected ?? "Baglanti kesildi",
        lockOwned: config.ui?.lockOwned ?? "Bu kaydi su anda siz duzenliyorsunuz.",
        lockReadonly: config.ui?.lockReadonly ?? "Bu kayit su anda {user} tarafindan duzenlenmektedir.",
        requestUnlock: config.ui?.requestUnlock ?? "Lutfen {record} kaydini kapatir misin?",
        messageUser: config.ui?.messageUser ?? "Mesaj Gonder",
        selectUser: config.ui?.selectUser ?? "Kullanici Secin",
        incomingToast: config.ui?.incomingToast ?? "{user} size yeni bir mesaj gonderdi.",
        connectionLostToast: config.ui?.connectionLostToast ?? "Canli isbirligi baglantisi kesildi.",
        connectionRestoredToast: config.ui?.connectionRestoredToast ?? "Canli isbirligi baglantisi tekrar kuruldu."
    };

    const formatTemplate = (template, replacements) => Object.entries(replacements ?? {}).reduce(
        (result, [key, value]) => result.replaceAll(`{${key}}`, value ?? ""),
        template ?? "");

    const normalizeToken = (value) => {
        const normalized = typeof value === "string" ? value.trim() : "";
        if (!normalized) {
            return "";
        }

        return normalized
            .replace(/([a-z0-9])([A-Z])/g, "$1-$2")
            .replace(/[^a-zA-Z0-9]+/g, "-")
            .replace(/-{2,}/g, "-")
            .replace(/^-|-$/g, "")
            .toLowerCase();
    };

    const getActionName = (form) => {
        try {
            const url = new URL(form.action, window.location.origin);
            const actionName = url.pathname.split("/").filter(Boolean).pop() ?? "record";
            return actionName
                .replace(/^Save/i, "")
                .replace(/^Update/i, "")
                .replace(/^Delete/i, "")
                .replace(/^Test/i, "test-");
        } catch {
            return "record";
        }
    };

    const resolveFormTitle = (form) => {
        const card = form.closest(".integrator-card, article, section");
        const titleElement = card?.querySelector(".integrator-collapsible-card__title, .integrator-card__header h2, h2, h3, h1");
        return titleElement?.textContent?.trim() || root.getAttribute("data-page-title") || "Kayit";
    };

    const scoreHiddenInput = (input) => {
        const name = (input.getAttribute("name") ?? "").toLowerCase();
        let score = 0;

        if (name.endsWith(".id") || name === "id") {
            score += 120;
        }
        if (name.includes("stockcardid")) {
            score += 110;
        }
        if (name.includes("groupid") || name.includes("fieldid")) {
            score += 95;
        }
        if (name.startsWith("selected") && name.endsWith("id")) {
            score += 70;
        }
        if (name.includes("companyid") || name.includes("departmentid") || name.includes("supervisoruserid") || name.includes("parentid")) {
            score -= 35;
        }

        return score;
    };

    const findRecordReference = (form) => {
        const explicitType = form.dataset.collaborationRecordType?.trim();
        const explicitId = form.dataset.collaborationRecordId?.trim();
        if (explicitType && explicitId) {
            return {
                recordType: normalizeToken(explicitType),
                recordId: explicitId,
                recordTitle: form.dataset.collaborationRecordTitle?.trim() || resolveFormTitle(form)
            };
        }

        const hiddenInputs = Array.from(form.querySelectorAll("input[type='hidden'][name]"))
            .filter((input) => input instanceof HTMLInputElement)
            .filter((input) => (input.value ?? "").trim().length > 0);

        if (hiddenInputs.length === 0) {
            return null;
        }

        hiddenInputs.sort((left, right) => scoreHiddenInput(right) - scoreHiddenInput(left));
        const primaryInput = hiddenInputs[0];
        if (!(primaryInput instanceof HTMLInputElement)) {
            return null;
        }

        const recordId = primaryInput.value.trim();
        if (!recordId) {
            return null;
        }

        return {
            recordType: normalizeToken(getActionName(form) || primaryInput.name || "record"),
            recordId,
            recordTitle: resolveFormTitle(form)
        };
    };

    const ensureBanner = (context) => {
        if (context.banner instanceof HTMLElement) {
            return context.banner;
        }

        const banner = document.createElement("div");
        banner.className = "collaboration-lock-banner";
        banner.hidden = true;

        const body = document.createElement("div");
        body.className = "collaboration-lock-banner__body";

        const status = document.createElement("span");
        status.className = "collaboration-lock-banner__status";

        const message = document.createElement("div");
        message.className = "collaboration-lock-banner__message";
        body.append(status, message);

        const actions = document.createElement("div");
        actions.className = "collaboration-lock-banner__actions";

        const messageButton = document.createElement("button");
        messageButton.type = "button";
        messageButton.className = "btn btn-outline-primary btn-sm";
        messageButton.setAttribute("data-collaboration-allow", "1");
        messageButton.textContent = ui.messageUser;
        messageButton.hidden = true;
        messageButton.addEventListener("click", () => {
            if (!context.lockInfo) {
                return;
            }

            const recordLabel = (context.recordTitle || "kayit").toLowerCase();
            openConversationWith(
                String(context.lockInfo.ownerUserId ?? "").toLowerCase(),
                formatTemplate(ui.requestUnlock, { record: recordLabel }));
        });

        actions.append(messageButton);
        banner.append(body, actions);

        const toolbar = context.card?.querySelector(".entity-form-toolbar");
        if (toolbar instanceof HTMLElement) {
            toolbar.appendChild(banner);
        } else {
            context.form.insertAdjacentElement("beforebegin", banner);
        }

        context.banner = banner;
        context.bannerStatus = status;
        context.bannerMessage = message;
        context.bannerMessageButton = messageButton;
        return banner;
    };

    const getInteractiveElements = (context) => {
        const formControls = Array.from(context.form.querySelectorAll("input, select, textarea, button"))
            .filter((element) => {
                if (!(element instanceof HTMLElement)) {
                    return false;
                }
                if (element instanceof HTMLInputElement && element.type === "hidden") {
                    return false;
                }
                return !element.hasAttribute("data-collaboration-allow");
            });

        const toolbarControls = Array.from((context.card ?? context.form).querySelectorAll(".entity-form-toolbar button, .entity-form-toolbar input"))
            .filter((element) => element instanceof HTMLElement && !element.hasAttribute("data-collaboration-allow"));

        return [...formControls, ...toolbarControls];
    };

    const setReadOnlyState = (context, readOnly) => {
        getInteractiveElements(context).forEach((element) => {
            if (!(element instanceof HTMLInputElement ||
                element instanceof HTMLButtonElement ||
                element instanceof HTMLSelectElement ||
                element instanceof HTMLTextAreaElement)) {
                return;
            }

            if (readOnly) {
                if (!element.disabled) {
                    element.dataset.collaborationDisabledByLock = "1";
                    element.disabled = true;
                }
                return;
            }

            if (element.dataset.collaborationDisabledByLock === "1") {
                element.disabled = false;
                delete element.dataset.collaborationDisabledByLock;
            }
        });
    };

    const applyLockState = (context, lockInfo) => {
        context.lockInfo = lockInfo ?? null;
        const banner = ensureBanner(context);

        if (!lockInfo) {
            banner.hidden = true;
            banner.classList.remove("is-owned", "is-readonly");
            if (context.bannerMessageButton) {
                context.bannerMessageButton.hidden = true;
            }
            context.lockMode = "none";
            setReadOnlyState(context, false);
            return;
        }

        const ownerUserId = String(lockInfo.ownerUserId ?? "").toLowerCase();
        banner.hidden = false;

        if (ownerUserId === currentUserId) {
            context.lockMode = "owned";
            banner.hidden = true;
            banner.classList.remove("is-owned", "is-readonly");
            if (context.bannerMessageButton) {
                context.bannerMessageButton.hidden = true;
            }
            setReadOnlyState(context, false);
            return;
        }

        banner.classList.add("is-readonly");
        banner.classList.remove("is-owned");
        if (context.bannerStatus) {
            context.bannerStatus.textContent = "Salt Okunur";
        }
        if (context.bannerMessage) {
            context.bannerMessage.textContent = formatTemplate(ui.lockReadonly, {
                user: lockInfo.ownerDisplayName ?? "Kullanici"
            });
        }
        if (context.bannerMessageButton) {
            context.bannerMessageButton.hidden = false;
        }
        context.lockMode = "readonly";
        setReadOnlyState(context, true);
    };

    const buildTrackedForms = () => Array.from(document.querySelectorAll("form.integrator-form"))
        .filter((form) => !form.closest(".integrator-inline-form"))
        .map((form) => {
            if (!(form instanceof HTMLFormElement)) {
                return null;
            }

            const reference = findRecordReference(form);
            if (!reference?.recordType || !reference.recordId) {
                return null;
            }

            const context = {
                form,
                card: form.closest(".integrator-card, article"),
                recordType: reference.recordType,
                recordId: reference.recordId,
                recordTitle: reference.recordTitle,
                lockInfo: null,
                lockMode: "none",
                acquireInFlight: false,
                banner: null,
                bannerStatus: null,
                bannerMessage: null,
                bannerMessageButton: null
            };

            const acquireIfNeeded = () => {
                if (context.lockMode === "owned" || context.lockMode === "readonly" || context.acquireInFlight) {
                    return;
                }
                acquireLock(context);
            };

            form.addEventListener("focusin", acquireIfNeeded);
            form.addEventListener("input", acquireIfNeeded);
            form.addEventListener("change", acquireIfNeeded);
            return context;
        })
        .filter((context) => context !== null);

    const persistState = () => {
        writeJson(conversationsStorageKey, state.conversations);
        writeJson(unreadStorageKey, state.unreadByUser);
    };

    const getConversationKey = (userId) => String(userId ?? "").toLowerCase();

    const updateUnreadBadge = () => {
        if (!(unreadBadge instanceof HTMLElement)) {
            return;
        }

        const unreadTotal = Object.values(state.unreadByUser)
            .map((value) => Number(value) || 0)
            .reduce((sum, value) => sum + value, 0);

        unreadBadge.hidden = unreadTotal <= 0;
        unreadBadge.textContent = unreadTotal.toString();
    };

    const renderThreadTitle = () => {
        if (!(threadTitle instanceof HTMLElement)) {
            return;
        }

        if (!state.selectedUserId) {
            threadTitle.textContent = ui.selectUser;
            return;
        }

        const selectedUser = state.onlineUsers.find((user) => String(user.userId ?? "").toLowerCase() === state.selectedUserId);
        threadTitle.textContent = selectedUser?.displayName ?? ui.selectUser;
    };

    const renderMessages = () => {
        if (!(messagesHost instanceof HTMLElement) || !(messagesEmptyState instanceof HTMLElement)) {
            return;
        }

        messagesHost.replaceChildren();

        if (!state.selectedUserId) {
            messagesEmptyState.hidden = false;
            messagesEmptyState.textContent = ui.noConversation;
            return;
        }

        const conversationKey = getConversationKey(state.selectedUserId);
        const messages = Array.isArray(state.conversations[conversationKey])
            ? state.conversations[conversationKey]
            : [];

        if (messages.length === 0) {
            messagesEmptyState.hidden = false;
            messagesEmptyState.textContent = ui.noConversation;
            return;
        }

        messagesEmptyState.hidden = true;
        messages.forEach((message) => {
            const item = document.createElement("article");
            const isOutgoing = String(message.senderUserId ?? "").toLowerCase() === currentUserId;
            item.className = `collaboration-message${isOutgoing ? " is-outgoing" : ""}`;

            const meta = document.createElement("div");
            meta.className = "collaboration-message__meta";
            meta.textContent = isOutgoing ? "Siz" : (message.senderDisplayName ?? "Kullanici");

            const text = document.createElement("div");
            text.className = "collaboration-message__text";
            text.textContent = message.message ?? "";

            item.append(meta, text);
            messagesHost.appendChild(item);
        });

        messagesHost.scrollTop = messagesHost.scrollHeight;
    };

    const selectUser = (userId, draftMessage) => {
        state.selectedUserId = String(userId ?? "").toLowerCase() || null;
        if (state.selectedUserId) {
            state.unreadByUser[getConversationKey(state.selectedUserId)] = 0;
            persistState();
        }

        if (messageInput instanceof HTMLTextAreaElement) {
            messageInput.disabled = !state.selectedUserId;
            if (draftMessage) {
                messageInput.value = draftMessage;
                messageInput.focus();
            }
        }

        renderUsers();
        renderThreadTitle();
        renderMessages();
        updateUnreadBadge();
    };

    const openPanel = () => {
        state.panelOpen = true;
        if (widget instanceof HTMLElement) {
            widget.hidden = false;
        }
        if (panel instanceof HTMLElement) {
            panel.hidden = false;
        }
        if (toggleButton instanceof HTMLButtonElement) {
            toggleButton.setAttribute("aria-expanded", "true");
        }
        root.classList.add("collaboration-panel-open");
    };

    const closePanel = () => {
        state.panelOpen = false;
        if (panel instanceof HTMLElement) {
            panel.hidden = true;
        }
        if (toggleButton instanceof HTMLButtonElement) {
            toggleButton.setAttribute("aria-expanded", "false");
        }
        root.classList.remove("collaboration-panel-open");
    };

    const openConversationWith = (userId, draftMessage) => {
        openPanel();
        selectUser(userId, draftMessage);
    };

    const renderUsers = () => {
        if (!(usersHost instanceof HTMLElement) || !(usersEmptyState instanceof HTMLElement)) {
            return;
        }

        usersHost.replaceChildren();
        const users = state.onlineUsers.filter((user) => String(user.userId ?? "").toLowerCase() !== currentUserId);

        usersEmptyState.hidden = users.length > 0;
        if (users.length === 0) {
            usersEmptyState.textContent = ui.noUsers;
            return;
        }

        users.forEach((user) => {
            const userId = String(user.userId ?? "").toLowerCase();
            const item = document.createElement("button");
            item.type = "button";
            item.className = `collaboration-user${state.selectedUserId === userId ? " is-active" : ""}`;
            item.addEventListener("click", () => {
                openPanel();
                selectUser(userId);
            });

            const title = document.createElement("div");
            title.className = "collaboration-user__title";
            title.textContent = user.displayName ?? "Kullanici";

            const meta = document.createElement("div");
            meta.className = "collaboration-user__meta";
            meta.textContent = `${user.connectionCount ?? 1} aktif oturum`;

            item.append(title, meta);

            const unread = Number(state.unreadByUser[getConversationKey(userId)] || 0);
            if (unread > 0) {
                const badge = document.createElement("span");
                badge.className = "collaboration-user__badge";
                badge.textContent = unread.toString();
                item.appendChild(badge);
            }

            usersHost.appendChild(item);
        });
    };

    const upsertMessage = (message) => {
        const senderUserId = String(message.senderUserId ?? "").toLowerCase();
        const recipientUserId = String(message.recipientUserId ?? "").toLowerCase();
        const otherUserId = senderUserId === currentUserId ? recipientUserId : senderUserId;
        if (!otherUserId) {
            return;
        }

        const conversationKey = getConversationKey(otherUserId);
        const currentItems = Array.isArray(state.conversations[conversationKey])
            ? state.conversations[conversationKey]
            : [];

        if (currentItems.some((item) => item.messageId === message.messageId)) {
            return;
        }

        state.conversations[conversationKey] = [...currentItems, message].slice(-60);
        const isIncoming = senderUserId !== currentUserId;
        if (isIncoming && (!state.panelOpen || state.selectedUserId !== otherUserId)) {
            state.unreadByUser[conversationKey] = Number(state.unreadByUser[conversationKey] || 0) + 1;
            if (typeof window.showToast === "function") {
                window.showToast({
                    type: "info",
                    message: formatTemplate(ui.incomingToast, { user: message.senderDisplayName ?? "Kullanici" }),
                    duration: 3500
                });
            }
        }

        persistState();
        renderUsers();
        renderMessages();
        updateUnreadBadge();
    };

    const updateConnectionStatus = (statusText) => {
        if (statusNode instanceof HTMLElement) {
            statusNode.textContent = statusText;
        }
    };

    const connection = new window.signalR.HubConnectionBuilder()
        .withUrl(`${config.hubUrl}?sessionId=${encodeURIComponent(sessionId)}`)
        .withAutomaticReconnect([0, 2000, 5000, 10000])
        .build();

    const matchesContext = (context, payload) =>
        context.recordType === String(payload.recordType ?? "").toLowerCase() &&
        context.recordId.toLowerCase() === String(payload.recordId ?? "").toLowerCase();

    const handleRecordLockChanged = (payload) => {
        state.trackedForms.forEach((context) => {
            if (matchesContext(context, payload)) {
                applyLockState(context, payload.lock ?? null);
            }
        });
    };

    const subscribeToRecords = async () => {
        for (const context of state.trackedForms) {
            try {
                const snapshot = await connection.invoke("WatchRecord", {
                    recordType: context.recordType,
                    recordId: context.recordId
                });
                applyLockState(context, snapshot?.lock ?? null);
            } catch {
                // Ignore transient realtime errors.
            }
        }
    };

    const sendHeartbeat = async () => {
        if (connection.state !== window.signalR.HubConnectionState.Connected) {
            return;
        }

        const records = state.trackedForms
            .filter((context) => context.lockMode === "owned")
            .map((context) => ({
                recordType: context.recordType,
                recordId: context.recordId
            }));

        try {
            await connection.send("Heartbeat", { sessionId, records });
        } catch {
            // Ignore transient realtime errors.
        }
    };

    const acquireLock = async (context) => {
        if (connection.state !== window.signalR.HubConnectionState.Connected) {
            return;
        }

        context.acquireInFlight = true;
        try {
            const result = await connection.invoke("AcquireRecordLock", {
                sessionId,
                recordType: context.recordType,
                recordId: context.recordId,
                recordTitle: context.recordTitle,
                pageUrl: window.location.pathname + window.location.search
            });
            applyLockState(context, result?.lock ?? null);
        } catch {
            // Ignore transient realtime errors.
        } finally {
            context.acquireInFlight = false;
        }
    };

    const releaseOwnedLocks = () => {
        const records = state.trackedForms
            .filter((context) => context.lockMode === "owned")
            .map((context) => ({
                recordType: context.recordType,
                recordId: context.recordId
            }));

        if (records.length === 0) {
            return;
        }

        const payload = JSON.stringify({ sessionId, records });
        if (navigator.sendBeacon) {
            navigator.sendBeacon(config.releaseUrl, new Blob([payload], { type: "application/json" }));
            return;
        }

        window.fetch(config.releaseUrl, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: payload,
            credentials: "same-origin",
            keepalive: true
        }).catch(() => {
            // Ignore release failures.
        });
    };

    const startConnection = async () => {
        try {
            updateConnectionStatus(ui.statusConnecting);
            await connection.start();
            updateConnectionStatus(ui.statusConnected);
            if (state.hadDisconnect && typeof window.showToast === "function") {
                window.showToast({ type: "success", message: ui.connectionRestoredToast, duration: 2500 });
            }
            state.hadDisconnect = false;
            await subscribeToRecords();
            await sendHeartbeat();
        } catch {
            updateConnectionStatus(ui.statusDisconnected);
            window.setTimeout(startConnection, 5000);
        }
    };

    connection.on("presenceUpdated", (users) => {
        state.onlineUsers = Array.isArray(users) ? users : [];
        renderUsers();
        renderThreadTitle();
    });
    connection.on("recordLockChanged", handleRecordLockChanged);
    connection.on("chatMessageReceived", upsertMessage);

    connection.onreconnecting(() => {
        state.hadDisconnect = true;
        updateConnectionStatus(ui.statusConnecting);
        if (typeof window.showToast === "function") {
            window.showToast({ type: "warning", message: ui.connectionLostToast, duration: 2500 });
        }
    });

    connection.onreconnected(async () => {
        updateConnectionStatus(ui.statusConnected);
        await subscribeToRecords();
        await sendHeartbeat();
    });

    connection.onclose(() => {
        state.hadDisconnect = true;
        updateConnectionStatus(ui.statusDisconnected);
        window.setTimeout(startConnection, 5000);
    });

    if (toggleButton instanceof HTMLButtonElement) {
        toggleButton.addEventListener("click", () => {
            if (state.panelOpen) {
                closePanel();
            } else {
                openPanel();
            }
        });
    }

    if (closeButton instanceof HTMLButtonElement) {
        closeButton.addEventListener("click", closePanel);
    }

    if (composeForm instanceof HTMLFormElement) {
        composeForm.addEventListener("submit", async (event) => {
            event.preventDefault();
            if (!(messageInput instanceof HTMLTextAreaElement) || !state.selectedUserId) {
                return;
            }

            const message = messageInput.value.trim();
            if (!message || connection.state !== window.signalR.HubConnectionState.Connected) {
                return;
            }

            try {
                await connection.invoke("SendDirectMessage", {
                    recipientUserId: state.selectedUserId,
                    message
                });
                messageInput.value = "";
            } catch {
                if (typeof window.showToast === "function") {
                    window.showToast({ type: "error", message: ui.statusDisconnected, duration: 2500 });
                }
            }
        });
    }

    if (messageInput instanceof HTMLTextAreaElement) {
        messageInput.disabled = true;
        messageInput.placeholder = ui.messagePlaceholder;
    }

    if (widget instanceof HTMLElement) {
        widget.hidden = false;
    }

    state.trackedForms = buildTrackedForms();
    renderUsers();
    renderThreadTitle();
    renderMessages();
    updateUnreadBadge();
    startConnection();
    window.setInterval(sendHeartbeat, heartbeatIntervalMs);
    window.addEventListener("pagehide", releaseOwnedLocks, { capture: true });
})();
