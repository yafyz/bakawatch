using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync.Entities
{
    [ComplexType]
    public record RoomBakaId(string Value);

    public class Room
    {
        public int ID { get; set; }

        // not set if IsFake=true
        // well null complex types are not yet supported
        // so it may be some garbage
        public RoomBakaId BakaId { get; set; }
        public bool IsFake { get; set; }

        public required string Name { get; set; }

        public required bool Active { get; set; }
    }
}
