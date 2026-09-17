using Chalmers.ILL.SignalR;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Chalmers.ILL.Tests.Infrastructure
{
    // Fas 9: the project had no mocking library at all before this. Existing tests keep using
    // handwritten stubs (the established pattern elsewhere in this project) - this just proves
    // Moq resolves and works under `dotnet test` on net10.0 for tests that want it going forward.
    [TestClass]
    public class MoqSmokeTest
    {
        [TestMethod]
        public void Moq_CanMockAndVerifyAnInterfaceCall()
        {
            var notifier = new Mock<INotifier>();

            notifier.Object.ReportNewOrderItemUpdate(null);

            notifier.Verify(n => n.ReportNewOrderItemUpdate(null), Times.Once);
        }
    }
}
