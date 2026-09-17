using System.Collections.Generic;
using Chalmers.ILL.Models;
using Chalmers.ILL.Repositories;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Nest;

namespace Chalmers.ILL.Tests.Repositories
{
    // Regression tests for a fas 0a latent defect: an empty "chillin_text" index (a freshly set up
    // environment, before the first Put) is a real state, not an error. The original code
    // null-dereferenced (All) or threw InvalidOperationException (ByTextField) instead of returning
    // an empty ChillinText, unlike Isolated.FileChillinTextRepository's "missing document" contract.
    [TestClass]
    public class ChillinTextRepositoryTest
    {
        [TestMethod]
        public void All_EmptyIndex_ReturnsEmptyChillinTextInsteadOfThrowing()
        {
            var client = new Mock<IElasticClient>();
            var response = new Mock<ISearchResponse<ChillinText>>();
            response.Setup(r => r.Hits).Returns(new List<IHit<ChillinText>>());
            client
                .Setup(c => c.Search(It.IsAny<System.Func<SearchDescriptor<ChillinText>, ISearchRequest>>()))
                .Returns(response.Object);

            var repository = new ChillinTextRepository(client.Object);

            var result = repository.All();

            Assert.IsNull(result.Id);
            Assert.IsNotNull(result.Source);
        }

        [TestMethod]
        public void All_DocumentPresent_ReturnsItsIdAndSource()
        {
            var source = new ChillinText { CheckInNote = "Note" };
            var hit = new Mock<IHit<ChillinText>>();
            hit.Setup(h => h.Id).Returns("1");
            hit.Setup(h => h.Source).Returns(source);

            var response = new Mock<ISearchResponse<ChillinText>>();
            response.Setup(r => r.Hits).Returns(new List<IHit<ChillinText>> { hit.Object });

            var client = new Mock<IElasticClient>();
            client
                .Setup(c => c.Search(It.IsAny<System.Func<SearchDescriptor<ChillinText>, ISearchRequest>>()))
                .Returns(response.Object);

            var repository = new ChillinTextRepository(client.Object);

            var result = repository.All();

            Assert.AreEqual("1", result.Id);
            Assert.AreSame(source, result.Source);
        }

        [TestMethod]
        public void ByTextField_EmptyIndex_ReturnsEmptyChillinTextInsteadOfThrowing()
        {
            var client = new Mock<IElasticClient>();
            var response = new Mock<ISearchResponse<ChillinText>>();
            response.Setup(r => r.Documents).Returns(new List<ChillinText>());
            client
                .Setup(c => c.Search(It.IsAny<System.Func<SearchDescriptor<ChillinText>, ISearchRequest>>()))
                .Returns(response.Object);

            var repository = new ChillinTextRepository(client.Object);

            var result = repository.ByTextField("checkInNote");

            Assert.IsNotNull(result);
            Assert.IsNull(result.CheckInNote);
        }

        [TestMethod]
        public void ByTextField_DocumentPresent_ReturnsItsSource()
        {
            var source = new ChillinText { CheckOutNote = "Note" };
            var response = new Mock<ISearchResponse<ChillinText>>();
            response.Setup(r => r.Documents).Returns(new List<ChillinText> { source });

            var client = new Mock<IElasticClient>();
            client
                .Setup(c => c.Search(It.IsAny<System.Func<SearchDescriptor<ChillinText>, ISearchRequest>>()))
                .Returns(response.Object);

            var repository = new ChillinTextRepository(client.Object);

            var result = repository.ByTextField("checkOutNote");

            Assert.AreSame(source, result);
        }
    }
}
