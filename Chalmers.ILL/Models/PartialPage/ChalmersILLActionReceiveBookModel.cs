namespace Chalmers.ILL.Models.PartialPage
{
    public class ChalmersILLActionReceiveBookModel : OrderItemPageModelBase
    {
        public string BookAvailableMailTemplate { get; set; }
        public string TitleInformation { get; set; }

        public ChalmersILLActionReceiveBookModel(OrderItemModel orderItemModel, string standardTitleText) : base(orderItemModel) 
        {
            TitleInformation = SetTitleInformation(orderItemModel.TitleInformation, orderItemModel.Reference, standardTitleText);
        }

        private string SetTitleInformation(string titleInformation, string reference, string text)
        {
            // text ("standardTitleText" from IChillinTextRepository) is null whenever that entry
            // hasn't been set yet - the repository returns an empty ChillinText rather than
            // throwing for a missing/empty index (see ChillinTextRepository.ByTextField). string
            // .Contains(null) throws ArgumentNullException, so without this every order with real
            // TitleInformation crashed this action until someone configured a standard title text.
            text = text ?? "";

            if (string.IsNullOrEmpty(titleInformation))
            {
                return $"{reference} {text}";
            }
            if (titleInformation.Contains(text))
            {
                return titleInformation;
            }
            return $"{titleInformation} {text}";
        }
    }
}