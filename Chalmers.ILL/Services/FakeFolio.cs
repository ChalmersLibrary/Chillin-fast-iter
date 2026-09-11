using Chalmers.ILL.Models;
using Chalmers.ILL.Repositories;
using System;

namespace Chalmers.ILL.Services
{
    public class FakeFolio : IFolioItemService, IFolioRepository, IFolioService, IFolioInstanceService,
        IFolioHoldingService, IFolioCirculationService, IFolioUserService
    {
        public ItemQuery ByQuery(string query)
        {
            return new ItemQuery
            {
                Items = new Item[0],
                TotalRecords = 0
            };
        }

        public FolioUser ByUserName(string userName)
        {
            return new FolioUser
            {
                Id = "7312031232",
                Personal = new Personal
                {
                    FirstName = "John",
                    LastName = "Doe"
                }
            };
        }

        public void InitFolio(string title, string orderId, string barcode, string pickUpServicePoint, bool readOnlyAtLibrary, string patronCardNumber)
        {
            Console.WriteLine("INIT FOLIO");
        }

        public Item Post(ItemBasic item, bool readOnlyAtLibrary)
        {
            Console.WriteLine("POST FOLIO ITEM");

            return null;
        }

        public string Post(string path, string body)
        {
            Console.WriteLine("POST FOLIO PATH BODY");

            return "tjosan";
        }

        public Instance Post(InstanceBasic item)
        {
            Console.WriteLine("POST FOLIO INSTANCE");

            return null;
        }

        public Holding Post(HoldingBasic item)
        {
            Console.WriteLine("POST FOLIO HOLDING BASIC");

            return null;
        }

        public Circulation Post(CirculationBasic item)
        {
            Console.WriteLine("POST FOLIO CIRCULATION");

            return null;
        }

        public void Put(Item item)
        {
            Console.WriteLine("PUT FOLIO ITEM");
        }

        public string Put(string path, string body)
        {
            Console.WriteLine("PUT FOLIO PATH BODY");

            return "tjosan";
        }

        public void SetItemToWithdrawn(string id)
        {
            Console.WriteLine("FOLIO SET ITEM TO WITHDRAWN " + id);
        }

        string IFolioRepository.ByQuery(string path)
        {
            return "IFolioRepository";
        }
    }
}
