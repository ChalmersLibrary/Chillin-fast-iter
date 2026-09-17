# Chillin

This is the system that is used on Chalmers Library for handling incoming inter library loans and purchases. It is highly tailored to our needs and you should not expect to be able to just take it and use it for your purposes without much hard work and many modifications. Hopefully the code can still be an interesting platform to start from or to draw inspiration from if developing something similar. We welcome constructive criticism and ideas.

## Status

This branch is mid-migration away from .NET Framework/`System.Web` to modern .NET, running on
Linux instead of Windows/IIS. See [CLAUDE.md](CLAUDE.md) for the current architecture and
[TODO-remove-dotnet-framework.md](TODO-remove-dotnet-framework.md) for what's done and what's left
(client-side asset pipeline and Azure deployment are the two big remaining pieces). The steps below
describe the current, not-yet-finished state - they will keep changing until that TODO list is
checked off.

## Prerequisites
1. [.NET SDK 10](https://dotnet.microsoft.com/download) or later.
2. For full/live mode: an [Elasticsearch](https://www.elastic.co) instance, and (for mail/patron
   lookups) Microsoft Graph and FOLIO credentials. None of these are required for isolated mode
   below.

## Setup
1. Clone this repository.
2. Build and test: `dotnet build Chalmers.ILL.Tests/Chalmers.ILL.Tests.csproj` and
   `dotnet test Chalmers.ILL.Tests/Chalmers.ILL.Tests.csproj` (see [CLAUDE.md](CLAUDE.md)'s
   Testrutiner section).
3. To run the app locally without any real Elasticsearch/mail/patron integrations, start it in
   **isolated mode**: set `Chillin__Isolated=true` and `Chillin__DataPath=<a local directory>` as
   environment variables, then `dotnet run --project Chalmers.ILL`. A `members.json` (see
   `Chalmers.ILL/Config/members.example.json` for the format - password hashes are generated with
   `Microsoft.AspNetCore.Identity.PasswordHasher<T>` in `IdentityV2` compatibility mode) and a
   `chillinPrevalues.json` (see `Chalmers.ILL/Config/chillinPrevalues.example.json`) under that data
   path are needed to log in and classify orders.
4. Full/live mode is configured through `appsettings.json`/`appsettings.{Environment}.json` rather
   than a checked-in file with real secrets - see `IChillinConfiguration` and the "Fastställda
   designbeslut" table in [TODO-remove-dotnet-framework.md](TODO-remove-dotnet-framework.md).
