using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync.Entities
{
    [ComplexType]
    public record ClassBakaId(string Value);

    public class Class
    {
        public int ID { get; set; }

        public required ClassBakaId BakaId { get; init; }
        public required string Name { get; set; }
        
        public required bool Active { get; set; }
    }
}
