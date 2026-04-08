using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Domain.Entities
{
    public class Section
    {
        public string Key { get; set; } = string.Empty;
        public string? Title { get; set; }
        public IEnumerable<Object> Items { get; set; } = new List<Object>();
    }
}
