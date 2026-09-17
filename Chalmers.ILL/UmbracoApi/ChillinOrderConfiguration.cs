using Chalmers.ILL.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Chalmers.ILL.UmbracoApi
{
    public class ChillinOrderConfiguration : IChillinOrderConfiguration
    {
        private readonly Dictionary<string, List<DropdownOption>> _lists;
        private readonly Dictionary<int, string> _idToValue;

        // Was AppDomain.CurrentDomain.BaseDirectory unconditionally - now takes the directory to
        // read chillinPrevalues.json from, so Bootstrapper can point it at IChillinConfiguration's
        // DataPath (fas 6, isolerat läge steg A, "Datarot").
        public ChillinOrderConfiguration(string configDirectory)
        {
            _lists = LoadLists(configDirectory);
            _idToValue = _lists.Values
                .SelectMany(l => l)
                .GroupBy(o => o.Id)
                .ToDictionary(g => g.Key, g => g.First().Value);
        }

        public List<DropdownOption> GetAvailableTypes() => Get("OrderType");
        public List<DropdownOption> GetAvailableStatuses() => Get("OrderStatus");
        public List<DropdownOption> GetAvailableDeliveryLibraries() => Get("DeliveryLibrary");
        public List<DropdownOption> GetAvailableCancellationReasons() => Get("CancellationReason");
        public List<DropdownOption> GetAvailablePurchasedMaterials() => Get("PurchasedMaterial");

        public string GetValueById(int id)
        {
            if (id == -1) return "";
            _idToValue.TryGetValue(id, out var value);
            return value ?? "";
        }

        public int GetIdByValue(string listKey, string value)
        {
            var list = Get(listKey);
            var match = list.FirstOrDefault(o => o.Value == value);
            return match?.Id ?? -1;
        }

        public void PopulateModelWithAvailableValues(OrderItemPageModelBase model)
        {
            model.AvailableTypes = GetAvailableTypes();
            model.AvailableStatuses = GetAvailableStatuses();
            model.AvailableDeliveryLibraries = GetAvailableDeliveryLibraries();
            model.AvailableCancellationReasons = GetAvailableCancellationReasons();
            model.AvailablePurchasedMaterials = GetAvailablePurchasedMaterials();
        }

        private List<DropdownOption> Get(string key)
        {
            _lists.TryGetValue(key, out var list);
            return list ?? new List<DropdownOption>();
        }

        private static Dictionary<string, List<DropdownOption>> LoadLists(string configDirectory)
        {
            var path = Path.Combine(configDirectory, "chillinPrevalues.json");
            if (!File.Exists(path))
                return EmptyConfig();

            try
            {
                var json = File.ReadAllText(path);
                var result = JsonConvert.DeserializeObject<Dictionary<string, List<DropdownOption>>>(json);
                return result ?? EmptyConfig();
            }
            catch (Exception)
            {
                return EmptyConfig();
            }
        }

        private static Dictionary<string, List<DropdownOption>> EmptyConfig() =>
            new Dictionary<string, List<DropdownOption>>
            {
                ["OrderStatus"] = new List<DropdownOption>(),
                ["OrderType"] = new List<DropdownOption>(),
                ["DeliveryLibrary"] = new List<DropdownOption>(),
                ["CancellationReason"] = new List<DropdownOption>(),
                ["PurchasedMaterial"] = new List<DropdownOption>()
            };
    }
}
