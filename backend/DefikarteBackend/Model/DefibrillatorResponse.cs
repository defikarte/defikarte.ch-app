using System;
using System.Collections.Generic;

namespace DefikarteBackend.Model
{
    public class DefibrillatorResponse
    {
        public int Latitude { get; set; }

        public int Longitude { get; set; }

        public long Id { get; set; }

        public int Type { get; set; }

        public Dictionary<string, string> Tags { get; set; } = new();

        public int ChangeSetId { get; set; }

        public bool Visible { get; set; }

        public DateTime TimeStamp { get; set; }

        public int Version { get; set; }

        public int UserId { get; set; }

        public string UserName { get; set; } = string.Empty;
    }
}