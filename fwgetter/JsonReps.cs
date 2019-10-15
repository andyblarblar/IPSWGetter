using System;
using System.Collections.Generic;
using System.Text;

namespace fwgetter
{
    public class JsonReps
    {
        public class device
        {
            public string name { get; set; }
            public string identifier { get; set; }
            public string platform { get; set; }
            public int cpid { get; set; }
            public int bdid { get; set; }

        }

        public class firmware
        {
            public string identifier { get; set; }
            public string version { get; set; }
            public string buildid { get; set; }
            public string sha1sum { get; set; }
            public string md5sum { get; set; }
            public long filesize { get; set; }
            public string url { get; set; }
            public string releasedate { get; set; }
            public string uploaddate { get; set; }
            public bool signed { get; set; }
        }

        public class FirmwareListing
        {
            public string name { get; set; }
            public string identifier { get; set; }
            public string platform { get; set; }
            public int cpid { get; set; }
            public int bdid { get; set; }

            public List<firmware> firmwares;


        }













    }
}
