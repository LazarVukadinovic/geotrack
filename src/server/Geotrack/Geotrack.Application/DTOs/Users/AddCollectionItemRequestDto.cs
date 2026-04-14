using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace Geotrack.Application.DTOs.Users;

public class AddCollectionItemRequestDto
{
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
}