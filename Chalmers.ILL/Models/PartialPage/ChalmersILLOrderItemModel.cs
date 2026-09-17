using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Models.PartialPage
{
    public class ChalmersILLOrderItemModel : OrderItemPageModelBase
    {
        public Dictionary<int, string> EventIdToEventNameMapping = new Dictionary<int, string>()
        {
            {1, "Snabbändring av typ"},
            {2, "Snabbändring av status"},
            {3, "Snabbändring av leveransbibliotek"},
            {4, "Referens ändrad"},
            {5, "Beställardata uppdaterad"},
            {6, "Mail skickat"},
            {7, "Beställning"},
            {8, "Manuell loggning"},
            {9, "Artikel levererad"},
            {10, "Bok mottagen"},
            {11, "Lånetid mot låntagare ändrad"},
            {12, "Lånetid från utlånande bibliotek ändrat"},
            {13, "Bok krävd"},
            {14, "Bok returnerad"},
            {15, "Automatiskt utskickat mail"},
            {16, "Dokument importerat"},
            {17, "Order skapad från Librisdata"},
            {18, "Uppdatering av order från Librisdata"},
            {19, "Tidsbaserad automatisk uppdatering av order"},
            {20, "Order skapad från maildata"},
            {21, "Mail från låntagare mottaget"},
            {22, "Mail mottaget (ej låntagare)"},
            {23, "Bok återlämnad av låntagare" },
            {24, "Bok utlånad" },
            {25, "Bok mottagen i lånedisk" },
            {26, "Artikel skickad till filial" },
            {27, "Leverantörsdata uppdaterad" },
            {28, "Snabbändring av inköpsbibliotek" },
            {29, "Artikel ankommen till filial" },
            {30, "Automatisk anonymisering av order" },
            {31, "Manuell anonymisering av order" },
            {32, "Duplicering av order." },
            {33, "Återställning av anonymiseringsflaggor." }
        };

        public ChalmersILLOrderItemModel(OrderItemModel orderItemModel) : base(orderItemModel) { }
    }
}