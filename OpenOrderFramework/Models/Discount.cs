using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenOrderFramework.Models
{
    public class Discount
    {
        [Key]
        public string Code { get; set; }
        public string Description { get; set; }
        public decimal Percentage { get; set; }
    }
}
