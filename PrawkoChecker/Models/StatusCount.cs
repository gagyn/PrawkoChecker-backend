using System;

namespace PrawkoChecker.Models
{
    public class StatusCount : Entity
    {
        public string Pkk { get; set; }
        public int Count { get; set; }
        public DateTime UpdatedAt { get; private set; } = DateTime.Now;
    }
}
