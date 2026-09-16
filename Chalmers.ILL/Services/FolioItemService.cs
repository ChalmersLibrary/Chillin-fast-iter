using Chalmers.ILL.Configuration;
using Chalmers.ILL.Models;
using Chalmers.ILL.Repositories;
using System;

namespace Chalmers.ILL.Services
{
    public class FolioItemService : IFolioItemService
    {
        private readonly string path = "/item-storage/items";
        private readonly string withdrawPath = "/inventory/items";
        private readonly IChillinTextRepository _chillinTextRepository;
        private readonly IFolioRepository _folioRepository;
        private readonly IJsonService _jsonService;
        private readonly IFolioUserService _folioUserService;
        private readonly IChillinConfiguration _config;

        public FolioItemService
        (
            IChillinTextRepository chillinTextRepository,
            IFolioRepository folioRepository,
            IJsonService jsonService,
            IFolioUserService folioUserService,
            IChillinConfiguration config)
        {
            _folioRepository = folioRepository;
            _jsonService = jsonService;
            _chillinTextRepository = chillinTextRepository;
            _folioUserService = folioUserService;
            _config = config;
        }

        public ItemQuery ByQuery(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                throw new ArgumentNullException(nameof(query));
            }
            var response = _folioRepository.ByQuery($"{path}?query=({query})");
            return _jsonService.DeserializeObject<ItemQuery>(response);
        }

        public Item Post(ItemBasic item, bool readOnlyAtLibrary)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            var user = _folioUserService.ByUserName(_config.FolioUsername);
            var source = new Source
            {
                Id = user.Id,
                Personal = user.Personal
            };

            item.CirculationNotes.Add(new CirculationNote
            {
                NoteType = "Check in",
                Note = _chillinTextRepository.ByTextField("checkInNote").CheckInNote,
                Source = source
            });

            if (readOnlyAtLibrary)
            {
                item.CirculationNotes.Add(new CirculationNote
                {
                    NoteType = "Check out",
                    Note = _chillinTextRepository.ByTextField("checkOutNote").CheckOutNote,
                    Source = source
                });
            }       

            var response = _folioRepository.Post(path, _jsonService.SerializeObject(item));
            return _jsonService.DeserializeObject<Item>(response);
        }

        public void Put(Item item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }
            _folioRepository.Post($"{withdrawPath}/{item.Id}/mark-withdrawn", "{}");
        }
    }
}