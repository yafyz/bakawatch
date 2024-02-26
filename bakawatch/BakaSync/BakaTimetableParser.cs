using AngleSharp.Html.Parser;
using bakawatch.BakaSync.Entities;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace bakawatch.BakaSync
{
    public class BakaTimetableParser
    {
        HtmlParser htmlParser = new();
        BakaAPI bakaApi;

        public BakaTimetableParser(BakaAPI api)
        {
            bakaApi = api;
        }

        public async Task<BakaListAll> GetList()
        {
            var ret = new BakaListAll();

            var res = await bakaApi.Request(() => new HttpRequestMessage(HttpMethod.Get, $"Timetable/Public/"));
            var bodystr = await res.Content.ReadAsStringAsync();
            var doc = htmlParser.ParseDocument(bodystr);

            foreach (var item in doc.QuerySelectorAll("#selectedClass option[value]"))
            {
                var id = item.Attributes["value"].Value.Trim();
                var name = item.InnerHtml.Trim();
                ret.Classes.Add(id, name);
            }

            foreach (var item in doc.QuerySelectorAll("#selectedTeacher option[value]"))
            {
                var id = item.Attributes["value"].Value.Trim();
                var name = item.InnerHtml.Trim();
                ret.Teachers.Add(id, name);
            }

            foreach (var item in doc.QuerySelectorAll("#selectedRoom option[value]"))
            {
                var id = item.Attributes["value"].Value.Trim();
                var name = item.InnerHtml.Trim();
                ret.Rooms.Add(id, name);
            }

            return ret;
        }

        public class BakaListAll
        {
            public Dictionary<string, string> Classes = new();
            public Dictionary<string, string> Teachers = new();
            public Dictionary<string, string> Rooms = new();
        }

        public async Task<List<PeriodInfo>> Get(string what, Who who, When when = When.Actual)
        {
            var res = await bakaApi.Request(() => new HttpRequestMessage(HttpMethod.Get, $"Timetable/Public/{when}/{who}/{what}"));
            var bodystr = await res.Content.ReadAsStringAsync();
            var doc = htmlParser.ParseDocument(bodystr);

            if (doc.QuerySelector(".bk-timetable-main") == null)
                throw new BakaParseErrorNoTimetable();

            var a = doc.QuerySelectorAll(".day-item-hover[data-detail]")
                .Select(x =>
                {
                    var p = new PeriodInfo()
                    {
                        JsonData = JsonSerializer.Deserialize<PeriodInfoJSON>(x.Attributes["data-detail"]!.Value)!,
                        SubjectShortName = x.QuerySelector("div.middle")?.InnerHtml.Trim(),
                        TeacherShortName = x.QuerySelector("div.bottom span")?.InnerHtml.Trim(),
                        PeriodIndex = -1,
                        Date = DateOnly.MaxValue,
                    };

                    if (p.JsonData.type == "atom")
                        p.SubjectFullName = p.JsonData.subjecttext.Split('|')[0].Trim();

                    if (p.TeacherShortName == "")
                        p.TeacherShortName = null;

                    if (p.SubjectShortName == "")
                        p.SubjectShortName = null;

                    if (p.JsonData.absentinfo == "")
                        p.JsonData.absentinfo = null;

                    if (p.JsonData.InfoAbsentName == "")
                        p.JsonData.InfoAbsentName = null;

                    if (p.JsonData.removedinfo == "")
                        p.JsonData.removedinfo = null;

                    if (p.JsonData.changeinfo == "")
                        p.JsonData.changeinfo = null;

                    if (p.JsonData.group == "")
                        p.JsonData.group = null;

                    if (when == When.Permanent) {
                        // format: "L/S: SUBJECT"
                        if (p.SubjectShortName?.Contains(':') == true) {
                            var split = p.SubjectShortName
                                .Split(':')
                                .Select(x => x.Trim())
                                .ToArray();

                            (p.OddOrEvenWeek, p.SubjectShortName) = split switch {
                                ["L", var subjectName] => (OddEven.Odd, subjectName),
                                ["S", var subjectName] => (OddEven.Even, subjectName),
                                _ => throw new InvalidDataException($"invalid data '{p.SubjectShortName}'")
                            };
                        }
                    }

                    // hasAbsent is true when there is an absence without
                    // a substituted period, or atleast that's one of the cases
                    if (p.JsonData.hasAbsent
                     && p.JsonData.absentInfoText?.Contains('|') == true
                     // these shouldn't be set if hasAbsent is true, but just to be sure
                     && (p.JsonData.absentinfo == null || p.JsonData.InfoAbsentName == null)) {

                        // it seems identical, just in one field and delimited by '|',
                        // set the other absent fields for usage simplicity later on
                        var split = p.JsonData.absentInfoText.Split('|');
                        p.JsonData.absentinfo = split[0];
                        p.JsonData.InfoAbsentName = split[1];
                    }

                    if (!string.IsNullOrEmpty(p.JsonData.teacher))
                    {
                        var nameSplit = p.JsonData.teacher.Split(" ");
                        var off = 0;
                        // degrees may be before and also after a name
                        while (off < nameSplit.Length && nameSplit[off].EndsWith('.'))
                            off++;
                        
                        // teacher name may not actualy be a name
                        // and may contain dots and it will
                        // fuck everything because of degrees
                        // and non consistent name formats
                        if (nameSplit.Length-off < 2) {
                            // pray
                            p.TeacherFullNameNoDegree = p.JsonData.teacher;
                        } else {
                            var TeacherFirstName = nameSplit[off];
                            var TeacherLastName = nameSplit[off + 1];
                            p.TeacherFullNameNoDegree = string.Join(" ", TeacherFirstName, TeacherLastName);
                        }
                    }

                    return p;
                }).ToList();

            var now = DateTime.Now;
            foreach (var item in a)
            {
                var match = item.JsonData.type switch
                {
                    "atom" => Regex.Match(item.JsonData.subjecttext, "^.*? \\| (.*?) (.*?) \\| ([0-9]*?) .*?$"),
                    _ => Regex.Match(item.JsonData.subjecttext, "(.*?) (.*?) \\| ([0-9]*?) .*?$")
                };

                item.PeriodIndex = int.Parse(match.Groups[3].Value);

                var weekday = match.Groups[1].Value switch
                {
                    "po" => DayOfWeek.Monday,
                    "út" => DayOfWeek.Tuesday,
                    "st" => DayOfWeek.Wednesday,
                    "čt" => DayOfWeek.Thursday,
                    "pá" => DayOfWeek.Friday,
                    "so" => DayOfWeek.Saturday,
                    "ne" => DayOfWeek.Sunday,
                    var x => throw new Exception($"invalid weekday \"{x}\"")
                };

                // non permanent timetable
                if (match.Groups[2].Value != "")
                {
                    var split = match.Groups[2].Value.Split(".");
                    var dateStr = $"{split[1]}/{split[0]}/{DateTime.Now.Year}";
                    var date = DateOnly.Parse(dateStr, CultureInfo.InvariantCulture);

                    // yes i know this is like...
                    // not the greatest way to do it

                    if (date.DayOfWeek != weekday)
                    {
                        date = date.AddYears(1);
                    }

                    if (date.DayOfWeek != weekday)
                    {
                        date = date.AddYears(-2);
                    }

                    if (date.DayOfWeek != weekday)
                    {
                        throw new Exception("i am retarded");
                    }

                    item.Date = date;
                }
            }

            return a;
        }

        public enum When
        {
            Actual,
            Next,
            Permanent
        }

        public enum Who
        {
            Class,
            Teacher,
            Room
        }

        public class PeriodInfoJSON
        {
            public string type { get; set; }
            public string subjecttext { get; set; }
            public string teacher { get; set; }
            public string room { get; set; }
            public string? group { get; set; }
            public string theme { get; set; }
            public string notice { get; set; }
            public object homeworks { get; set; }

            public string? changeinfo { get; set; }
            public string? absentinfo { get; set; }
            public string? InfoAbsentName { get; set; }
            public string? removedinfo { get; set; }

            public string? absentInfoText { get; set; }
            public bool hasAbsent { get; set; }
        }

        public class PeriodInfo
        {
            public DateOnly Date;
            public int PeriodIndex;

            public OddEven? OddOrEvenWeek;

            public PeriodInfoJSON JsonData;
            public string? SubjectShortName;
            public string? SubjectFullName;
            public string? TeacherShortName;

            public string? TeacherFullNameNoDegree;
        }

        public class BakaParseErrorNoTimetable : Exception;
    }
}
