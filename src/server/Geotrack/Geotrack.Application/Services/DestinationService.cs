using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Application.Interfaces;


namespace Geotrack.Application.Services
{
    public class DestinationService
    {

        private readonly IDestinationRepository destRepo;

        public DestinationService(IDestinationRepository destinationRepository)
        { 
            this.destRepo = destinationRepository;
        }

        public async Task<IEnumerable<dynamic>> ExploreByLocationAsync(double lat, double lon, double rad)
        { 
            var all = await  destRepo.GetAllAsync();

            return all.Select(d => new { d.Id,d.Name , d.Latitude,d.Longitude,DistanceKm = HaversineDistance(lat,lon,d.Latitude,d.Longitude)}).Where(d => d.DistanceKm <= rad).OrderBy(d => d.DistanceKm);
        }

        //Haversinova distanca zbog toga sto uracunava i zakrivljenje zemlje
        private static double HaversineDistance(double lat1,double lon1, double lat2, double lon2)
        {
            const double R = 6371;
            var dLat = (lat2 - lat1) * Math.PI / 180;
            var dLon = (lon2 - lon1) * Math.PI / 180;

            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
            Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            return 2 * R * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        }
    }
}
