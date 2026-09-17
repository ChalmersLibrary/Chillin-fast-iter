using Chalmers.ILL.Configuration;
using Chalmers.ILL.Mail;
using Chalmers.ILL.MediaItems;
using Chalmers.ILL.Patron;
using Chalmers.ILL.SignalR;
using System.Collections.Generic;

namespace Chalmers.ILL.OrderItems
{
    public class ChalmersSourceFactory : ISourceFactory
    {
        IMailWebApi _exchangeMailWebApi;
        IOrderItemManager _orderItemManager;
        INotifier _notifier;
        IMediaItemManager _mediaItemManager;
        IPatronDataProvider _patronDataProvider;
        IPersonDataProvider _personDataProvider;
        IOrderItemSearcher _orderItemSearcher;
        IChillinConfiguration _config;

        public ChalmersSourceFactory(IMailWebApi exchangeMailWebApi, IOrderItemManager orderItemManager, INotifier notifier,
            IMediaItemManager mediaItemManager, IPatronDataProvider patronDataProvider, IPersonDataProvider personDataProvider,
            IOrderItemSearcher orderItemSearcher, IChillinConfiguration config)
        {
            _exchangeMailWebApi = exchangeMailWebApi;
            _orderItemManager = orderItemManager;
            _notifier = notifier;
            _mediaItemManager = mediaItemManager;
            _patronDataProvider = patronDataProvider;
            _personDataProvider = personDataProvider;
            _orderItemSearcher = orderItemSearcher;
            _config = config;
        }

        public List<ISource> Sources()
        {
            var res = new List<ISource>();
            // Libris (the only other source there ever was) was discontinued 2025-09-08 and its
            // class removed entirely (fas 10, "Beskär sökytan") - this is the one remaining source.
            res.Add(new ChalmersOrderItemsMailSource(_exchangeMailWebApi, _orderItemManager, _notifier, _mediaItemManager, _patronDataProvider, _personDataProvider, _orderItemSearcher, _config));
            return res;
        }
    }
}