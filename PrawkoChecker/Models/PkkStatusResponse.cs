using System.Collections.Generic;

namespace PrawkoChecker.Models
{
    public class PkkStatusResponse
    {
        public ulong NewestStatusDate { get; set; }
        public List<PkkStatus> StatusHistory { get; set; }
        public string Type { get; set; }
    }
}