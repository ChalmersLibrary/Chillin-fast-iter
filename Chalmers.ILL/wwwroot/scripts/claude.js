// Claudeborgen: keeps the integration's own pages (/Claude) live - included only there. ClaudeHub only pings
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

    // Included from the page body, before the layout's signalr.min.js - so wait for "load".
    window.addEventListener("load", function () {
        if (!window.signalR) {
            return;
        }
        var connection = new signalR.HubConnectionBuilder()
            .withUrl("/claudeHub")
            .withAutomaticReconnect()
            .build();
        connection.on("claudeUpdate", refreshAll);
        connection.onreconnected(refreshAll);
        connection.start().catch(function () { /* the poll below still works */ });
    });

    setInterval(refreshAll, 15 * 1000);
})();
