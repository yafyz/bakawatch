using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync.Entities
{
    [ComplexType]
    public record TeacherBakaId(string Value);

    public class Teacher
    {
        public int ID { get; set; }

        public required TeacherBakaId BakaId { get; set; }
        public required string FullName { get; set; }

        public required bool Active { get; set; }
    }
}
