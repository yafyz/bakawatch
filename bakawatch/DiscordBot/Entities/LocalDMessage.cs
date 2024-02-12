using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.DiscordBot.Entities
{
    public class LocalDMessage
    {
        [Key]
        public required ulong ID { get; set; }
        public required LocalDChannel Channel { get; set; }
    }
}
