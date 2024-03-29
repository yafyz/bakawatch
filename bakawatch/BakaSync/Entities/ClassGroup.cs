﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync.Entities
{
    public class ClassGroup
    {
        public int ID { get; set; }

        public required Class Class { get; set; }
        public required string Name { get; set; }
        public bool IsDefaultGroup { get; set; }

        public ICollection<LivePeriod> LivePeriods { get; set; }
        public ICollection<PermanentPeriod> PermanentPeriods { get; set; }

        public const string DefaultGroupName = "default";
    }
}
