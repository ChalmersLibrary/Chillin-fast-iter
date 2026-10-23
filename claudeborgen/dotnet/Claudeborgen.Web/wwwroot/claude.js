// Claudeborgen: keeps the integration's parts of the page live, and sends from its chat boxes
// without reloading. ClaudeborgenHub only pings ("something changed"); every element with
// data-claude-refresh re-fetches its own fragment.
// A slow poll on top covers what no ping announces - Claude going offline is the absence of
// heartbeats, not an event.
(function () {
    var refreshing = false;
    var pending = false;

    function refreshAll() {
        if (refreshing) {
            pending = true;
            return;
        }
        refreshing = true;

        var elements = document.querySelectorAll("[data-claude-refresh]");
        var requests = Array.prototype.map.call(elements, function (el) {
            return fetch(el.getAttribute("data-claude-refresh"), { credentials: "same-origin", cache: "no-store" })
                // res.redirected: the login cookie expired and we got the login page - don't paste it in.
                .then(function (res) { return res.ok && !res.redirected ? res.text() : null; })
                .then(function (html) { if (html !== null) { el.innerHTML = html; } })
                .catch(function () { /* next ping or poll tries again */ });
        });

        Promise.all(requests).then(function () {
            refreshing = false;
            if (pending) {
                pending = false;
                refreshAll();
            }
        });
    }

    // Uses the host's SignalR client (window.signalR) if it loads one; without it, the poll below
    // alone keeps things fresh. Included from the page body, possibly before the host's
    // signalr.min.js - so wait for "load".
    window.addEventListener("load", function () {
        if (!window.signalR) {
            return;
        }
        var connection = new signalR.HubConnectionBuilder()
            .withUrl("/claudeborgenHub")
            .withAutomaticReconnect()
            .build();
        connection.on("claudeUpdate", refreshAll);
        connection.onreconnected(refreshAll);
        connection.start().catch(function () { /* the poll below still works */ });
    });

    setInterval(refreshAll, 15 * 1000);

    // Chat convention in the boxes where you talk to Claude (marked data-enter-sends): Enter sends,
    // Shift+Enter is a new line. Not while an input method is composing (e.g. accents on some
    // keyboards), and nothing is sent empty.
    document.addEventListener("keydown", function (e) {
        var box = e.target;
        if (e.key !== "Enter" || e.shiftKey || e.isComposing || !box.matches || !box.matches("textarea[data-enter-sends]")) {
            return;
        }
        e.preventDefault();
        var form = box.form;
        if (!form || box.value.trim() === "" || form.getAttribute("data-sending") === "1") {
            return;
        }
        if (form.requestSubmit) {
            form.requestSubmit(); // goes through the submit handler below
        } else {
            form.setAttribute("data-sending", "1");
            form.submit();
        }
    });

    // The chat boxes (forms marked data-claude-send) post without reloading the page: the box is
    // emptied and the live parts re-fetched, so the message and "Claude skriver…" show up in place.
    // A first message in the idea box makes the idea, and the box is swapped for that idea's. The
    // server answers the X-Claudeborgen-Send header with no redirect (ClaudeController); anything
    // unexpected - e.g. the login expired - falls back to an ordinary post, so nothing written is lost.
    document.addEventListener("submit", function (e) {
        var form = e.target;
        if (!form.matches || !form.matches("form[data-claude-send]") || !window.fetch || !window.FormData) {
            return;
        }
        e.preventDefault();
        if (form.getAttribute("data-sending") === "1") {
            return;
        }
        form.setAttribute("data-sending", "1");
        showError(form, null);

        var box = form.querySelector("textarea[name=body]");
        var button = form.querySelector("button[type=submit]");
        if (button) {
            button.disabled = true;
        }
        function done() {
            form.removeAttribute("data-sending");
            if (button) {
                button.disabled = false;
            }
        }
        function postNormally() {
            done();
            form.setAttribute("data-sending", "1");
            form.submit(); // bypasses this handler
        }

        fetch(form.action, {
            method: "POST",
            body: new FormData(form),
            credentials: "same-origin",
            headers: { "X-Claudeborgen-Send": "1" }
        }).then(function (res) {
            if (res.redirected) {
                return postNormally();
            }
            if (res.status === 204) {
                done();
                if (box) {
                    box.value = "";
                    box.focus();
                }
                refreshAll();
                return;
            }
            if (res.status === 200) {
                // The new idea's chat box (/Claude/Idea).
                return res.text().then(function (html) {
                    var holder = document.createElement("div");
                    holder.innerHTML = html;
                    var panel = holder.firstElementChild;
                    var old = form.closest("#claude-idea");
                    if (!panel || !old) {
                        return postNormally();
                    }
                    old.parentNode.replaceChild(panel, old);
                    if (history.replaceState) {
                        history.replaceState(null, "", "/?idea=" + panel.getAttribute("data-idea-id") + "#claude-idea");
                    }
                    var newBox = panel.querySelector("textarea[data-enter-sends]");
                    if (newBox) {
                        newBox.focus();
                    }
                    refreshAll();
                });
            }
            if (res.status === 409) {
                return res.text().then(function (message) {
                    done();
                    showError(form, message);
                });
            }
            postNormally();
        }).catch(function () {
            done();
            showError(form, "Meddelandet kunde inte skickas. Försök igen.");
        });
    });

    function showError(form, message) {
        var el = form.querySelector("[data-claude-send-error]");
        if (!message) {
            if (el) {
                el.parentNode.removeChild(el);
            }
            return;
        }
        if (!el) {
            el = document.createElement("div");
            el.className = "text-danger";
            el.setAttribute("data-claude-send-error", "");
            el.setAttribute("data-testid", "claude-error");
            form.insertBefore(el, form.firstChild);
        }
        el.textContent = message;
    }
})();
