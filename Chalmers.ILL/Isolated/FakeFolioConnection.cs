using Chalmers.ILL.Connections;

namespace Chalmers.ILL.Isolated
{
    // FolioConnection's real counterpart reads four appSettings in field initializers and throws
    // NullReferenceException if they're missing - exactly the crash isolated mode exists to avoid
    // (fas 6, isolerat läge steg A). A constant, always-valid token means callers (FolioPatronDataProvider,
    // FakeFolio) never need to branch on isolation themselves.
    public class FakeFolioConnection : IFolioConnection
    {
        private const string FakeToken = "isolated-mode-fake-token";

        public void ClearToken() { }

        public bool NeedNewToken() => false;

        public bool IsRefreshTokenOk() => true;

        public void SetToken() { }

        public string GetToken() => FakeToken;

        public string GetTenant() => "isolated";

        public string GetFolioApiBaseAddress() => "https://folio.isolated.invalid";
    }
}
