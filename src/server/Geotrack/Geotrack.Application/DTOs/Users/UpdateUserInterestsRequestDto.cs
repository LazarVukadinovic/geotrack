using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace Geotrack.Application.DTOs.Users;

public class UpdateUserInterestsRequestDto
{
    public List<int> TagIds { get; set; } = new();
}