# @microsoft/signalr

Vendored browser build from npm `@microsoft/signalr@10.0.11` (MIT licensed), matching the app's
`net10.0` target. `signalr.min.js` is `dist/browser/signalr.min.js` from that package, unmodified,
copied in because the project does not have a client-asset build step (see fas 5 in
[TODO-remove-dotnet-framework.md](../../../../TODO-remove-dotnet-framework.md)). Replaces
`bower_components/signalr` (classic `jquery.signalR`), which required the ASP.NET Core SignalR
server migrated in fas 5.

To update: `npm pack @microsoft/signalr@<version>` and replace this file with the new package's
`dist/browser/signalr.min.js`.
