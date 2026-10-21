// Claudeborgen: keeps the integration's own pages (/Claude) live - included only there. ClaudeborgenHub only pings
// ("something changed"); every element with data-claude-refresh re-fetches its own fragment.
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
    // keyboards), and nothing is sent twice or empty.
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
        form.setAttribute("data-sending", "1");
        if (form.requestSubmit) {
            form.requestSubmit();
        } else {
            form.submit();
        }
    });
})();
