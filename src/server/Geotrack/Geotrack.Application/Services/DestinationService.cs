using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Application.Interfaces;
using Geotrack.Domain.Entities;

namespace Geotrack.Application.Services
{
    public class DestinationService
    {

        private readonly IDestinationRepository destRepo;

        public DestinationService(IDestinationRepository destinationRepository)
        { 
            this.destRepo = destinationRepository;
        }

        public async Task<dynamic> ExploreByLocationAsync(double lat, double lon, double rad, int? userId)
        {
            var all = await destRepo.GetAllAsync();
            var nearest = all.Select(d => new
            {
                Destination = d,
                DistanceKm = HaversineDistance(lat, lon, d.Latitude, d.Longitude)
            }).OrderBy(d => d.DistanceKm).FirstOrDefault();

            if (nearest == null)
                return new { SelectedDestination = (object?)null, Sections = new List<object>() };

            var sections = new List<object>();
            if (nearest.Destination.Locations?.Any() == true)
                sections.Add(new Section
                {
                    Key = "local-places",
                    Title = "Local Places & Landmarks",
                    Items = nearest.Destination.Locations.Select(l => new
                    {l.Id,l.Name,l.Latitude,l.Longitude}).Cast<object>()
                });

            if (nearest.Destination.DestinationTags?.Any() == true)
                sections.Add(new Section
                {
                    Key = "tags",
                    Title = "Tags",
                    Items = nearest.Destination.DestinationTags
                        .Where(dt => dt.Tag != null)
                        .Select(dt => new
                        {dt.Tag.Id,dt.Tag.Name,dt.Tag.Description}).Cast<object>()
                });


            return (dynamic)new
            {
                SelectedDestination = nearest.Destination,
                Sections = sections
            };
        }

        //Haversinova distanca == Vazdusna linija
        private static double HaversineDistance(double lat1,double lon1, double lat2, double lon2)
        {
            const double R = 6371;
            var dLat = (lat2 - lat1) * Math.PI / 180;
            var dLon = (lon2 - lon1) * Math.PI / 180;

            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            return 2 * R * Math.Atan2(Math.Sqrt(a),Math.Sqrt(1 - a));

        }
    }
}
