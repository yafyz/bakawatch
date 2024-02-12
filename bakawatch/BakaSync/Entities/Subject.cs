using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync.Entities
{
    public class Subject
    {
        public int ID { get; set; }

        public required string ShortName { get; set; }
        public required string Name { get; set; }
    }
}
