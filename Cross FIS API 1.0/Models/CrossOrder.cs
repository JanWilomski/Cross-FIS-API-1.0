
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cross_FIS_API_1._0.Models
{
    public class CrossOrder
    {
        public string InstrumentId { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public string BuyerAccount { get; set; }
        public string SellerAccount { get; set; }
    }
}
