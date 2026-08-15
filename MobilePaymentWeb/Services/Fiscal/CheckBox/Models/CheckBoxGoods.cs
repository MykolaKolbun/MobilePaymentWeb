using System.Text.Json.Serialization;

namespace MobilePaymentWeb.Services.Fiscal.CheckBox.Models
{
    internal class CheckBoxGoods
    {
        [JsonPropertyName("good")]
        public CheckBoxGood Good { get; set; } = new();

        [JsonPropertyName("good_id")]
        public Guid GoodId { get; set; } = Guid.NewGuid();

        [JsonPropertyName("quantity")]
        public long Quantity { get; set; }

        [JsonPropertyName("sum")]
        public int Sum { get; set; }
    }
}
