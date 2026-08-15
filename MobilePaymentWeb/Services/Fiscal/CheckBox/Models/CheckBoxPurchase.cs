using System.Text.Json.Serialization;

namespace MobilePaymentWeb.Services.Fiscal.CheckBox.Models
{
    internal class CheckBoxPurchase
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [JsonPropertyName("goods")]
        public List<CheckBoxGoods> Goods { get; set; } = new();

        [JsonPropertyName("payments")]
        public List<CheckBoxPayment> Payments { get; set; } = new();
    }
}
